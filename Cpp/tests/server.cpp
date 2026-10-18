#include <cstring>
#include "gmod_interfaces.h"
// Synthetic fixture for the GMod-specific prefix. This is not a game compatibility test.
class TestServer : public cgh::gmod::ServerGameDll009 {
public:
    void PreInit(void*, void*) override {}
    virtual bool DLLInit(void*, void*, void*, void*) { return true; }
    virtual bool ReplayInit(void*) { return true; }
    virtual bool GameInit() { return true; }
    virtual bool LevelInit(const char*, const char*, const char*, const char*, bool, bool) { return true; }
    virtual void ServerActivate(void*, int, int) {}
    virtual void GameFrame(bool) { ++frames; }
    unsigned long long frames{};
};
static TestServer server;
extern "C" __declspec(dllexport) void* CreateInterface(const char* name, int*) {
    return std::strcmp(name, "ServerGameDLL009") == 0 ? &server : nullptr;
}
extern "C" __declspec(dllexport) void fixture_frame() {
    volatile auto object = &server;
    auto** table = *reinterpret_cast<void***>(object);
    reinterpret_cast<void(*)(void*, bool)>(table[cgh::gmod::game_frame_slot])(&server, true);
}
