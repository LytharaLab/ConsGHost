#include <windows.h>
#include <filesystem>
#include <stdexcept>
#include <vector>
#include <nethost.h>
#include <hostfxr.h>
#include <coreclr_delegates.h>
#include "hosting.h"

namespace fs = std::filesystem;
static fs::path module_directory() {
    HMODULE module{};
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&module_directory), &module);
    std::vector<wchar_t> path(32768);
    if (!GetModuleFileNameW(module, path.data(), static_cast<DWORD>(path.size()))) throw std::runtime_error("GetModuleFileNameW failed");
    return fs::path(path.data()).parent_path();
}

struct boot_args { uint32_t size; const wchar_t* config; void* set_tick; };
int start_managed(const std::wstring& config, void* tick_setter) {
    auto root = module_directory();
    // The coordinator selects and verifies the x64 .NET root. Do not trust a stale global DOTNET_ROOT.
    std::vector<wchar_t> configured(32768);
    DWORD length = GetEnvironmentVariableW(L"CGH_DOTNET_ROOT", configured.data(), static_cast<DWORD>(configured.size()));
    std::wstring dotnet_root;
    if (length > 0 && length < configured.size()) dotnet_root = configured.data();
    else {
        DWORD count = GetEnvironmentVariableW(L"ProgramFiles", configured.data(), static_cast<DWORD>(configured.size()));
        if (count == 0 || count >= configured.size()) return -10;
        dotnet_root = (fs::path(configured.data()) / L"dotnet").wstring();
    }
    get_hostfxr_parameters params{ sizeof(params), nullptr, dotnet_root.c_str() };
    std::vector<wchar_t> buffer(32768); size_t size = buffer.size();
    if (get_hostfxr_path(buffer.data(), &size, &params) != 0) return -11;
    HMODULE fxr = LoadLibraryExW(buffer.data(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!fxr) return -12;
    auto init = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(GetProcAddress(fxr, "hostfxr_initialize_for_runtime_config"));
    auto get = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(GetProcAddress(fxr, "hostfxr_get_runtime_delegate"));
    auto close = reinterpret_cast<hostfxr_close_fn>(GetProcAddress(fxr, "hostfxr_close"));
    if (!init || !get || !close) return -13;
    hostfxr_handle context{};
    int rc = init((root / L"ConsGHost.Bootstrap.runtimeconfig.json").c_str(), nullptr, &context);
    if (rc < 0 || !context) return rc < 0 ? rc : -14;
    void* loader{};
    rc = get(context, hdt_load_assembly_and_get_function_pointer, &loader);
    close(context);
    if (rc < 0 || !loader) return rc < 0 ? rc : -15;
    using entry_fn = int (CORECLR_DELEGATE_CALLTYPE*)(boot_args*, int);
    entry_fn entry{};
    rc = reinterpret_cast<load_assembly_and_get_function_pointer_fn>(loader)(
        (root / L"ConsGHost.Bootstrap.dll").c_str(), L"ConsGHost.Bootstrap.Entry, ConsGHost.Bootstrap",
        L"Start", UNMANAGEDCALLERSONLY_METHOD, nullptr, reinterpret_cast<void**>(&entry));
    if (rc < 0 || !entry) return rc < 0 ? rc : -16;
    boot_args args{sizeof(args), config.c_str(), tick_setter};
    return entry(&args, sizeof(args));
}
