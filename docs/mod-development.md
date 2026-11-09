# CGH 模组开发

## 元数据 v1

```json
{
  "schemaVersion": 1,
  "id": "author.my-mod",
  "name": "My Mod",
  "version": "1.0.0",
  "description": "模组说明",
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

`id/name/version` 必填；id 以小写字母开头，仅含小写字母、数字、点、下划线和连字符，最多 64 字符。版本为 `major.minor.patch` 非负整数。
启用状态由元数据 `enabled` 与宿主禁用列表共同决定。缺少强依赖、版本不匹配、重复 id、循环依赖都会阻止启动；可选依赖安装时仍检查版本并决定顺序。
`loadAfter` 对已启用模组增加排序约束。版本 v1 不支持 prerelease、通配符或自动下载依赖。

`managedAssemblies/nativeLibraries` 省略时扫描对应目录顶层所有 DLL；显式 `[]` 表示无入口。
声明路径相对对应目录，可以指定子目录，禁止绝对路径、越界路径与 junction/symlink。
托管 DLL 中所有非抽象 `ICghMod` 实现都会实例化，要求公共无参构造器；纯依赖程序集没有入口也合法。
第三方 Managed 依赖和 `.deps.json` 应随 DLL 放进 `Managed`；引用 SDK 时设置 `Private=false`，使用宿主共享 SDK。
Native 入口 DLL 与其依赖都放在 `Native`；DLL 文件名在同一进程中必须唯一，隔离进程各自独立。
元数据依赖决定生命周期顺序，不会打通不同模组的 .NET 类型加载上下文；跨模组 API 通过服务和消息契约暴露。

## GMod 插件

CGH 1.1.0 起，模组可以在 `Addons/` 下提供任意数量的标准 GMod 插件。每个直属子目录是一个展开的插件，每个直属 `.gma` 文件是一个打包插件。模组也可以只提供插件和元数据。

```text
Mods/MyMod/
  cghm_metadata.json
  Addons/
    my_weapons/
      lua/
      materials/
    my_maps.gma
    developer_tools/
      lua/
```

`ignoredAddons` 是可选数组，填写当前模组内要跳过的直属子目录名或文件名。文件名须包含 `.gma` 扩展名。匹配忽略大小写，支持提前列出尚未安装的插件。其他顶层文件不会作为插件扫描。

CGH 在启动前为所有已启用模组暂存插件，包括设为独立子进程的模组。标准未压缩 GMAD 包会先展开成目录，因此目录插件和 `.gma` 都由 GMod 从 `garrysmod/addons` 加载。暂存目录使用 CGH 生成的唯一名称，已有游戏插件保留原位。修改源插件后，重开会话即可更新副本。会话停止、退出或启动回滚后会清理副本，强制终止后由下次 CGH 会话回收残留。游戏参数 `-noaddons` 会跳过 CGH 插件，使用这项功能时请保留默认 GMod 游戏目录。

## 模组配置

在模组根目录放置 `config.default.json`。文件是一个 JSON 对象，键和默认值同时定义简单配置界面的字段与类型。例如：

```json
{
  "enabled": true,
  "displayName": "My Mod",
  "interval": 0.5,
  "diagnostics": { "verbose": false },
  "maps": ["gm_construct"]
}
```

CGH 首次扫描时复制模板生成同目录的 `config.json`。重新扫描保留现有配置。新增的默认键在读取时递归补齐，未知键保留，数组整体作为一个值。模板为 `null` 的键可接受任意 JSON 类型。没有模板的模组仍可正常加载。

在「模组管理」中选择模组后点击「配置…」。简单配置按字段提供复选框和输入框，数组和自由 JSON 值可以在单个字段中编辑。高级配置直接编辑完整 JSON，可用来修复手动编辑造成的格式错误。两种模式共享未保存的编辑，保存前校验 JSON 和模板类型。如果磁盘上的配置在编辑期间被其他进程修改，重新打开后再编辑。

### C# Config API

```csharp
if (context.Config is { } config)
{
    bool verbose = config.Read().GetProperty("diagnostics").GetProperty("verbose").GetBoolean();
    string? label = config.Get<string>("displayName");
    config.Set("interval", 1.0);
}
```

`Read()` 返回当前有效配置快照，`Get<T>(key)` 读取顶层值，`Set(key, value)` 原子写入一个顶层值并保留其他键。API 每次读取磁盘，模组可自行选择重新读取的时机，建议在加载或明确的刷新操作中使用。

### 原生 Config API

CGH 1.2.0 起，`cgh_host_api_config` 扩展表保留原 ABI v1 前缀。读取扩展前检查 `api->size >= sizeof(cgh_host_api_config)`。

```cpp
if (api->size >= sizeof(cgh_host_api_config)) {
    auto* config = reinterpret_cast<const cgh_host_api_config*>(api);
    int32_t length = config->config_read(api->context, nullptr, 0);
    if (length > 0) {
        // 分配 length 字节后再次调用 config_read。若返回值更大，请按新长度重试。
        const uint8_t interval[] = {'1', '.', '0'};
        config->config_set(api->context, "interval", interval, sizeof(interval));
    }
}
```

`config_read` 返回 UTF-8 JSON 所需字节数，缓冲区足够时复制完整快照。数据不含 NUL 结尾。`config_set` 接收单个 JSON 值，成功返回 `0`。两者在模组未提供模板时返回 `-1`，发生参数或配置错误时返回 `-2`。

## C#

目标 `net10.0-windows`，x64；引用发行目录 `SDK/ConsGHost.SDK.dll`，实现：

```csharp
using ConsGHost.SDK;

