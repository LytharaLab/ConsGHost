#include "api.h"
#include <windows.h>

#include <array>
#include <bit>
#include <cstring>
#include <deque>
#include <mutex>
#include <string>
#include <string_view>
#include <utility>

namespace {
using namespace lapi;

struct Event {
    Kind kind;
    std::string frame;
};
struct RealmState {
    const void* owner = nullptr;
    std::string version;
    std::deque<Event> events;
};
std::mutex state_mutex;
std::array<RealmState, 2> states;
const cgh_host_api* host = nullptr;
bool started = false;
PTP_TIMER console_timer = nullptr;
ULONGLONG console_deadline = 0;
bool console_pending = false;
bool glua_pending = false;

using ConsoleMsg = void (__cdecl*)(const char*, ...);

ConsoleMsg find_console() {
    const auto engine = GetModuleHandleW(L"engine.dll");
    const auto tier0 = GetModuleHandleW(L"tier0.dll");
    if (!engine || !tier0 || !GetModuleHandleW(L"GameUI.dll")) return nullptr;

    // Wait for the engine's spew handler before sending anything to tier0.
    using GetSpewOutput = FARPROC (__cdecl*)();
    const auto get_output = std::bit_cast<GetSpewOutput>(GetProcAddress(tier0, "GetSpewOutputFunc"));
    const auto output = get_output ? get_output() : nullptr;
    HMODULE sink = nullptr;
    if (!output || !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, std::bit_cast<LPCWSTR>(output), &sink) ||
        sink != engine) return nullptr;
    // engine.dll imports this overload from the x64 tier0.dll.
    return std::bit_cast<ConsoleMsg>(GetProcAddress(tier0, "?ConMsg@@YAXPEBDZZ"));
}

void CALLBACK report_startup(PTP_CALLBACK_INSTANCE, void*, PTP_TIMER timer) {
    const cgh_host_api* api;
    {
        std::scoped_lock lock(state_mutex);
        if (timer != console_timer) return;
        api = host;
    }

    const bool expired = GetTickCount64() >= console_deadline;
    if (console_pending) {
        if (const auto print = find_console()) {
            print("%s", "[CGH-LAPI] v" LAPI_VERSION " startup test: Source console online.\n");
            console_pending = false;
        } else if (expired) {
            if (api->log) api->log(api->context, "warn", "Source console startup test timed out.");
            console_pending = false;
        }
    }
    if (glua_pending) {
        if (install_glua(*api)) glua_pending = false;
        else if (expired) {
            if (api->log) api->log(api->context, "warn", "CGH-LAPI timed out waiting for lua_shared.dll.");
            glua_pending = false;
        }
    }

    std::scoped_lock lock(state_mutex);
    if (timer == console_timer && (console_pending || glua_pending)) {
        auto due = std::bit_cast<FILETIME>(-1000000LL); // 100 ms, in 100 ns units.
        SetThreadpoolTimer(timer, &due, 0, 0);
    }
}

RealmState& state(Realm realm) { return states[static_cast<size_t>(realm)]; }

const cgh_host_api* active_host(Realm realm, const void* owner) {
    std::scoped_lock lock(state_mutex);
    return started && owner && state(realm).owner == owner ? host : nullptr;
}

int32_t copy_response(std::string_view bytes, void* out, int32_t capacity) {
    if (capacity < 0 || bytes.size() > static_cast<size_t>(capacity)) return -3;
    if (!bytes.empty()) std::memcpy(out, bytes.data(), bytes.size());
    return static_cast<int32_t>(bytes.size());
}

int32_t __cdecl attach(Realm realm, const void* owner, const char* json, int32_t size) {
    if (!owner || !json || size <= 0 || size > max_version) return invalid;
    try {
        std::scoped_lock lock(state_mutex);
        if (!started) return unavailable;
        auto& current = state(realm);
        current.version.assign(json, static_cast<size_t>(size));
        if (current.owner != owner) current.events.clear();
        current.owner = owner;
        return 0;
    } catch (...) { return -2; }
}

