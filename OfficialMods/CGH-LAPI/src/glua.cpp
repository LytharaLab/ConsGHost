#include "api.h"
#include <windows.h>

#include <array>
#include <bit>
#include <cstring>

namespace {
using namespace lapi;
#ifdef LAPI_CLIENT
constexpr Realm realm = Realm::client;
#else
constexpr Realm realm = Realm::server;
#endif

struct lua_State;
using LuaFunction = int (__cdecl*)(lua_State*);
constexpr int registry = -10000;
constexpr int globals = -10002;
constexpr int lua_string = 4;
constexpr char owner_key[] = "cgh.lapi.owner.v1";

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
    void (*push_light)(lua_State*, void*);
    void (*push_closure)(lua_State*, LuaFunction, int);
    void (*push_string)(lua_State*, const char*, size_t);
    void (*push_int)(lua_State*, intptr_t);
    void (*push_nil)(lua_State*);
    void (*create_table)(lua_State*, int, int);
    void* (*new_userdata)(lua_State*, size_t);

    explicit Lua(HMODULE module)
        : get_field(symbol<decltype(get_field)>(module, "lua_getfield")),
          set_field(symbol<decltype(set_field)>(module, "lua_setfield")),
          set_top(symbol<decltype(set_top)>(module, "lua_settop")),
          type(symbol<decltype(type)>(module, "lua_type")),
          boolean(symbol<decltype(boolean)>(module, "lua_toboolean")),
          string(symbol<decltype(string)>(module, "lua_tolstring")),
          userdata(symbol<decltype(userdata)>(module, "lua_touserdata")),
          push_light(symbol<decltype(push_light)>(module, "lua_pushlightuserdata")),
          push_closure(symbol<decltype(push_closure)>(module, "lua_pushcclosure")),
          push_string(symbol<decltype(push_string)>(module, "lua_pushlstring")),
          push_int(symbol<decltype(push_int)>(module, "lua_pushinteger")),
          push_nil(symbol<decltype(push_nil)>(module, "lua_pushnil")),
          create_table(symbol<decltype(create_table)>(module, "lua_createtable")),
          new_userdata(symbol<decltype(new_userdata)>(module, "lua_newuserdata")) {}

    bool complete() const {
        return get_field && set_field && set_top && type && boolean && string && userdata &&
               push_light && push_closure && push_string && push_int && push_nil && create_table && new_userdata;
    }
};

const Lua& lua() {
    static const Lua api(GetModuleHandleW(L"lua_shared.dll"));
    return api;
}

const Api* bridge() {
    const auto module = GetModuleHandleW(L"cgh_lapi_win64.dll");
    if (!module) return nullptr;
    const auto get = symbol<GetApi>(module, "cgh_lapi_get_api");
    return get ? get(abi) : nullptr;
}

void* owner(lua_State* L) {
    lua().get_field(L, registry, owner_key);
    auto value = lua().userdata(L, -1);
    lua().set_top(L, -2);
    return value;
}

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
    const auto api = bridge();
    if (!api) return failure(L, unavailable);
    const auto status = api->attach(realm, owner(L), json, static_cast<int32_t>(size));
    if (status < 0) return failure(L, status);
    lua().push_int(L, status);
    return 1;
}

int check_version(lua_State* L) {
    const auto module = GetModuleHandleW(L"cgh_lapi_win64.dll");
    const auto check = module ? symbol<CheckVersion>(module, "cgh_lapi_check_version") : nullptr;
    if (!check) return failure(L, unavailable);
    std::array<char, max_version> output;
    const auto count = check(realm, owner(L), output.data(), max_version);
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
    const auto api = bridge();
    if (!api) return failure(L, unavailable);
    const auto current = owner(L);

    auto output = static_cast<char*>(lua().new_userdata(L, max_payload));
    const auto count = api->call(realm, current, mod, service, data, static_cast<int32_t>(size),
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
    const auto api = bridge();
    if (!api) return failure(L, unavailable);
    const auto status = api->publish(realm, owner(L), topic, data, static_cast<int32_t>(size));
    if (status < 0) return failure(L, status);
    lua().push_int(L, status);
    return 1;
}

int poll(lua_State* L) {
    const auto api = bridge();
    if (!api) return failure(L, unavailable);
    thread_local std::array<char, max_frame> frame;
    Kind kind{};
    const auto count = api->poll(realm, owner(L), &kind, frame.data(), max_frame);
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
    lua().push_closure(L, fn, 0);
    lua().set_field(L, -2, name);
}
}

extern "C" __declspec(dllexport) int gmod13_open(lua_State* L) {
    if (!lua().complete() || global_flag(L, "MENU_DLL") ||
        !global_flag(L, realm == Realm::server ? "SERVER" : "CLIENT")) return 0;
    lua().push_light(L, L);
    lua().set_field(L, registry, owner_key);
    lua().create_table(L, 0, 5);
    set_method(L, "_Attach", &attach);
    set_method(L, "_CheckVersion", &check_version);
    set_method(L, "_Poll", &poll);
    set_method(L, "Call", &call);
    set_method(L, "Publish", &publish);
    lua().set_field(L, globals, "cgh");
    return 0;
}

extern "C" __declspec(dllexport) int gmod13_close(lua_State* L) {
    if (lua().complete()) {
        if (const auto api = bridge()) api->detach(realm, owner(L));
    }
    return 0;
}
