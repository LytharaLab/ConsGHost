#include "api.h"
#include <windows.h>

#include <array>
#include <bit>
#include <condition_variable>
#include <cstring>
#include <mutex>
#include <string_view>

namespace {
using namespace lapi;
struct lua_State;
using LuaFunction = int (__cdecl*)(lua_State*);
using LoadBuffer = int (__cdecl*)(lua_State*, const char*, size_t, const char*, const char*);
constexpr int registry = -10000;
constexpr int globals = -10002;
constexpr int owner_upvalue = globals - 1;
constexpr int lua_string = 4;
constexpr int lua_table = 5;
constexpr char bridge_key[] = "cgh.lapi.bridge.v2";
constexpr std::string_view bootstrap_name = "CGH-LAPI/0.2.0/bootstrap";
constexpr std::string_view bootstrap_source = "return nil";

std::mutex hook_mutex;
std::condition_variable hook_idle;
void* load_buffer_target = nullptr;
void* load_buffer_trampoline = nullptr;
size_t active_compiles = 0;
bool hook_enabled = false;

template<class Fn>
Fn symbol(HMODULE module, const char* name) {
    return std::bit_cast<Fn>(GetProcAddress(module, name));
}

struct Lua {
    void (*get_field)(lua_State*, int, const char*);
    void (*set_field)(lua_State*, int, const char*);
    void (*set_top)(lua_State*, int);
    int (*type)(lua_State*, int);
    int (*boolean)(lua_State*, int);
    const char* (*string)(lua_State*, int, size_t*);
    void* (*userdata)(lua_State*, int);
    void (*push_value)(lua_State*, int);
    void (*push_closure)(lua_State*, LuaFunction, int);
    void (*push_string)(lua_State*, const char*, size_t);
    void (*push_int)(lua_State*, intptr_t);
    void (*push_nil)(lua_State*);
    void (*create_table)(lua_State*, int, int);
    void* (*new_userdata)(lua_State*, size_t);
    int (*set_metatable)(lua_State*, int);

    explicit Lua(HMODULE module)
        : get_field(symbol<decltype(get_field)>(module, "lua_getfield")),
          set_field(symbol<decltype(set_field)>(module, "lua_setfield")),
          set_top(symbol<decltype(set_top)>(module, "lua_settop")),
          type(symbol<decltype(type)>(module, "lua_type")),
          boolean(symbol<decltype(boolean)>(module, "lua_toboolean")),
          string(symbol<decltype(string)>(module, "lua_tolstring")),
          userdata(symbol<decltype(userdata)>(module, "lua_touserdata")),
          push_value(symbol<decltype(push_value)>(module, "lua_pushvalue")),
          push_closure(symbol<decltype(push_closure)>(module, "lua_pushcclosure")),
          push_string(symbol<decltype(push_string)>(module, "lua_pushlstring")),
          push_int(symbol<decltype(push_int)>(module, "lua_pushinteger")),
          push_nil(symbol<decltype(push_nil)>(module, "lua_pushnil")),
          create_table(symbol<decltype(create_table)>(module, "lua_createtable")),
          new_userdata(symbol<decltype(new_userdata)>(module, "lua_newuserdata")),
          set_metatable(symbol<decltype(set_metatable)>(module, "lua_setmetatable")) {}