void __cdecl detach(Realm realm, const void* owner) {
    std::scoped_lock lock(state_mutex);
    auto& current = state(realm);
    if (current.owner != owner) return;
    current.owner = nullptr;
    current.version.clear();
    current.events.clear();
}

int32_t __cdecl call(Realm realm, const void* owner, const char* mod, const char* service,
                     const char* data, int32_t size, char* out, int32_t capacity) {
    auto api = active_host(realm, owner);
    if (!api) return unavailable;
    // Internal LAPI services are Mod-facing. GLua gets its own version from GetVersion.
    if (std::strcmp(mod, "cgh.lapi") == 0) return invalid;
    return api->call_service(api->context, mod, service, reinterpret_cast<const uint8_t*>(data), size,
                             reinterpret_cast<uint8_t*>(out), capacity);
}

int32_t __cdecl publish(Realm realm, const void* owner, const char* topic, const char* data, int32_t size) {
    auto api = active_host(realm, owner);
    if (!api) return unavailable;
    return api->publish(api->context, topic, reinterpret_cast<const uint8_t*>(data), size);
}

int32_t __cdecl poll(Realm realm, const void* owner, Kind* kind, char* out, int32_t capacity) {
    std::scoped_lock lock(state_mutex);
    auto& current = state(realm);
    if (!started || !owner || current.owner != owner) return unavailable;
    if (current.events.empty()) return 0;
    const auto& event = current.events.front();
    const auto size = copy_response(event.frame, out, capacity);
    if (size < 0) return size;
    *kind = event.kind;
    current.events.pop_front();
    return size;
}

int32_t enqueue(Realm realm, Kind kind, const uint8_t* input, int32_t size) {
    if (!input || size < 2 || size > max_frame) return invalid;
    std::string_view frame(reinterpret_cast<const char*>(input), static_cast<size_t>(size));
    const auto delimiter = frame.find('\0');
    if (delimiter == 0 || delimiter == std::string_view::npos || delimiter > max_name ||
        frame.size() - delimiter - 1 > max_payload) return invalid;
    try {
        std::scoped_lock lock(state_mutex);
        auto& current = state(realm);
        if (!started || !current.owner) return unavailable;
        if (current.events.size() >= queue_capacity) return queue_full;
        current.events.push_back({kind, std::string(frame)});
        return 0;
    } catch (...) { return -2; }
}

template<Realm realm>
int32_t CGH_CALL version_service(void*, const uint8_t*, int32_t, uint8_t* out, int32_t capacity) {
    std::scoped_lock lock(state_mutex);
    const auto& current = state(realm);
    if (!started || !current.owner) return unavailable;
    return copy_response(current.version, out, capacity);
}

template<Realm realm, Kind kind>
int32_t CGH_CALL event_service(void*, const uint8_t* input, int32_t size, uint8_t*, int32_t) {
    return enqueue(realm, kind, input, size);
}

void send_version(Realm realm, std::string_view source, const uint8_t* input, int32_t size) {
    if (source.empty() || source.size() > 64 || !input || size <= 0 || size > max_name ||
        std::memchr(input, 0, static_cast<size_t>(size))) return;
    try {
        std::string reply(reinterpret_cast<const char*>(input), static_cast<size_t>(size));
        reply.push_back('\0');
        const cgh_host_api* api;
        {
            std::scoped_lock lock(state_mutex);
            if (!started) return;
            api = host;
            const auto& current = state(realm);
            reply += current.owner ? current.version : R"({"error":-4})";
        }
        const auto topic = "cgh.lapi.version.reply." + std::string(source);
        api->publish(api->context, topic.c_str(), reinterpret_cast<const uint8_t*>(reply.data()),
                     static_cast<int32_t>(reply.size()));
    } catch (...) { /* asynchronous delivery has no caller to report an allocation failure to */ }
}

const Api bridge = {abi, &attach, &detach, &call, &publish, &poll};
}

