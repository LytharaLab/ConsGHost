# Developing a CGH mod

## Files and metadata

Each mod is a directory under `Mods/` next to `ConsGHost.Host.exe`:

```text
Mods/MyMod/
  cghm_metadata.json
  config.default.json
  Managed/MyMod.dll
  Native/MyNative.dll
  Resources/
  Addons/
```

Only `cghm_metadata.json` is required. This example shows the fields accepted by the [metadata schema](cghm_metadata.schema.json):

```json
{
  "schemaVersion": 1,
  "id": "author.my-mod",
  "name": "My Mod",
  "version": "1.0.0",
  "description": "A sample CGH mod",
  "author": "Author",
  "enabled": true,
  "loadAsSubprocess": false,
  "dependencies": [
    { "id": "other.mod", "minVersion": "1.0.0", "maxVersionExclusive": "2.0.0", "optional": false }
  ],
  "loadAfter": [],
  "ignoredAddons": ["developer_tools", "optional_models.gma"],
  "managedAssemblies": ["MyMod.dll"],
  "nativeLibraries": ["MyNative.dll"]
}
```

- `id`, `name`, and `version` are required. IDs start with a lowercase letter and contain at most 64 characters from `a-z`, `0-9`, `.`, `_`, and `-`. Versions use three nonnegative integer components. Dependency versions are checked when the dependency is present and enabled, including optional dependencies. Missing required dependencies, version mismatches, duplicate IDs, and dependency cycles all prevent launch.
- `loadAfter` orders enabled mods. `enabled` and the host's disabled list determine whether the mod is active. Mods and their dependencies must already be installed. CGH v1 accepts numeric version bounds.
- Omit `managedAssemblies` or `nativeLibraries` to scan the top-level DLLs in the corresponding directory. Use an empty array to select no entries. Listed paths can include subdirectories and must resolve inside `Managed/` or `Native/` without symlinks or junctions.
- Put managed dependencies and any `.deps.json` beside the managed DLL. CGH instantiates every concrete `ICghMod` implementation with a public parameterless constructor. Use the host's shared SDK assembly and mark your reference `Private=false`.
- Put native libraries and their dependencies under `Native/`. Use unique native DLL names within each process. Dependency declarations order lifecycle calls. Cross-mod APIs use services and messages across the mods' separate .NET load contexts.

## Garry's Mod addons

Since CGH 1.1.0, a mod can ship expanded addons as direct subdirectories of `Addons/`, and standard uncompressed GMAD archives as direct `.gma` files. A mod can consist entirely of addons and metadata.

```text
Addons/
  sample_addon/
    lua/
    materials/
  sample_maps.gma
```

`ignoredAddons` lists direct child names, including the `.gma` suffix for archives. Matching is case-insensitive. Entries can refer to addons that have yet to be installed.

Before launch, CGH stages copies under `garrysmod/addons` for all enabled mods, including subprocess mods. Each staged addon gets a unique directory name. CGH removes its copies on shutdown or failed startup and can recover its own leftovers on a later run. Restart the session after editing source addons. Use the standard `garrysmod/` game directory and launch without `-noaddons` when exercising this feature.

## Mod settings

Add a JSON object named `config.default.json` in the mod directory:

```json
{
  "enabled": true,
  "displayName": "My Mod",
  "interval": 0.5,
  "diagnostics": { "verbose": false },
  "maps": ["gm_construct"]
}
```

CGH creates `config.json` on first discovery and preserves that file on subsequent scans. Reads fill in missing defaults recursively for objects and keep unknown keys. Arrays are values and are replaced as a whole. A `null` default permits any JSON type. Ship the default template with the mod and let each user generate their own configuration.

The mod manager offers a form based on the template and a raw JSON editor. Both share unsaved edits and validate JSON and field types before writing. Use the raw editor to repair malformed JSON. Reopen the editor if another process changes the file while you are editing it.

### C# configuration API

