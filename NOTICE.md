# License and third-party notices

## ConsGHost

The [MIT License](LICENSE) covers the original ConsGHost source, documentation, SDK, examples, and CGH-LAPI. Keep the copyright and permission notices when redistributing copies or substantial portions. The license contains the project's warranty and liability terms. Third-party material keeps its own license.

## Third-party components

| Component | Use in ConsGHost | License and attribution |
| --- | --- | --- |
| MinHook 1.3.4 | Statically linked into `cgh_runtime_win64.dll`. The vendored copy includes Hacker Disassembler Engine (HDE). | BSD 2-Clause. Keep the complete [MinHook and HDE notices](https://github.com/TsudaKageyu/minhook/blob/v1.3.4/LICENSE.txt). The source tree carries them in `Cpp/third_party/minhook-1.3.4/LICENSE.txt`; the Windows archive carries them in `licenses/MinHook.txt`. |
| Microsoft .NET hosting components | The SDK supplies the Windows x64 app hosts and `libnethost.lib`. The desktop runtime is installed separately. | The Windows archive includes `licenses/dotnet-LICENSE.txt` and `licenses/dotnet-ThirdPartyNotices.txt` from the Microsoft .NET installation used to build it. |
| Microsoft Visual C++ runtime | The Windows Release native binaries link the MSVC runtime statically. | See the [Visual Studio 2022 redistribution terms](https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution) for Microsoft's distributable code requirements. |

## Game and platform

ConsGHost is an independent community project and has no affiliation with or endorsement from Facepunch Studios, Valve, or Microsoft. Garry's Mod, Steam, Source, and other product names identify the software CGH works with. Their names, logos, and game assets belong to their respective rights holders.

The release contains CGH, its supporting components, and its examples. Obtain Garry's Mod and Steam separately. The project license grants no rights to game files, Valve or Facepunch content, Workshop addons, or third-party mods. When distributing an addon, include only content you have permission to redistribute and carry through its notices.

CGH is intended for local mod development. Follow the terms that apply to your game and platform, including the [Facepunch Modding Guidelines](https://facepunch.com/legal/modding), [Facepunch Terms of Service](https://facepunch.com/legal/tos), and [Steam Subscriber Agreement](https://store.steampowered.com/subscriber_agreement/).
