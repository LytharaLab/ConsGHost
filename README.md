# ConsGHost (CGH)

**Con's GMod Host**：面向 Windows x64 Garry's Mod 的 C# / C++ 原生模组托管框架。

## 启动

需要 Windows 10/11 x64、.NET 10 **Desktop Runtime x64** 和已登录的 Steam。
源码构建另需 .NET 10 SDK、Visual Studio 2022 的 Desktop development with C++、Windows SDK、CMake 3.24+。

```powershell
./build.ps1
./artifacts/CGH/ConsGHost.Host.exe
```

也可以双击 `launch.cmd`。输出目录完整保存在 `artifacts/CGH`，可以整体移动；`Mods` 始终相对宿主 EXE，而不是终端工作目录。
`./package.ps1` 生成版本化的 Windows x64 压缩包和 SHA-256 校验文件。发行包从构建产物单独暂存，只包含自带示例模组、SDK 和文档，用户模组、设置和会话数据留在本地。

窗口包含：

- **模组管理**：扫描、启用/禁用、依赖预检、打开模组目录。
- **启动游戏 / 停止会话**：运行时握手、模块加载、依赖顺序启动和逆序停止。
- **额外 DLL**：增加、修改、删除多个目录，可启停与递归；全部 x64 原生 DLL 在模组之前加载至游戏进程。
- **设置**：游戏目录、每行一个启动参数、.NET 根目录、引擎 Hook、启动超时。

首次启动默认游戏目录为 `L:\SteamLibrary\steamapps\common\GarrysMod`。默认参数包含 `-insecure`，用于本地原生模组开发。
设置保存在宿主旁 `cgh_settings.json`。`Sessions/<会话>/events.jsonl` 是会话日志；模组数据在 `Data/<id>`。

## CGH 版本管理

产品版本以仓库根目录的 `VERSION` 为准。采用稳定版 `主版本.次版本.修订号`，每段取值为 0 至 65534。版本号会写入 Windows 文件属性、发行目录的 `version.json` 和会话日志，也会显示在宿主窗口。

```powershell
./version.ps1                                                        # 查看源码版本
./artifacts/CGH/ConsGHost.Runtime.exe --version                        # 查看已构建版本
./artifacts/CGH/ConsGHost.Runtime.exe --verify-version ./artifacts/CGH # 检查发行目录中的组件
./artifacts/CGH/cgh_mrt_win64.exe --version                            # 查看原生启动器版本
```

维护者可以用 `./version.ps1 -Set 1.1.0` 指定版本，或用 `./version.ps1 -Bump Patch` 提升修订号。主版本和次版本分别使用 `-Bump Major` 与 `-Bump Minor`。在 [更新日志](CHANGELOG.md) 中记录变更后，运行 `./test.ps1` 和 `./package.ps1 -SkipBuild`。后者只接受与源码版本一致的 Release 构建产物，生成 `CGH-<版本>-win-x64.zip` 和同名的 `.sha256` 校验文件。

CGH 产品版本、模组自己的版本号和原生 SDK 的 ABI 版本分别维护。

## 模组结构

```text
Mods/MyMod/
  cghm_metadata.json           # 必须
  Managed/                    # 任意数量的 .NET 10 DLL
  Native/                     # 任意数量的 Windows x64 原生 DLL
  Resources/                  # 任意资源，由模组自行使用
  Addons/                     # GMod 插件子目录或 .gma 文件，可在元数据中忽略指定项
```

元数据与 SDK 细节见 [模组开发](docs/mod-development.md)；进程、桥与生命周期见 [架构](docs/architecture.md)。
本机验收结果与证据边界见 [验证记录](docs/verification.md)。
随发行目录附有 `BridgeDemo` 和 `Observer`：双向原生桥、100000 次调用校验、引擎线程派发和隔离子进程消息往返。

## 验证

```powershell
./test.ps1                         # 原生 detour + 完整受控测试程序链路
./test.ps1 -SkipBuild -RealGame     # 暂时禁用既有 addons/workshop，载入 gm_construct 并验证 300 个实际帧
```

测试程序和真实 GMod 验证分开报告；支持的引擎接口是 `ServerGameDLL009`。未匹配接口时明确报错。
真实游戏测试只给该次进程附加 `-noaddons -noworkshop`，不修改 Steam 启动设置或删除 addons；宿主日常启动不自动添加这两个参数。
CGH 模组使用自己的 `ICghMod` / `cgh_module_load` 入口。标准 GMod 插件由游戏通过 `Addons/` 提供的内容加载。

原生 DLL 固定到进程生命周期；更换模组需要重启游戏会话。隔离进程提供崩溃边界，**不提供安全沙箱**。
原生 SDK 允许模组按明确 ABI 创建 Hook；模组负责不在回调中抛出 C++ 异常并停止自己的线程。不要在 `DllMain` 中启动 CLR 或加载模组。

第三方 MinHook 1.3.4 源码随附，许可证见 `Cpp/third_party/minhook-1.3.4/LICENSE.txt`。
