#include <windows.h>
#include <iostream>
#include <string_view>
int wmain(int argc, wchar_t** argv) {
    int seconds = argc > 1 ? _wtoi(argv[1]) : 30;
    std::cout << "CGH controlled fixture entered main" << std::endl;
    wchar_t heap_setting[2]{}, inherited[32]{};
    if (GetEnvironmentVariableW(L"_NO_DEBUG_HEAP", heap_setting, 2) == 1 && heap_setting[0] == L'1' &&
        GetEnvironmentVariableW(L"CGH_TEST_INHERITED", inherited, 32) == 5 && std::wstring_view(inherited) == L"path中")
        std::cout << "CGH game environment verified" << std::endl;
    auto server = LoadLibraryW(L".\\server.dll");
    auto frame = server ? reinterpret_cast<void(*)()>(GetProcAddress(server, "fixture_frame")) : nullptr;
    auto extra = GetModuleHandleW(L"cgh_test_extra.dll");
    auto probe = extra ? reinterpret_cast<int(*)()>(GetProcAddress(extra, "cgh_extra_probe")) : nullptr;
    if (probe && probe() == 42) std::cout << "CGH_EXTRA_PROBE=42" << std::endl;
    for (int i = 0; i < seconds * 100; ++i) { if (frame) frame(); Sleep(10); }
    return argc > 2 ? _wtoi(argv[2]) : 0;
}