CGH_EXPORT const lapi::Api* CGH_CALL cgh_lapi_get_api(uint32_t version) {
    return version == lapi::abi ? &bridge : nullptr;
}

CGH_EXPORT int32_t CGH_CALL cgh_lapi_check_version(lapi::Realm realm, const void* owner, char* out, int32_t capacity) {
    if ((realm != lapi::Realm::server && realm != lapi::Realm::client) || !out || capacity <= 0)
        return lapi::invalid;
    const auto api = active_host(realm, owner);
    if (!api) return lapi::unavailable;
    const auto service = realm == lapi::Realm::server ? "version.server" : "version.client";
    return api->call_service(api->context, "cgh.lapi", service, nullptr, 0,
                             reinterpret_cast<uint8_t*>(out), capacity);
}

CGH_EXPORT int32_t CGH_CALL cgh_module_load(const cgh_host_api* api, const cgh_mod_info* info) {
    if (!api || api->abi_version != 1 || api->size < sizeof(cgh_host_api) ||
        !info || info->size < sizeof(cgh_mod_info) || info->is_isolated) return -1;
    using lapi::Kind;
    using lapi::Realm;
    const std::pair<const char*, cgh_service_fn> services[] = {
        {"version.server", version_service<Realm::server>},
        {"version.client", version_service<Realm::client>},
        {"server.emit", event_service<Realm::server, Kind::message>},
        {"client.emit", event_service<Realm::client, Kind::message>},
        {"server.hook", event_service<Realm::server, Kind::hook>},
        {"client.hook", event_service<Realm::client, Kind::hook>}
    };
    for (const auto& [name, service] : services) {
        if (api->register_service(api->context, name, service, nullptr) != 0) return -2;
    }
    std::scoped_lock lock(state_mutex);
    host = api;
    return 0;
}

CGH_EXPORT int32_t CGH_CALL cgh_module_start() {
    std::scoped_lock lock(state_mutex);
    if (!host) return -1;
    if (started) return 0;
    started = true;
    console_timer = CreateThreadpoolTimer(report_startup, nullptr, nullptr);
    if (console_timer) {
        auto due = std::bit_cast<FILETIME>(-1000000LL); // 100 ms, in 100 ns units.
        console_deadline = GetTickCount64() + 120000;
        console_pending = true;
        glua_pending = host->create_hook != nullptr;
        SetThreadpoolTimer(console_timer, &due, 0, 0);
    } else if (host->log) {
        host->log(host->context, "warn", "Could not schedule CGH-LAPI startup.");
    }
    return 0;
}

CGH_EXPORT void CGH_CALL cgh_module_message(const char* source, const char* topic, const uint8_t* input, int32_t size) {
    if (!source || !topic || std::strcmp(source, "cgh.lapi") == 0) return;
    const std::string_view name(topic);
    if (name == "cgh.lapi.server.emit") enqueue(Realm::server, Kind::message, input, size);
    if (name == "cgh.lapi.client.emit") enqueue(Realm::client, Kind::message, input, size);
    if (name == "cgh.lapi.server.hook") enqueue(Realm::server, Kind::hook, input, size);
    if (name == "cgh.lapi.client.hook") enqueue(Realm::client, Kind::hook, input, size);
    if (name == "cgh.lapi.version.server") send_version(Realm::server, source, input, size);
    if (name == "cgh.lapi.version.client") send_version(Realm::client, source, input, size);
}

CGH_EXPORT void CGH_CALL cgh_module_stop() {
    PTP_TIMER timer;
    const cgh_host_api* api;
    {
        std::scoped_lock lock(state_mutex);
        started = false;
        api = std::exchange(host, nullptr);
        timer = std::exchange(console_timer, nullptr);
        for (auto& current : states) {
            current.owner = nullptr;
            current.version.clear();
            current.events.clear();
        }
    }
    if (timer) {
        SetThreadpoolTimer(timer, nullptr, 0, 0);
        WaitForThreadpoolTimerCallbacks(timer, TRUE);
        CloseThreadpoolTimer(timer);
    }
    if (api) stop_glua(*api);
}