    bool complete() const {
        return get_field && set_field && set_top && type && boolean && string && userdata &&
               push_value && push_closure && push_string && push_int && push_nil && create_table &&
               new_userdata && set_metatable;
    }
};

const Lua& lua() {
    static const Lua api(GetModuleHandleW(L"lua_shared.dll"));
    return api;
}

const Api& bridge() { return *cgh_lapi_get_api(abi); }
const Realm* owner(lua_State* L) { return static_cast<const Realm*>(lua().userdata(L, owner_upvalue)); }

bool global_flag(lua_State* L, const char* flag) {
    lua().get_field(L, globals, flag);
    const auto value = lua().boolean(L, -1) != 0;
    lua().set_top(L, -2);
    return value;
}

const char* text_arg(lua_State* L, int index, size_t& size, size_t limit) {
    if (lua().type(L, index) != lua_string) return nullptr;
    const auto value = lua().string(L, index, &size);
    return size <= limit ? value : nullptr;
}

const char* name_arg(lua_State* L, int index, size_t limit) {
    size_t size = 0;
    const auto value = text_arg(L, index, size, limit);
    return value && size != 0 && !std::memchr(value, '\0', size) ? value : nullptr;
}

int failure(lua_State* L, int code) {
    lua().push_nil(L);
    lua().push_int(L, code);
    return 2;
}

int attach(lua_State* L) {
    size_t size = 0;
    const auto json = text_arg(L, 1, size, max_version);
    if (!json) return failure(L, invalid);
    const auto current = owner(L);
    const auto status = bridge().attach(*current, current, json, static_cast<int32_t>(size));
    if (status < 0) return failure(L, status);
    lua().push_int(L, status);
    return 1;
}

int check_version(lua_State* L) {
    const auto current = owner(L);
    std::array<char, max_version> output;
    const auto count = cgh_lapi_check_version(*current, current, output.data(), max_version);
    if (count < 0) return failure(L, count);
    if (count > max_version) return failure(L, -3);
    lua().push_string(L, output.data(), static_cast<size_t>(count));
    return 1;
}

int call(lua_State* L) {
    const auto mod = name_arg(L, 1, 64);
    const auto service = name_arg(L, 2, max_name);
    size_t size = 0;
    const auto data = text_arg(L, 3, size, max_payload);
    if (!mod || !service || !data) return failure(L, invalid);
    const auto current = owner(L);

    auto output = static_cast<char*>(lua().new_userdata(L, max_payload));
    const auto count = bridge().call(*current, current, mod, service, data, static_cast<int32_t>(size),
                                    output, max_payload);
    if (count < 0) return failure(L, count);
    if (count > max_payload) return failure(L, -3);
    lua().push_string(L, output, static_cast<size_t>(count));
    return 1;
}

int publish(lua_State* L) {
    const auto topic = name_arg(L, 1, max_name);
    size_t size = 0;
    const auto data = text_arg(L, 2, size, max_payload);
    if (!topic || !data) return failure(L, invalid);
    const auto current = owner(L);
    const auto status = bridge().publish(*current, current, topic, data, static_cast<int32_t>(size));
    if (status < 0) return failure(L, status);
    lua().push_int(L, status);
    return 1;
}

int poll(lua_State* L) {
    const auto current = owner(L);
    thread_local std::array<char, max_frame> frame;
    Kind kind{};
    const auto count = bridge().poll(*current, current, &kind, frame.data(), max_frame);
    if (count == 0) return 0;
    if (count < 0) return failure(L, count);
    const auto delimiter = static_cast<const char*>(std::memchr(frame.data(), '\0', static_cast<size_t>(count)));
    if (!delimiter) return failure(L, invalid);
    const auto name_size = static_cast<size_t>(delimiter - frame.data());
    lua().push_int(L, static_cast<int32_t>(kind));
    lua().push_string(L, frame.data(), name_size);
    lua().push_string(L, delimiter + 1, static_cast<size_t>(count) - name_size - 1);
    return 3;
}

void set_method(lua_State* L, const char* name, LuaFunction fn) {
    // The userdata below the table is shared by all closures in this realm.
    lua().push_value(L, -2);
    lua().push_closure(L, fn, 1);
    lua().set_field(L, -2, name);
}

int close_realm(lua_State* L) {
    const auto current = static_cast<const Realm*>(lua().userdata(L, 1));
    if (current) bridge().detach(*current, current);
    return 0;
}

int open_realm(lua_State* L) {
    if (global_flag(L, "MENU_DLL")) return 0;
    const bool server = global_flag(L, "SERVER");
    if (!server && !global_flag(L, "CLIENT")) return 0;

    lua().get_field(L, registry, bridge_key);
    if (lua().type(L, -1) == lua_table) return 1;
    lua().set_top(L, -2);

    auto current = static_cast<Realm*>(lua().new_userdata(L, sizeof(Realm)));
    *current = server ? Realm::server : Realm::client;
    lua().create_table(L, 0, 1);
    lua().push_closure(L, &close_realm, 0);
    lua().set_field(L, -2, "__gc");
    lua().set_metatable(L, -2);

    lua().create_table(L, 0, 5);
    set_method(L, "_Attach", &attach);
    set_method(L, "_CheckVersion", &check_version);
    set_method(L, "_Poll", &poll);
    set_method(L, "Call", &call);
    set_method(L, "Publish", &publish);
    lua().push_value(L, -1);
    lua().set_field(L, registry, bridge_key);
    return 1;
}

bool is_bootstrap(const char* source, size_t size, const char* name) {
    if (!name || size != bootstrap_source.size()) return false;
    if (*name == '=' || *name == '@') ++name;
    return bootstrap_name == name && bootstrap_source == std::string_view(source, size);
}

int compile(lua_State* L, const char* source, size_t size, const char* name, const char* mode) {
    LoadBuffer original;
    bool enabled;
    {
        std::scoped_lock lock(hook_mutex);
        enabled = hook_enabled;
        original = std::bit_cast<LoadBuffer>(enabled ? load_buffer_trampoline : load_buffer_target);
        if (enabled) ++active_compiles;
    }
    const auto result = original(L, source, size, name, mode);
    if (!enabled) return result;
    {
        std::scoped_lock lock(hook_mutex);
        if (--active_compiles == 0) hook_idle.notify_all();
    }

    // Replace only the successfully compiled bootstrap chunk with the native factory.
    // No Lua execution or DLL loading happens inside the compilation hook.
    if (result == 0 && is_bootstrap(source, size, name)) {
        lua().set_top(L, -2);
        lua().push_closure(L, &open_realm, 0);
    }
    return result;
}
}

bool lapi::install_glua(const cgh_host_api& host) {
    HMODULE module;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN, L"lua_shared.dll", &module)) return false;

    const auto target = std::bit_cast<void*>(GetProcAddress(module, "luaL_loadbufferx"));
    if (target && lua().complete() && host.create_hook && host.enable_hook) {
        std::scoped_lock lock(hook_mutex);
        load_buffer_target = target;
        hook_enabled = host.create_hook(host.context, target, std::bit_cast<void*>(&compile),
                                        &load_buffer_trampoline) == 0;
        if (hook_enabled) return true;
    }
    if (host.log) host.log(host.context, "warn", "Could not install the CGH-LAPI GLua bootstrap.");
    return true;
}

void lapi::stop_glua(const cgh_host_api& host) {
    if (!hook_enabled) return;

    // CGH removes the trampoline after Stop returns. Drain calls already using it.
    host.enable_hook(host.context, load_buffer_target, 0);
    std::unique_lock lock(hook_mutex);
    hook_enabled = false;
    hook_idle.wait(lock, [] { return active_compiles == 0; });
}
