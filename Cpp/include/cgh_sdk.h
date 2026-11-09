#pragma once
#include <stdint.h>
#include <stddef.h>

#ifdef _WIN32
#define CGH_CALL __cdecl
#define CGH_EXPORT extern "C" __declspec(dllexport)
#else
#error ConsGHost currently supports Windows x64 only
#endif

// All strings UTF-8, all buffers borrowed for the duration of the call. ABI v1.
// No C++ exceptions may escape a callback. Service handlers can run concurrently.
typedef int32_t (CGH_CALL* cgh_service_fn)(void* user, const uint8_t* request, int32_t request_size,
                                         uint8_t* response, int32_t response_capacity);
struct cgh_mod_info {
    uint32_t size;
    const char* id;
    const char* mod_directory;
    const char* resources_directory;
    const char* data_directory;
    int32_t is_isolated;
};
struct cgh_host_api {
    uint32_t size;
    uint32_t abi_version;
    void* context;
    void (CGH_CALL* log)(void*, const char* level, const char* message);
    // Service names belong to this mod. Duplicate names are rejected. Valid during Load/Start.
    int32_t (CGH_CALL* register_service)(void*, const char* name, cgh_service_fn handler, void* user);
    int32_t (CGH_CALL* call_service)(void*, const char* target_mod, const char* service,
                                    const uint8_t*, int32_t, uint8_t*, int32_t);
    // Queued, bounded cross-process delivery. Payload max 256 KiB. 0 accepted; -2 queue full/error.
    int32_t (CGH_CALL* publish)(void*, const char* topic, const uint8_t*, int32_t);
    void* (CGH_CALL* find_export)(void*, const char* library_name, const char* export_name);
    int32_t (CGH_CALL* create_hook)(void*, void* target, void* detour, void** trampoline);
    int32_t (CGH_CALL* enable_hook)(void*, void* target, int32_t enabled);
    int32_t (CGH_CALL* remove_hook)(void*, void* target);
};

// CGH 1.2+. Check base.size >= sizeof(cgh_host_api_config) before casting to this table.
struct cgh_host_api_config {
    cgh_host_api base;
    // Returns the UTF-8 JSON byte count. Copies only when capacity suffices. Query with nullptr/0.
    // No NUL terminator. -1: no config template, -2: invalid arguments or configuration error.
    int32_t (CGH_CALL* config_read)(void*, uint8_t* output, int32_t capacity);
    // Sets one top-level key from a UTF-8 JSON value. 0: saved, -1: no template, -2: error.
    int32_t (CGH_CALL* config_set)(void*, const char* key, const uint8_t* json, int32_t size);
};

// A Native DLL with no cgh_module_load export is a dependency library, not an entry module.
// Any number of DLLs in Native may export these entry points. Load return: 0 success.
typedef int32_t (CGH_CALL* cgh_module_load_fn)(const cgh_host_api*, const cgh_mod_info*);
typedef int32_t (CGH_CALL* cgh_module_start_fn)();
typedef void (CGH_CALL* cgh_module_tick_fn)(double delta_seconds);
typedef void (CGH_CALL* cgh_module_message_fn)(const char* source, const char* topic, const uint8_t*, int32_t);
typedef void (CGH_CALL* cgh_module_stop_fn)();

// Export these from a mod (start/tick/message/stop are optional).
// CGH_EXPORT int32_t CGH_CALL cgh_module_load(const cgh_host_api*, const cgh_mod_info*);
// CGH_EXPORT int32_t CGH_CALL cgh_module_start();
// CGH_EXPORT void CGH_CALL cgh_module_tick(double);
// CGH_EXPORT void CGH_CALL cgh_module_message(const char*, const char*, const uint8_t*, int32_t);
// CGH_EXPORT void CGH_CALL cgh_module_stop();
