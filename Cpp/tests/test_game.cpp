#include <windows.h>
#include <iostream>
int wmain(int argc, wchar_t** argv) {
    int seconds = argc > 1 ? _wtoi(argv[1]) : 30;
    std::cout << "CGH controlled fixture entered main" << std::endl;
    auto server = LoadLibraryW(L".\\server.dll");
    auto frame = server ? reinterpret_cast<void(*)()>(GetProcAddress(server, "fixture_frame")) : nullptr;
    auto extra = GetModuleHandleW(L"cgh_test_extra.dll");
    auto probe = extra ? reinterpret_cast<int(*)()>(GetProcAddress(extra, "cgh_extra_probe")) : nullptr;
    if (probe && probe() == 42) std::cout << "CGH_EXTRA_PROBE=42" << std::endl;
    for (int i = 0; i < seconds * 100; ++i) { if (frame) frame(); Sleep(10); }
    return 0;
}