public sealed class MyMod : ICghMod
{
    private IModContext context = null!;
    public void Load(IModContext value)
    {
        context = value;
        context.RegisterService("echo", (input, output) =>
        {
            if (output.Length < input.Length) return -3;
            input.CopyTo(output);
            return input.Length;
        });
        context.Log("Loaded");
    }
    public void Start() { }
    public void Tick(double deltaSeconds) { }
    public void Stop() { }
}
```

`ResourcesDirectory` 为只读约定的资产目录，持久数据写 `DataDirectory`。`Stopping` 在清理时取消。
不要从后台线程调用引擎对象，使用 `RunOnGameThread`；此队列只有实际引擎帧才会执行，每帧最多 256 项。
`Call` 仅用于同进程目标；隔离进程间使用 `Subscribe/PublishAsync`。`FindNativeExport` 仅查本模组已映射的 Native DLL。
Hook delegate 的生存期必须覆盖整个 Hook；不要对可能仍在执行的回调释放 trampoline。

完整示例见 `CSharp/Examples/BridgeDemo` 和 `Observer`。

## C++

包含发行目录 `SDK/cgh_sdk.h`，按 Windows x64 编译。导出 `cgh_module_load`，其余 Start/Tick/Message/Stop 可选。

```cpp
#include "cgh_sdk.h"
static const cgh_host_api* host;
CGH_EXPORT int32_t CGH_CALL cgh_module_load(const cgh_host_api* api, const cgh_mod_info* mod) {
    if (!api || api->abi_version != 1 || api->size < sizeof(cgh_host_api)) return -1;
    host = api;
    host->log(host->context, "info", "Hello from native");
    return 0;
}
CGH_EXPORT void CGH_CALL cgh_module_stop() { /* join threads, release owned resources */ }
```

API/info 指针在整个进程生命周期有效；文本为 UTF-8；缓冲区只在当前调用有效，想保留需复制。
Load/Start 返回 0 才成功；失败仍尝试 Stop 以清理部分初始化。不得让 C++ 异常跨 C ABI；访问违规无法由框架安全恢复。
Stop 必须结束自己启动的后台任务。CGH 自动禁用通过 SDK 注册的 Hook，但无法识别模组自行修改的内存或第三方 Hook。
可以在一个模组内放多个 C#/C++ 入口；服务名在本模组内不可重复。

`loadAsSubprocess=true` 同时将该模组的 Managed 和 Native 部分放到同一个 MRT 进程，因此两侧仍可使用快速函数桥。
隔离进程没有 GMod 地址空间访问权；不要把指针发往另一个进程使用。跨进程数据通过消息传递。

CGH-LAPI 是独立模组。基础 SDK 提供托管和原生模组接口。
