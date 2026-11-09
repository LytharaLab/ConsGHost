#pragma once
#include <cstdint>

namespace lapi {
inline constexpr uint32_t abi = 1;
inline constexpr int32_t max_payload = 64 * 1024;
inline constexpr int32_t max_name = 128;
inline constexpr int32_t max_frame = max_name + 1 + max_payload;
inline constexpr int32_t max_version = 4096;
inline constexpr int32_t queue_capacity = 128;
inline constexpr int32_t unavailable = -4;
inline constexpr int32_t queue_full = -5;
inline constexpr int32_t invalid = -6;

enum class Realm : int32_t { server, client };
enum class Kind : int32_t { message, hook };

struct Api {
    uint32_t version;
    int32_t (__cdecl* attach)(Realm realm, const void* owner, const char* json, int32_t size);
    void (__cdecl* detach)(Realm realm, const void* owner);
    int32_t (__cdecl* call)(Realm realm, const void* owner, const char* mod, const char* service,
                          const char* data, int32_t size, char* out, int32_t capacity);
    int32_t (__cdecl* publish)(Realm realm, const void* owner, const char* topic, const char* data, int32_t size);
    int32_t (__cdecl* poll)(Realm realm, const void* owner, Kind* kind, char* out, int32_t capacity);
};
using GetApi = const Api* (__cdecl*)(uint32_t);
using CheckVersion = int32_t (__cdecl*)(Realm realm, const void* owner, char* out, int32_t capacity);
}
