# Changelog

## 1.2.2

- Fix intermittent crashes at game startup while CGH and mods install native hooks. MinHook now enumerates this process's threads through ntdll instead of the Toolhelp API that Chromium's `chrome_elf.dll` detours in Garry's Mod.
- Serialize hook creation and enabling on one native worker so the engine hook, CGH-LAPI, and rendering mods can install hooks at the same time without suspending each other.
- Existing CGH mods need no changes; the native hook ABI is unchanged.

## 1.2.1

- The desktop host supports English, Simplified Chinese, Russian, Spanish, and French. It follows the Windows display language by default and falls back to English.
- The interface language can be changed in Settings without restarting CGH. The selection is saved with the host settings.
- The host writes its log messages in English for every interface language.

## 1.2.0

- Use `config.default.json` to define settings for a mod. On discovery, CGH creates `config.json` if it is missing. The mod manager offers both a settings form and a raw JSON editor.
- C# and C++ mods can read and update their own settings. Reads fill in missing keys from the defaults and preserve custom keys.

## 1.1.0

- Load regular Garry's Mod addon directories and `.gma` files from a CGH mod's `Addons/` folder.
- Use `ignoredAddons` in mod metadata to skip addons by name. The mod manager lists discovered addons and shows which ones are skipped.
- Stage enabled addons before the game starts and clean up the staged copies when the session ends or startup fails.

## 1.0.0

- Set the product version of managed components, native components, and release archives from `VERSION`.
- Show the product version in the host and the session log. Query it or verify a distribution from the runtime command line.
- Build versioned Windows x64 ZIPs with SHA-256 checksums from a fresh staging directory.
