#include "api.h"
#include "cgh_sdk.h"
#include <windows.h>

#include <array>
#include <bit>
#include <cstring>
#include <iostream>
#include <map>
#include <stdexcept>
#include <string>
#include <string_view>

namespace {
using namespace lapi;
std::map<std::string, cgh_service_fn> services;
std::string published_topic, published_payload;
char server_owner, client_owner, replacement_owner;

int32_t CGH_CALL register_service(void*, const char* name, cgh_service_fn fn, void*) {
    return services.emplace(name, fn).second ? 0 : -2;
}
int32_t CGH_CALL call_service(void*, const char* mod, const char* service,
                            const uint8_t* input, int32_t size, uint8_t* output, int32_t capacity) {
    if (std::string_view(mod) == "cgh.lapi") {
        auto entry = services.find(service);
        return entry == services.end() ? -1 : entry->second(nullptr, input, size, output, capacity);
    }
    if (std::string_view(mod) != "test" || std::string_view(service) != "echo") return -1;
    if (capacity < size) return -3;
    if (size) std::memcpy(output, input, static_cast<size_t>(size));
    return size;
}
int32_t CGH_CALL publish(void*, const char* topic, const uint8_t* input, int32_t size) {
    published_topic = topic;
    published_payload.assign(reinterpret_cast<const char*>(input), static_cast<size_t>(size));
    return 0;
}

void check(bool condition, const char* name) {
    if (!condition) throw std::runtime_error(name);
    std::cout << "PASS " << name << '\n';
}

template<class Fn>
Fn export_fn(HMODULE module, const char* name) {
    const auto fn = GetProcAddress(module, name);
    if (!fn) throw std::runtime_error(name);
    return std::bit_cast<Fn>(fn);
}

void run(HMODULE module) {
    auto load = export_fn<cgh_module_load_fn>(module, "cgh_module_load");
    auto start = export_fn<cgh_module_start_fn>(module, "cgh_module_start");
    auto message = export_fn<cgh_module_message_fn>(module, "cgh_module_message");
    auto stop = export_fn<cgh_module_stop_fn>(module, "cgh_module_stop");
    auto get_api = export_fn<GetApi>(module, "cgh_lapi_get_api");
    auto check_version = export_fn<CheckVersion>(module, "cgh_lapi_check_version");
    const auto api = get_api(abi);
    check(api && !get_api(abi + 1), "ABI negotiation");

    cgh_host_api host{};
    host.size = sizeof(host);
    host.abi_version = 1;
    host.register_service = &register_service;
    host.call_service = &call_service;
    host.publish = &publish;
    cgh_mod_info info{};
    info.size = sizeof(info);
    info.is_isolated = 1;
    check(load(&host, &info) == -1, "reject isolated bridge");
    info.is_isolated = 0;
    check(load(&host, &info) == 0 && services.size() == 6, "register services");
    struct StopOnExit {
        cgh_module_stop_fn stop;
        ~StopOnExit() { stop(); }
    } stop_on_exit{stop};
    check(api->attach(Realm::server, &server_owner, "{}", 2) == unavailable, "wait for CGH Start");
    check(start() == 0, "start");

    const std::string version = R"({"version":261109,"realm":"server"})";
    check(api->attach(Realm::server, &server_owner, version.data(), static_cast<int32_t>(version.size())) == 0,
          "attach server version");
    check(api->attach(Realm::client, &client_owner, "{}", 2) == 0, "attach independent client");
    std::array<char, max_frame> out{};
    auto count = call_service(nullptr, "cgh.lapi", "version.server", nullptr, 0,
                              reinterpret_cast<uint8_t*>(out.data()), max_frame);
    check(count > 0 && std::string_view(out.data(), static_cast<size_t>(count)) == version, "read session version");
    count = call_service(nullptr, "cgh.lapi", "version.client", nullptr, 0,
                         reinterpret_cast<uint8_t*>(out.data()), max_frame);
    check(count == 2 && std::string_view(out.data(), 2) == "{}", "query separate realm version");
    count = check_version(Realm::server, &server_owner, out.data(), max_frame);
    check(count > 0 && std::string_view(out.data(), static_cast<size_t>(count)) == version,
          "self-test round trips through the registered version service");
    count = check_version(Realm::client, &client_owner, out.data(), max_frame);
    check(count == 2 && std::string_view(out.data(), 2) == "{}", "self-test selects its own realm");
    check(check_version(Realm::server, &client_owner, out.data(), max_frame) == unavailable,
          "self-test rejects a mismatched owner");
    message("isolated.test", "cgh.lapi.version.server", reinterpret_cast<const uint8_t*>("id"), 2);
    check(published_topic == "cgh.lapi.version.reply.isolated.test" &&
          published_payload == std::string("id\0", 3) + version, "isolated session version reply");
    check(api->call(Realm::server, &server_owner, "cgh.lapi", "client.hook", "", 0, out.data(), max_frame) ==
          invalid, "Lua cannot use internal Mod services to force another realm's hooks");

    const std::string_view binary("a\0b", 3);
    count = api->call(Realm::server, &server_owner, "test", "echo", binary.data(), 3, out.data(), max_frame);
    check(count == 3 && std::string_view(out.data(), 3) == binary, "Lua-to-Mod binary echo");
    check(api->call(Realm::server, &server_owner, "test", "missing", "", 0, out.data(), max_frame) == -1,
          "missing service");
    check(api->call(Realm::server, &server_owner, "test", "echo", binary.data(), 3, out.data(), 1) == -3,
          "short response buffer");
    check(api->publish(Realm::server, &server_owner, "test.request", binary.data(), 3) == 0 &&
          published_topic == "test.request" && published_payload == binary, "Lua-to-Mod publish");

    auto enqueue = [&](const char* service, std::string_view frame) {
        return call_service(nullptr, "cgh.lapi", service, reinterpret_cast<const uint8_t*>(frame.data()),
                            static_cast<int32_t>(frame.size()), nullptr, 0);
    };
    const std::string_view frame("ProbeEvent\0a\0b", 14);
    check(enqueue("server.emit", frame) == 0 && enqueue("server.hook", frame) == 0, "Mod-to-Lua emit and force hook");
    check(enqueue("server.emit", "invalid") == invalid, "reject malformed event");
    Kind kind{};
    check(api->poll(Realm::client, &client_owner, &kind, out.data(), max_frame) == 0, "realm isolation");
    check(api->poll(Realm::server, &server_owner, &kind, out.data(), 1) == -3, "short poll keeps event");
    count = api->poll(Realm::server, &server_owner, &kind, out.data(), max_frame);
    check(count == 14 && kind == Kind::message && std::string_view(out.data(), 14) == frame, "poll binary message");
    count = api->poll(Realm::server, &server_owner, &kind, out.data(), max_frame);
    check(count == 14 && kind == Kind::hook, "poll named hook");

    message("isolated.test", "cgh.lapi.client.hook", reinterpret_cast<const uint8_t*>(frame.data()), 14);
    check(api->poll(Realm::client, &client_owner, &kind, out.data(), max_frame) == 14 && kind == Kind::hook,
          "isolated Mod hook routing");
    bool filled = true;
    for (int i = 0; i < queue_capacity; ++i) filled &= enqueue("server.emit", frame) == 0;
    check(filled && enqueue("server.emit", frame) == queue_full, "bounded queue");

    check(api->attach(Realm::server, &replacement_owner, "{}", 2) == 0, "replace realm");
    api->detach(Realm::server, &server_owner);
    check(api->poll(Realm::server, &server_owner, &kind, out.data(), max_frame) == unavailable &&
          api->poll(Realm::server, &replacement_owner, &kind, out.data(), max_frame) == 0,
          "realm replacement drops stale events and rejects stale owner");
    api->detach(Realm::server, &replacement_owner);
    check(enqueue("server.emit", frame) == unavailable, "detach clears realm");
    stop();
    check(api->publish(Realm::client, &client_owner, "test", "", 0) == unavailable, "stop invalidates bridge");
}
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    const auto module = LoadLibraryW(argv[1]);
    if (!module) return 2;
    try { run(module); }
    catch (const std::exception& error) {
        std::cerr << "FAIL " << error.what() << '\n';
        FreeLibrary(module);
        return 1;
    }
    FreeLibrary(module);
    return 0;
}
