# CGH-LAPI

CGH-LAPI 是 ConsGHost 的独立进程内 Mod。它接入 Windows x64 GMod 的 Server 和 Client realm，支持会话版本、GLua 与 CGH Mod 的双向通信，以及 Mod 主动派发 Lua hook。

## 构建和安装

在本目录运行 `./build.ps1`，需要 VS 2022 C++ x64、Windows SDK 和 CMake。默认生成 `out/package`，并在打包前执行原生测试。

- 将 `out/package/Mods/CGH-LAPI` 复制到 **ConsGHost.Host.exe 旁边**的 `Mods`。
- 将 `out/package/GMod/garrysmod` 中的内容合并到 GMod 安装下的 `garrysmod`。其中 `lua/bin` 是按 realm 加载的两份 Windows x64 Lua 模块，`lua/autorun` 负责装载和派发。
- 从 CGH 启动 GMod 并进入地图。每个 realm 第一次运行派发器时会登记自己的版本。只启用单侧时，可以保留相应的 DLL，并将启动脚本放入 `lua/autorun/server` 或 `lua/autorun/client`。

GLua 可以从自己的 realm 调用进程内 Mod 服务和发布话题。服务的参数格式、授权与副作用由提供服务的 Mod 决定。用 `loadAsSubprocess: false` 运行 CGH-LAPI。它支持 Server 和 Client，菜单 realm 不加载桥。

## Source 控制台诊断

- 从 CGH 启动游戏时，CGH-LAPI 0.1.1 会等引擎接管 `tier0.dll` 的日志输出，再通过 `ConMsg` 写入一次 `[CGH-LAPI] v0.1.1 startup test: Source console online.`。在主菜单也可以看到这条消息。
- 进入地图后，Server 和 Client 各自在 `InitPostEntity` 结束后做一次自检。本地开图时，Server 还会等到本机客户端进图并发出首个有效指令。控制台会收到带 realm 和地图名的 `Self-test PASS` 或 `Self-test FAIL`。自检通过 CGH 的版本服务往返校验 realm 元数据。桥有最多 10 秒的时间接入 CGH，自检失败后派发器仍会继续尝试连接。

## GLua → CGH Mod

```lua
PrintTable(cgh.GetVersion())
local reply, err = cgh.Call("cgh.example", "managed.echo", "hello\0CGH")
if reply == nil then print("CGH error", err) end

local status, publishErr = cgh.Publish("my.mod.request", "request-42")
if status == nil then print("CGH publish error", publishErr) end
```

`Call` 使用 CGH 同进程服务。参数为元数据 ID、已注册的服务名和二进制安全的 Lua 字符串。成功返回响应字符串，允许空字符串。`Publish` 走 CGH 消息路由，成功排入 IPC 队列时返回 `0`。两者失败时返回 `nil, errorCode`。

## CGH Mod → GLua

先给调用方 Mod 添加对 `cgh.lapi` 的依赖。进程内 Mod 使用 SDK 的 `IModContext.Call` 或原生 ABI 的 `call_service`。

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
int forced = context.Call("cgh.lapi", "server.hook",
    Encoding.UTF8.GetBytes("MyModRefresh\0manual"), []);
```

```lua
hook.Add("CGHLAPIMessage", "Inventory", function(name, payload)
    if name == "inventory.changed" then
        PrintTable(util.JSONToTable(payload))
    end
end)

hook.Add("MyModRefresh", "MyMod", function(payload)
    print("CGH 主动触发 hook", payload)
end)
```

| 服务 | 请求 | 结果 |
|---|---|---|
| `version.server` / `version.client` | 空 | 对应 realm 的 UTF-8 JSON，返回写入字节数 |
| `server.emit` / `client.emit` | `事件名 + NUL + payload` | 排队后返回 `0`，在对应 realm 调用 `hook.Run("CGHLAPIMessage", name, payload)` |
| `server.hook` / `client.hook` | `hook 名 + NUL + payload` | 排队后返回 `0`，在对应 realm 调用 `hook.Run(name, payload)` |

版本 JSON 包含 `realm`、`version`（`VERSION`）、`versionString`（`VERSIONSTR`）、`branch`、`lua`、`jit`、`arch`。分支和版本来自实际 GLua 会话。hook 派发沿用 GLua 的 `hook.Run` 语义，只有一个二进制字符串参数。入队成功只表示已接受请求。

隔离 Mod 通过 `PublishAsync` 发送同样的事件帧，话题为 `cgh.lapi.server.emit`、`cgh.lapi.client.emit`、`cgh.lapi.server.hook` 或 `cgh.lapi.client.hook`。这些话题异步投递。需要确认执行时，Lua 回调可用 `cgh.Publish` 发送应用层确认。隔离 Mod 要查询版本时，先订阅 `cgh.lapi.version.reply.<自己的Mod ID>`，再向 `cgh.lapi.version.server` 或 `cgh.lapi.version.client` 发布一个 1–128 字节且不含 NUL 的请求 ID。回复来自 `cgh.lapi`，格式为 `请求 ID + NUL + JSON`，realm 未登记时 JSON 为 `{"error":-4}`。

### 限制和错误码

- `-1` 服务未找到，`-2` CGH 调用或发布失败，`-3` 响应缓冲区不足。
- `-4` CGH 或目标 realm 尚未就绪，`-5` 目标队列已满，`-6` 参数或帧无效。
- 通信载荷和 Lua 同步响应均上限 64 KiB，事件名 1–128 字节。各 realm 分别排队，最多 128 条，每次 Think 最多派发 32 条。切换或关闭 Lua 状态时会清除旧队列。
- `cgh.lapi` 的版本及事件服务供 CGH Mod 使用。GLua 通过 `cgh.GetVersion()` 查询自身。同步服务在调用线程执行，耗时工作用消息请求和异步回复。

## 测试

`./build.ps1` 会用模拟宿主加载实际原生 DLL，检查 ABI、版本、二进制载荷、服务错误、realm 隔离、队列和停止清理。

真实 GMod 验收需要现有的 CGH 构建、Steam 在线和可启动的 GMod x64。关闭现有 GMod 会话后执行：

```powershell
./test-game.ps1 -GameDirectory 'L:\SteamLibrary\steamapps\common\GarrysMod'
```

测试在独立 CGH 分发目录中运行，使用 `gm_construct` 和 `-noaddons -noworkshop`。测试关闭宿主的原生 GameFrame detour，直接验证 GLua Think 驱动的派发。脚本仅在测试用 Lua 路径空闲时暂存 4 个文件，结束时移除自己写入的文件。Client 和 Server 都必须通过版本比对、同步二进制 echo、缺失服务、进程内 Mod 消息与强制 hook，以及隔离 Mod 的版本查询、消息和 hook 往返。日志保留在 `out/smoke/<id>/CGH/Sessions`。
