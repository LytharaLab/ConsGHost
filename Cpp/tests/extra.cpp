#include <windows.h>
extern "C" __declspec(dllexport) int cgh_extra_probe() { return 42; }
BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
