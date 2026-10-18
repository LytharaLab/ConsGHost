#include <windows.h>
#include <MinHook.h>
#include <atomic>
#include <chrono>
#include <mutex>
#include <string>
#include "hosting.h"
#include "cgh_sdk.h"
#include "gmod_interfaces.h"

using tick_fn = void (CGH_CALL*)(double);
static std::atomic<tick_fn> tick_callback{};
static std::atomic<bool> booted{};
static std::once_flag hooks_init;
static int hooks_status = MH_UNKNOWN;
static std::mutex engine_mutex;
using frame_fn = void (CGH_CALL*)(void*, bool);
static frame_fn original_frame{};
static void* frame_target{};
static thread_local std::chrono::steady_clock::time_point last_frame;

CGH_EXPORT void CGH_CALL cgh_set_tick(tick_fn tick) { tick_callback.store(tick, std::memory_order_release); }
static void CGH_CALL game_frame(void* self, bool simulating) {
    auto now = std::chrono::steady_clock::now();
    double dt = last_frame.time_since_epoch().count() == 0 ? 0.0 : std::chrono::duration<double>(now - last_frame).count();
    last_frame = now;
    if (auto tick = tick_callback.load(std::memory_order_acquire)) tick(dt);
    original_frame(self, simulating);
}
static int init_hooks() {
    std::call_once(hooks_init, [] { hooks_status = MH_Initialize(); });
    return hooks_status == MH_OK || hooks_status == MH_ERROR_ALREADY_INITIALIZED ? 0 : hooks_status;
}
CGH_EXPORT int CGH_CALL cgh_hook_create(void* target, void* detour, void** original) {
    if (init_hooks() != 0 || !target || !detour || !original) return -1;
    return MH_CreateHook(target, detour, original);
}
CGH_EXPORT int CGH_CALL cgh_hook_enable(void* target, int enabled) {
    return enabled ? MH_EnableHook(target) : MH_DisableHook(target);
}
CGH_EXPORT int CGH_CALL cgh_hook_remove(void* target) { return MH_RemoveHook(target); }

// Supported Source interface only. No signature scanning or guessing a different ABI.
// GMod adds PreInit BEFORE DLLInit; GameFrame is slot 6, not generic Source's slot 5.
CGH_EXPORT int CGH_CALL cgh_attach_engine() {
    std::lock_guard lock(engine_mutex);
    if (frame_target) return 1;
    HMODULE server = GetModuleHandleW(L"server.dll");
    if (!server) return 0;
    using factory_fn = void* (CGH_CALL*)(const char*, int*);
    auto factory = reinterpret_cast<factory_fn>(GetProcAddress(server, "CreateInterface"));
    if (!factory) return -1;
    auto interface_ptr = factory("ServerGameDLL009", nullptr);
    if (!interface_ptr) return -2;
    auto target = (*reinterpret_cast<void***>(interface_ptr))[cgh::gmod::game_frame_slot];
    MEMORY_BASIC_INFORMATION memory{};
    if (!VirtualQuery(target, &memory, sizeof(memory)) || !(memory.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY))) return -3;
    int rc = cgh_hook_create(target, reinterpret_cast<void*>(&game_frame), reinterpret_cast<void**>(&original_frame));
    if (rc != 0) return -100 - rc;
    rc = cgh_hook_enable(target, 1);
    if (rc != 0) { cgh_hook_remove(target); return -200 - rc; }
    frame_target = target;
    return 1;
}

// Called by a remote thread AFTER LoadLibrary returns, outside the loader lock.
extern "C" __declspec(dllexport) DWORD WINAPI cgh_bootstrap(void* configuration_path) {
    if (!configuration_path || booted.exchange(true)) return 1;
    try { return static_cast<DWORD>(start_managed(static_cast<const wchar_t*>(configuration_path), reinterpret_cast<void*>(&cgh_set_tick))); }
    catch (...) { return static_cast<DWORD>(-99); }
}
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(module);
    return TRUE;
}
