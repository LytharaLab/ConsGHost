#pragma once
#include <cstddef>

// GMod-specific ServerGameDLL009 prefix. Unlike Valve's generic Source SDK it has
// PreInit at slot 0. See danielga/sourcesdk-minimal public/eiface.h (GMod SDK).
// This is only an ABI declaration: CGH calls no initialization methods itself.
namespace cgh::gmod {
struct ServerGameDll009 {
    virtual void PreInit(void*, void*) = 0;
    virtual bool DLLInit(void*, void*, void*, void*) = 0;
    virtual bool ReplayInit(void*) = 0;
    virtual bool GameInit() = 0;
    virtual bool LevelInit(const char*, const char*, const char*, const char*, bool, bool) = 0;
    virtual void ServerActivate(void*, int, int) = 0;
    virtual void GameFrame(bool) = 0;
};
inline constexpr std::size_t game_frame_slot = 6;
}
