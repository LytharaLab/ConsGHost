#include <windows.h>
#include <MinHook.h>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <deque>
#include <future>
#include <memory>
#include <mutex>
#include <thread>
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

namespace {
// MinHook's freeze step suspends every thread while it patches code, and its
// hook list is not safe for concurrent create/enable/remove calls. Keep every
// MinHook call on one native worker; managed callers wait in native code, so
// they cannot be suspended in the middle of a hook P/Invoke.
enum class HookOperation { create, enable, remove };

struct HookRequest {
    HookOperation operation{};
    void* target = nullptr;
    void* detour = nullptr;
    void** original = nullptr;
    bool enabled = false;
    std::promise<int> completion;
};

std::mutex hook_queue_mutex;
std::condition_variable hook_queue_ready;
std::deque<std::shared_ptr<HookRequest>> hook_queue;
std::once_flag hook_worker_once;
std::atomic<bool> hook_worker_available{false};

int init_hooks() {
    std::call_once(hooks_init, [] { hooks_status = MH_Initialize(); });
    return hooks_status == MH_OK || hooks_status == MH_ERROR_ALREADY_INITIALIZED ? 0 : hooks_status;
}

void hook_worker() {
    const int initialized = init_hooks();
    for (;;) {
        std::shared_ptr<HookRequest> request;
        {
            std::unique_lock lock(hook_queue_mutex);
            hook_queue_ready.wait(lock, [] { return !hook_queue.empty(); });
            request = std::move(hook_queue.front());
            hook_queue.pop_front();
        }

        int result = -1;
        if (initialized == 0) {
            switch (request->operation) {
            case HookOperation::create:
                result = MH_CreateHook(request->target, request->detour, request->original);
                break;
            case HookOperation::enable:
                result = request->enabled ? MH_EnableHook(request->target) : MH_DisableHook(request->target);
                break;
            case HookOperation::remove:
                result = MH_RemoveHook(request->target);
                break;
            }
        }
        try { request->completion.set_value(result); } catch (...) { }
    }
}

bool ensure_hook_worker() {
    std::call_once(hook_worker_once, [] {
        try {
            std::thread(hook_worker).detach();
            hook_worker_available.store(true, std::memory_order_release);
        } catch (...) {
            hook_worker_available.store(false, std::memory_order_release);
        }
    });
    return hook_worker_available.load(std::memory_order_acquire);
}

int submit_hook_request(HookOperation operation, void* target, void* detour, void** original, bool enabled) {
    if (!ensure_hook_worker()) return -1;
    auto request = std::make_shared<HookRequest>();
    request->operation = operation;
    request->target = target;
    request->detour = detour;
    request->original = original;
    request->enabled = enabled;
    auto completion = request->completion.get_future();
    {
        std::scoped_lock lock(hook_queue_mutex);
        hook_queue.push_back(std::move(request));
    }
    hook_queue_ready.notify_one();
    try { return completion.get(); } catch (...) { return -1; }
}
} // namespace

CGH_EXPORT void CGH_CALL cgh_set_tick(tick_fn tick) { tick_callback.store(tick, std::memory_order_release); }
static void CGH_CALL game_frame(void* self, bool simulating) {
    auto now = std::chrono::steady_clock::now();
    double dt = last_frame.time_since_epoch().count() == 0 ? 0.0 : std::chrono::duration<double>(now - last_frame).count();
    last_frame = now;
    if (auto tick = tick_callback.load(std::memory_order_acquire)) tick(dt);
    original_frame(self, simulating);
}
CGH_EXPORT int CGH_CALL cgh_hook_create(void* target, void* detour, void** original) {
    if (!target || !detour || !original) return -1;
    return submit_hook_request(HookOperation::create, target, detour, original, false);
}
CGH_EXPORT int CGH_CALL cgh_hook_enable(void* target, int enabled) {
    if (!target) return -1;
    return submit_hook_request(HookOperation::enable, target, nullptr, nullptr, enabled != 0);
}
CGH_EXPORT int CGH_CALL cgh_hook_remove(void* target) {
    if (!target) return -1;
    return submit_hook_request(HookOperation::remove, target, nullptr, nullptr, false);
}

// Supported Source interface only. No signature scanning or guessing a different ABI.
// GMod adds PreInit BEFORE DLLInit; GameFrame is slot 6, not generic Source's slot 5.
CGH_EXPORT int CGH_CALL cgh_attach_engine() {
    std::scoped_lock lock(engine_mutex);
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
