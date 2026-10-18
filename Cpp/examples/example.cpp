#include "cgh_sdk.h"
#include <cstring>
static const cgh_host_api* host;
static int32_t CGH_CALL echo(void*, const uint8_t* request, int32_t length, uint8_t* response, int32_t capacity) {
    if (capacity < length) return -3;
    if (length) std::memcpy(response, request, static_cast<size_t>(length));
    return length;
}
CGH_EXPORT int32_t CGH_CALL cgh_module_load(const cgh_host_api* api, const cgh_mod_info* mod) {
    if (!api || api->abi_version != 1 || api->size < sizeof(cgh_host_api)) return -1;
    host = api;
    host->log(host->context, "info", mod->is_isolated ? "Native module loaded in isolated MRT" : "Native module loaded in game");
    return host->register_service(host->context, "native.echo", &echo, nullptr);
}
CGH_EXPORT int32_t CGH_CALL cgh_module_start() {
    uint8_t response[64]{};
    const char* message = "native -> managed bridge OK";
    int result = host->call_service(host->context, "cgh.example", "managed.echo", reinterpret_cast<const uint8_t*>(message),
        static_cast<int32_t>(std::strlen(message)), response, sizeof(response));
    if (result < 0) return result;
    host->log(host->context, "info", "Native -> managed direct service call passed");
    return 0;
}
CGH_EXPORT void CGH_CALL cgh_module_message(const char*, const char* topic, const uint8_t*, int32_t) {
    if (std::strcmp(topic, "example.ping") == 0) host->log(host->context, "info", "Native received cross-process example.ping");
}
CGH_EXPORT void CGH_CALL cgh_module_stop() { host->log(host->context, "info", "Native module stopped"); }
