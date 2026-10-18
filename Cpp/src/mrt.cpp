#include <windows.h>
#include <tlhelp32.h>
#include <filesystem>
#include <iostream>
#include <string>
#include <vector>
#include <stdexcept>
#include <utility>

namespace fs = std::filesystem;
struct handle {
    HANDLE value{};
    explicit handle(HANDLE h = nullptr) : value(h) {}
    ~handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    handle(const handle&) = delete;
    handle& operator=(const handle&) = delete;
    handle(handle&& other) noexcept : value(std::exchange(other.value, nullptr)) {}
};
static void check(bool ok, const char* action) {
    if (!ok) throw std::runtime_error(std::string(action) + " (Win32 " + std::to_string(GetLastError()) + ")");
}
static fs::path root_dir() {
    std::vector<wchar_t> buffer(32768);
    check(GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size())) != 0, "GetModuleFileName");
    return fs::path(buffer.data()).parent_path();
}
static std::wstring quote(const std::wstring& text) {
    std::wstring result = L"\""; size_t slashes = 0;
    for (wchar_t c : text) {
        if (c == L'\\') { ++slashes; continue; }
        if (c == L'\"') { result.append(slashes * 2 + 1, L'\\'); result += c; }
        else { result.append(slashes, L'\\'); result += c; }
        slashes = 0;
    }
    result.append(slashes * 2, L'\\'); return result + L"\"";
}
static uintptr_t remote_module(DWORD pid, const std::wstring& name) {
    for (int attempt = 0; attempt < 30; ++attempt) {
        handle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid));
        if (snapshot.value != INVALID_HANDLE_VALUE) {
            MODULEENTRY32W entry{}; entry.dwSize = sizeof(entry);
            if (Module32FirstW(snapshot.value, &entry)) do {
                if (_wcsicmp(entry.szModule, name.c_str()) == 0) return reinterpret_cast<uintptr_t>(entry.modBaseAddr);
            } while (Module32NextW(snapshot.value, &entry));
        }
        Sleep(10);
    }
    throw std::runtime_error("Remote module not found");
}
static uintptr_t remote_function(DWORD pid, const wchar_t* module, const char* function) {
    auto local = GetModuleHandleW(module);
    auto address = GetProcAddress(local, function);
    check(address != nullptr, "GetProcAddress");
    HMODULE owner{};
    check(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(address), &owner) != 0, "Locate export owner");
    std::vector<wchar_t> path(32768);
    check(GetModuleFileNameW(owner, path.data(), static_cast<DWORD>(path.size())) != 0, "Locate owner path");
    return remote_module(pid, fs::path(path.data()).filename().wstring()) +
        reinterpret_cast<uintptr_t>(address) - reinterpret_cast<uintptr_t>(owner);
}
static DWORD remote_call(HANDLE process, uintptr_t function, const std::wstring& argument) {
    size_t bytes = (argument.size() + 1) * sizeof(wchar_t);
    void* memory = VirtualAllocEx(process, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    check(memory != nullptr, "VirtualAllocEx");
    try {
        check(WriteProcessMemory(process, memory, argument.c_str(), bytes, nullptr) != 0, "WriteProcessMemory");
        handle thread(CreateRemoteThread(process, nullptr, 0, reinterpret_cast<LPTHREAD_START_ROUTINE>(function), memory, 0, nullptr));
        check(thread.value != nullptr, "CreateRemoteThread");
        DWORD wait = WaitForSingleObject(thread.value, 120000);
        // On timeout the caller terminates the owned game before reclaiming its address space.
        if (wait != WAIT_OBJECT_0) throw std::runtime_error("Bootstrap thread timed out");
        DWORD result{}; check(GetExitCodeThread(thread.value, &result) != 0, "GetExitCodeThread");
        VirtualFreeEx(process, memory, 0, MEM_RELEASE);
        return result;
    } catch (...) { /* process rollback releases remote memory */ throw; }
}

static void write_code(HANDLE process, void* address, BYTE byte) {
    DWORD previous{};
    check(VirtualProtectEx(process, address, 1, PAGE_EXECUTE_READWRITE, &previous) != 0, "Protect entry barrier");
    BOOL written = WriteProcessMemory(process, address, &byte, 1, nullptr);
    DWORD unused{};
    BOOL restored = VirtualProtectEx(process, address, 1, previous, &unused);
    check(written != 0 && restored != 0 && FlushInstructionCache(process, address, 1) != 0, "Write entry barrier");
}

// A freshly CREATE_SUSPENDED process has not initialized its loader yet: enumerating kernel32
// or running CLR there is unreliable. Let Windows finish DLL initialization under its debugger,
// stop at the actual PE entry point, restore its byte/context, then detach with the main thread
// suspended. Injection now runs with a usable loader and before the game's entry point executes.
static void prepare_entry_barrier(const PROCESS_INFORMATION& info) {
    check(ResumeThread(info.hThread) != static_cast<DWORD>(-1), "Initialize game loader");
    void* entry{}; BYTE original{}; bool armed = false;
    auto deadline = GetTickCount64() + 30000;
    while (GetTickCount64() < deadline) {
        DEBUG_EVENT event{};
        if (!WaitForDebugEvent(&event, 100)) {
            if (GetLastError() == ERROR_SEM_TIMEOUT) continue;
            check(false, "Wait for game entry");
        }
        DWORD continuation = DBG_CONTINUE;
        if (event.dwDebugEventCode == CREATE_PROCESS_DEBUG_EVENT) {
            entry = reinterpret_cast<void*>(event.u.CreateProcessInfo.lpStartAddress);
            if (event.u.CreateProcessInfo.hFile) CloseHandle(event.u.CreateProcessInfo.hFile);
            check(entry != nullptr && ReadProcessMemory(info.hProcess, entry, &original, 1, nullptr) != 0, "Read game entry");
            write_code(info.hProcess, entry, 0xCC); armed = true;
        } else if (event.dwDebugEventCode == LOAD_DLL_DEBUG_EVENT) {
            if (event.u.LoadDll.hFile) CloseHandle(event.u.LoadDll.hFile);
        } else if (event.dwDebugEventCode == EXCEPTION_DEBUG_EVENT) {
            auto& exception = event.u.Exception.ExceptionRecord;
            if (armed && exception.ExceptionCode == EXCEPTION_BREAKPOINT && exception.ExceptionAddress == entry) {
                write_code(info.hProcess, entry, original);
                CONTEXT context{}; context.ContextFlags = CONTEXT_CONTROL;
                check(GetThreadContext(info.hThread, &context) != 0, "Read entry context");
                context.Rip = reinterpret_cast<DWORD64>(entry);
                check(SetThreadContext(info.hThread, &context) != 0, "Restore entry context");
                check(SuspendThread(info.hThread) != static_cast<DWORD>(-1), "Hold game entry");
                check(ContinueDebugEvent(event.dwProcessId, event.dwThreadId, DBG_CONTINUE) != 0, "Continue entry barrier");
                check(DebugActiveProcessStop(info.dwProcessId) != 0, "Detach startup debugger");
                return;
            }
            if (exception.ExceptionCode != EXCEPTION_BREAKPOINT) continuation = DBG_EXCEPTION_NOT_HANDLED;
        } else if (event.dwDebugEventCode == EXIT_PROCESS_DEBUG_EVENT) {
            ContinueDebugEvent(event.dwProcessId, event.dwThreadId, DBG_CONTINUE);
            throw std::runtime_error("Game exited before its entry point");
        }
        check(ContinueDebugEvent(event.dwProcessId, event.dwThreadId, continuation) != 0, "Continue loader initialization");
    }
    throw std::runtime_error("Game loader initialization timed out");
}
static int launch_game(const fs::path& game, const fs::path& config, const std::vector<std::wstring>& args) {
    auto runtime = root_dir() / L"cgh_runtime_win64.dll";
    HMODULE local = LoadLibraryExW(runtime.c_str(), nullptr, DONT_RESOLVE_DLL_REFERENCES);
    check(local != nullptr, "Read bootstrap export");
    auto entry = GetProcAddress(local, "cgh_bootstrap");
    check(entry != nullptr, "Find bootstrap export");
    uintptr_t offset = reinterpret_cast<uintptr_t>(entry) - reinterpret_cast<uintptr_t>(local);
    FreeLibrary(local);
    handle job(CreateJobObjectW(nullptr, nullptr)); check(job.value != nullptr, "CreateJobObject");
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    check(SetInformationJobObject(job.value, JobObjectExtendedLimitInformation, &limits, sizeof(limits)) != 0, "Configure job");
    std::wstring command = quote(game.wstring());
    for (const auto& arg : args) command += L" " + quote(arg);
    STARTUPINFOW startup{}; startup.cb = sizeof(startup); PROCESS_INFORMATION info{};
    check(CreateProcessW(game.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | DEBUG_ONLY_THIS_PROCESS,
        nullptr, game.parent_path().c_str(), &startup, &info) != 0, "Create suspended game");
    handle process(info.hProcess); handle thread(info.hThread);
    try {
        check(AssignProcessToJobObject(job.value, process.value) != 0, "Assign owned game job");
        prepare_entry_barrier(info);
        auto load_library = remote_function(info.dwProcessId, L"kernel32.dll", "LoadLibraryW");
        (void)remote_call(process.value, load_library, runtime.wstring());
        auto base = remote_module(info.dwProcessId, runtime.filename().wstring());
        DWORD result = remote_call(process.value, base + offset, config.wstring());
        if (result != 0) throw std::runtime_error("CGH bootstrap returned " + std::to_string(static_cast<int>(result)));
        check(ResumeThread(thread.value) != static_cast<DWORD>(-1), "Resume game");
        std::wcout << L"CGH_GAME_PID=" << info.dwProcessId << std::endl;
        WaitForSingleObject(process.value, INFINITE);
        DWORD code{}; GetExitCodeProcess(process.value, &code);
        return static_cast<int>(code);
    } catch (...) { DebugActiveProcessStop(info.dwProcessId); TerminateProcess(process.value, 1); WaitForSingleObject(process.value, 5000); throw; }
}

using hook_create_fn = int(__cdecl*)(void*, void*, void**);
using hook_enable_fn = int(__cdecl*)(void*, int);
using hook_remove_fn = int(__cdecl*)(void*);
static int(__cdecl* original_test)(int);
__declspec(noinline) static int __cdecl hook_target(int x) { volatile int v = x + 11; return v * 3; }
__declspec(noinline) static int __cdecl hook_detour(int x) { return original_test(x) + 100; }
static int native_test(HMODULE runtime) {
    auto create = reinterpret_cast<hook_create_fn>(GetProcAddress(runtime, "cgh_hook_create"));
    auto enable = reinterpret_cast<hook_enable_fn>(GetProcAddress(runtime, "cgh_hook_enable"));
    auto remove = reinterpret_cast<hook_remove_fn>(GetProcAddress(runtime, "cgh_hook_remove"));
    if (!create || !enable || !remove) return 10;
    auto target = reinterpret_cast<void*>(&hook_target);
    volatile auto invoke = &hook_target;
    if (invoke(2) != 39 || create(target, reinterpret_cast<void*>(&hook_detour), reinterpret_cast<void**>(&original_test)) != 0) return 11;
    if (enable(target, 1) != 0 || invoke(2) != 139) return 12;
    if (enable(target, 0) != 0 || invoke(2) != 39 || remove(target) != 0) return 13;
    std::cout << "PASS native x64 detour, trampoline, disable, remove" << std::endl;
    return 0;
}
int wmain(int argc, wchar_t** argv) {
    try {
        if (argc < 2) throw std::runtime_error("Use --worker CONFIG, --game GAME --config CONFIG -- ARGS, or --native-test");
        if (std::wstring(argv[1]) == L"--game") {
            if (argc < 5 || std::wstring(argv[3]) != L"--config") throw std::runtime_error("Invalid game arguments");
            std::vector<std::wstring> args;
            for (int i = argc > 5 && std::wstring(argv[5]) == L"--" ? 6 : 5; i < argc; ++i) args.emplace_back(argv[i]);
            return launch_game(fs::absolute(argv[2]), fs::absolute(argv[4]), args);
        }
        HMODULE runtime = LoadLibraryW((root_dir() / L"cgh_runtime_win64.dll").c_str());
        check(runtime != nullptr, "Load CGH runtime");
        if (std::wstring(argv[1]) == L"--native-test") return native_test(runtime);
        if (std::wstring(argv[1]) != L"--worker" || argc != 3) throw std::runtime_error("Invalid worker arguments");
        auto start = reinterpret_cast<LPTHREAD_START_ROUTINE>(GetProcAddress(runtime, "cgh_bootstrap"));
        check(start != nullptr, "Find cgh_bootstrap");
        DWORD rc = start(argv[2]);
        if (rc != 0) return static_cast<int>(rc);
        // Managed runtime exits this worker after its authenticated Stop or coordinator disconnect.
        Sleep(INFINITE);
        return 0;
    } catch (const std::exception& error) { std::cerr << "CGH MRT: " << error.what() << std::endl; return 1; }
}
