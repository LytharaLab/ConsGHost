# ConsGHost

**English** · [简体中文](docs/readme/README.zh-CN.md) · [Русский](docs/readme/README.ru.md) · [Español](docs/readme/README.es.md) · [Français](docs/readme/README.fr.md)

ConsGHost (CGH) loads C# and C++ mods into the Windows x64 build of Garry's Mod. A mod can run inside the game or in a separate process. Mods can also ship regular Garry's Mod addons as directories or `.gma` files.

## Getting started

1. Install the [.NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and sign in to Steam. CGH requires Windows 10 or 11 and a Garry's Mod installation with `gmod_win64.exe`.
2. Download `CGH-<version>-win-x64.zip` from [GitHub Releases](https://github.com/LytharaLab/ConsGHost/releases) and extract it into a writable folder. Keep the archive's files and directories together.
3. Run `ConsGHost.Host.exe`. Open **Settings**, the last item in the sidebar, and select the directory containing `gmod_win64.exe`. In Steam, **Manage → Browse local files** opens the game's installation directory.
4. Open the mod manager to review the enabled mods, then launch the game from CGH. The package includes BridgeDemo and Observer, which demonstrate calls between C# and C++ and messages between processes.

CGH starts Garry's Mod with `-insecure` by default for local development. Use a local session. Install mods and extra DLLs only from sources you trust. Running a mod in a separate process offers crash isolation but leaves it with your Windows user permissions.

CGH follows the Windows display language by default and uses English when that language is unsupported. In **Settings → Interface language**, choose English, Simplified Chinese, Russian, Spanish, or French. Changes apply immediately and are saved for the next launch.

## Installing mods

Put each mod in its own subdirectory under `Mods/`, next to `ConsGHost.Host.exe`. Each mod needs `cghm_metadata.json`. It can include `Managed/`, `Native/`, `Resources/`, `Addons/`, and a `config.default.json` template. CGH creates `config.json` from the template when it first finds the mod. See the [mod development guide](docs/mod-development.md) for the SDK and metadata format.

CGH keeps settings in `cgh_settings.json`, session logs under `Sessions/`, and persistent mod data under `Data/<mod-id>/`. To upgrade, extract the new ZIP into a fresh folder, then move over the mods and local data you want to keep. Preserve your mod configurations and review your settings before launching.

The Settings page also controls the .NET installation directory, one launch argument per line, the `GameFrame` adapter, and the startup timeout. The Extra DLLs page lets you add x64 native library directories, with optional recursive scanning. The runtime loads these DLLs before in-process mods.

## Building from source

Install the .NET 10 SDK, Visual Studio 2022 Build Tools with **Desktop development with C++** and a Windows SDK, and CMake 3.24 or later. Run these commands from the repository root in PowerShell:

```powershell
./build.ps1
./artifacts/CGH/ConsGHost.Host.exe
```

`./test.ps1` runs the native, integration, and desktop UI checks. `./package.ps1` creates a Windows x64 ZIP and a SHA-256 file under `artifacts/`. See the [verification guide](docs/verification.md).

## Project links

- [Architecture](docs/architecture.md)
- [Changelog](CHANGELOG.md)
- [License and third-party notices](NOTICE.md)
- [Security reports and diagnostic data](SECURITY.md)

ConsGHost is an independent community project. The project's own code and documentation are distributed under the [MIT License](LICENSE).
