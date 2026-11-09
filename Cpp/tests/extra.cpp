#include <windows.h>
#include "cgh_sdk.h"
#include <string_view>

extern "C" __declspec(dllexport) int cgh_extra_probe() { return 42; }
BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }

// Also loaded as a mod entry point in the configuration acceptance fixture.
CGH_EXPORT int32_t CGH_CALL cgh_module_load(const cgh_host_api* api, const cgh_mod_info*) {
    if (api->abi_version != 1 || api->size < sizeof(cgh_host_api_config)) return -1;
    const auto* config = reinterpret_cast<const cgh_host_api_config*>(api);
    uint8_t response[256]{};
    int32_t length = config->config_read(api->context, nullptr, 0);
    if (length <= 0 || length > static_cast<int32_t>(sizeof(response))) return -2;
    if (config->config_read(api->context, response, sizeof(response)) != length) return -3;
    std::string_view json(reinterpret_cast<const char*>(response), static_cast<size_t>(length));
    if (json.find("\"nativeProbe\":41") == std::string_view::npos) return -4;
    const uint8_t value[] = {'4', '2'};
    return config->config_set(api->context, "nativeProbe", value, sizeof(value));
}