```csharp
if (context.Config is { } config)
{
    bool verbose = config.Read().GetProperty("diagnostics").GetProperty("verbose").GetBoolean();
    string? label = config.Get<string>("displayName");
    config.Set("interval", 1.0);
}
```

`Read()` returns an effective snapshot. `Get<T>(key)` reads a top-level value. `Set(key, value)` updates one top-level value atomically and preserves the other keys. Reads consult the file, so read at load time or at an explicit refresh point if you need to avoid disk accesses in `Tick`.

### C++ configuration API

CGH 1.2.0 adds `cgh_host_api_config` after the original ABI v1 fields. Check `size` before accessing it:

```cpp
if (api->size >= sizeof(cgh_host_api_config)) {
    const auto* config = reinterpret_cast<const cgh_host_api_config*>(api);
    const uint8_t interval[] = {'1', '.', '0'};
    const int32_t status = config->config_set(api->context, "interval", interval, sizeof(interval));
    if (status != 0) api->log(api->context, "warning", "Could not update interval");
}
```

`config_read` returns the byte count needed for a UTF-8 JSON snapshot and copies it if the supplied buffer fits. The snapshot has no trailing NUL. Pass a null buffer and zero capacity to obtain the size. If the file grows between calls, retry with the returned size. `config_set` accepts a single JSON value and returns `0` on success. Both return `-1` when the mod has no template and `-2` for an argument or configuration error.

## C# entry points

Target `net10.0-windows` and x64, and reference `SDK/ConsGHost.SDK.dll` from the release archive:

```csharp
using ConsGHost.SDK;

public sealed class MyMod : ICghMod
{
    public void Load(IModContext context)
    {
        context.RegisterService("echo", (input, output) =>
        {
            if (output.Length < input.Length) return -3;
            input.CopyTo(output);
            return input.Length;
        });
    }

    public void Start() { }
    public void Tick(double deltaSeconds) { }
    public void Stop() { }
}
```

Read assets from `ResourcesDirectory` and write persistent state to `DataDirectory`. `Stopping` is canceled during cleanup. Use `RunOnGameThread` for work that touches engine objects. It runs from engine frames and drains up to 256 actions per frame. `Call` is for services in the active process, and `Subscribe` and `PublishAsync` work across processes. `FindNativeExport` looks up exports in this mod's mapped native libraries. Keep hook delegates alive while their hooks are installed.

The source tree includes working `BridgeDemo` and `Observer` projects under `CSharp/Examples/`.

## C++ entry points

Include `SDK/cgh_sdk.h` and compile for Windows x64. Export `cgh_module_load`. The Start, Tick, Message, and Stop exports are optional.

```cpp
#include "cgh_sdk.h"

CGH_EXPORT int32_t CGH_CALL cgh_module_load(const cgh_host_api* api, const cgh_mod_info*) {
    if (!api || api->abi_version != 1 || api->size < sizeof(cgh_host_api)) return -1;
    api->log(api->context, "info", "Hello from native code");
    return 0;
}

CGH_EXPORT void CGH_CALL cgh_module_stop() {
    // Join owned threads and release their resources.
}
```

The API and mod info pointers are valid for the process lifetime. Text uses UTF-8. Call buffers are borrowed and must be copied to retain their contents. Load and Start return `0` on success. CGH attempts Stop even after partial initialization. Keep C++ exceptions inside your mod and join any owned threads during Stop. CGH disables hooks registered through the SDK. Manage any hooks installed outside the SDK yourself.

Service names must be unique within a mod. `loadAsSubprocess: true` places both managed and native parts in the same worker, so they can still call each other's services. Use messages for data exchanged with the game process. Pointers refer to the address space that created them.

## GLua bridge

[CGH-LAPI](https://github.com/LytharaLab/ConsGHost/tree/master/OfficialMods/CGH-LAPI) is a separately installed mod for communication with the Garry's Mod client and server Lua realms. CGH's C# and C++ APIs are available without that mod.
