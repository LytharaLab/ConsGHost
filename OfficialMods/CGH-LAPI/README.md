# CGH-LAPI

CGH-LAPI connects ConsGHost mods to the Server and Client GLua realms in Garry's Mod for Windows x64. It exposes session version information, lets Lua call CGH services, and dispatches events and hooks from CGH mods to Lua. Keep `loadAsSubprocess` set to `false` for CGH-LAPI itself.

## Installation

1. Use CGH 1.1.0 or later. Copy `Mods/CGH-LAPI` beside `ConsGHost.Host.exe`, inside its `Mods/` directory.
2. When upgrading from 0.1.x, remove the old `garrysmod/lua/autorun/cgh_lapi.lua`, `garrysmod/lua/bin/gmsv_cgh_lapi_win64.dll`, and `garrysmod/lua/bin/gmcl_cgh_lapi_win64.dll` that were installed with that version.
3. Start Garry's Mod from CGH with addons enabled and load a map. CGH loads `Native/cgh_lapi_win64.dll` and stages `Addons/cgh_lapi/` for the session. The dispatcher registers each available GLua realm and its version.

The Menu realm is outside the bridge. Each CGH service defines its payload format, permissions, and side effects.

## Checking the installation

The native module waits for the Source console and writes `[CGH-LAPI] v0.2.0 startup test: Source console online.` once per launch. The main menu can display that message.

After `InitPostEntity`, each realm runs a self-test. The server on a local map waits for the local client's first valid command. Look for `Self-test PASS` or `Self-test FAIL` with the realm and map name in the console. The test checks the reported realm metadata through CGH's version service. It gives the bridge up to 10 seconds to connect. The dispatcher can continue trying to connect after a timeout.

## Calling CGH from GLua

```lua
PrintTable(cgh.GetVersion())
local reply, err = cgh.Call("cgh.example", "managed.echo", "hello\0CGH")
if reply == nil then print("CGH error", err) end

local status, publishErr = cgh.Publish("my.mod.request", "request-42")
if status == nil then print("CGH publish error", publishErr) end
```

`Call` accepts a mod ID, service name, and binary-safe Lua string. It returns a response string, which can be empty. `Publish` queues a message through CGH and returns `0` when accepted. Both return `nil, errorCode` on failure. Synchronous service handlers run on the Lua caller's thread. Use asynchronous requests and replies for expensive work.

## Calling GLua from a CGH mod

Declare a dependency on `cgh.lapi`. An in-process mod can use `IModContext.Call` or the native `call_service` API:

```csharp
using System.Text;
using System.Text.Json;

// IModContext context
Span<byte> result = stackalloc byte[4096];
int size = context.Call("cgh.lapi", "version.server", [], result);
if (size >= 0)
{
    using var version = JsonDocument.Parse(result[..size].ToArray());
    context.Log(version.RootElement.GetProperty("versionString").GetString() ?? "");
}

int emitted = context.Call("cgh.lapi", "server.emit",
    Encoding.UTF8.GetBytes("inventory.changed\0{\"slot\":3}"), []);
int requested = context.Call("cgh.lapi", "server.hook",
    Encoding.UTF8.GetBytes("MyModRefresh\0manual"), []);
```

```lua
hook.Add("CGHLAPIMessage", "Inventory", function(name, payload)
    if name == "inventory.changed" then
        PrintTable(util.JSONToTable(payload))
    end
end)

hook.Add("MyModRefresh", "MyMod", function(payload)
    print("Refresh requested by CGH", payload)
end)
```

| Service | Request | Result |
| --- | --- | --- |
| `version.server` or `version.client` | Empty | Returns the number of UTF-8 JSON bytes written to the response buffer. |
| `server.emit` or `client.emit` | Event name, NUL, then payload | Returns `0` on queueing `hook.Run("CGHLAPIMessage", name, payload)` in the selected realm. |
| `server.hook` or `client.hook` | Hook name, NUL, then payload | Returns `0` on queueing `hook.Run(name, payload)` in the selected realm. |

Version JSON contains `realm`, `version` (`VERSION`), `versionString` (`VERSIONSTR`), `branch`, `lua`, `jit`, and `arch` from the active GLua session. Hook callbacks receive one binary string payload. A successful queue operation confirms receipt of the request. For confirmation that a Lua callback ran, publish an explicit acknowledgment from that callback.

### Requests from workers

A subprocess mod can use `PublishAsync` to send the same event frame on `cgh.lapi.server.emit`, `cgh.lapi.client.emit`, `cgh.lapi.server.hook`, or `cgh.lapi.client.hook`.

For a version lookup, subscribe to `cgh.lapi.version.reply.<your-mod-id>` and publish a request ID of 1–128 bytes without a NUL to `cgh.lapi.version.server` or `cgh.lapi.version.client`. The reply from `cgh.lapi` contains the request ID, a NUL, and the JSON response. A realm that has yet to register returns `{"error":-4}` in the JSON portion.

### Limits and errors

| Code | Meaning |
| --- | --- |
| `-1` | Service not found. |
| `-2` | CGH call or publish failed. |
| `-3` | Response buffer too small. |
| `-4` | CGH or the target realm is unavailable. |
| `-5` | Target realm's queue is full. |
| `-6` | Invalid argument or frame. |

Payloads and synchronous Lua responses are limited to 64 KiB. Event names are 1–128 bytes. Each realm holds up to 128 queued items and dispatches up to 32 per `Think`. Changing or closing the Lua state clears its queue.

## Building and testing

From this source directory, run `./build.ps1` using the Visual Studio 2022 x64 C++ tools, Windows SDK, and CMake. It builds the DLL, runs the native tests against a test host, and installs the package layout into `out/package-0.2.0/`.

For the real-game smoke test, build ConsGHost, close other Garry's Mod sessions, and sign in to Steam. Run these commands from the same source directory:

```powershell
$gameDirectory = Read-Host 'Garry''s Mod installation directory'
./test-game.ps1 -GameDirectory $gameDirectory
```

The smoke test uses a separate CGH distribution and opens `gm_construct` with `-noworkshop`. It disables the host's native `GameFrame` callback and exercises the GLua `Think` dispatcher directly. CGH stages and cleans up both the packaged addon and the test addon through its regular addon lifecycle. The test checks the native bootstrap, version reporting, binary echo, service errors, messages, and hook dispatch for both realms, including requests from a worker. Logs are under `out/smoke/`.

## License

CGH-LAPI is distributed under the ConsGHost MIT license. The standalone ZIP includes `LICENSE`. The Windows binaries link the Microsoft Visual C++ runtime statically. [Visual Studio 2022 redistribution terms](https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution)

CGH-LAPI is a ConsGHost project mod for Garry's Mod and has no affiliation with or endorsement from Facepunch Studios or Valve. [Facepunch Modding Guidelines](https://facepunch.com/legal/modding) · [Steam Subscriber Agreement](https://store.steampowered.com/subscriber_agreement/)
