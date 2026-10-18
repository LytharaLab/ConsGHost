# CGH 0.1.0 验证记录

日期：2026-10-19（Asia/Shanghai）。系统 Windows 10 x64；.NET SDK 10.0.402 / Runtime 10.0.13；MSVC 19.44。

## 本地自动验收

`./test.ps1 -SkipBuild`：原生 CTest 1/1 通过，C# 验收 35 项通过。

覆盖依赖排序、循环/重复/禁用/版本错误、元数据错误、路径越界、托管 DLL 误放 Native、IPC 帧边界，及：

- 真正创建进程、初始化 Windows 加载器、在应用 PE 入口前装入 CGH、hostfxr 启动 .NET 10。
- 任意包含空格和中文的发行目录运行。
- 同进程 C# → C++、C++ → C# 服务调用，100000 次有效载荷 echo 校验。
- 游戏进程 → 隔离模组 → 游戏进程消息往返。
- 额外依赖映射至游戏进程，游戏实际调用导出并得到 42。
- 合成 GMod 七方法接口连续 300 帧 detour，游戏线程派发。
- 原生 Hook trampoline、禁用、移除。
- 正常逆序停止、重启会话、隔离模组崩溃回滚。
- Load / Start 故障注入的清理，启动中取消。
- 同一模组的 C# / C++ 同时运行在独立 MRT 并保留快速桥。

最终异常清理逻辑完成后，连续三轮各 35 项通过；另有一次包含 UI 自检的完整验收通过。
证据：`artifacts/acceptance-repeat-1.log` 至 `acceptance-repeat-3.log`、`artifacts/tests/c8d375d7d3d041c2b9718a04124a7f23/result.txt` 及其会话日志。

## 真实 GMod

`./test.ps1 -SkipBuild -RealGame`：24 项通过，使用本机 `L:\SteamLibrary\steamapps\common\GarrysMod\gmod_win64.exe`。

该次游戏进程附加 `-noaddons -noworkshop`，避免既有插件干扰；CGH 模组仍照常加载。
载入 `gm_construct` 后，最终构建的 C# 示例完成 **307 次真实 GameFrame 回调**，并验证了游戏线程派发、双向服务调用、跨进程消息、额外 DLL 加载和生命周期清理。停止后确认本次游戏 PID 已退出。

本机该次测量：27 字节负载、100000 次 C# → 原生 echo 共 6.9572 ms，平均 69.572 ns/次。这是单次运行的桥接小负载测量，不能视为所有模组或跨进程消息的性能保证。

最终原生运行时 SHA256：`eec3afd2a6514712fe9d481e29063e2b721c95c4fcdcb5b6f635a872b5fd75e9`。
已核对真实游戏测试拷贝与发行目录一致。
托管 Bootstrap SHA256：`625029f91f9d5af7a56c4ba69610f7642f71b869f1a7d957ae5b66df5a502013`。

证据：`artifacts/tests/d685364657284f97b5cc61433d8fd0ce/result.txt`、其中 `CGH space 中文/Sessions/20261019-053132-3ac8e502/events.jsonl` 和 `Data/cgh.example/bridge-metrics.json`。

持续帧验收修正了早期适配器误用 Valve 通用 vtable 的问题：GMod 的 `PreInit` 使 GameFrame 位于第 6 槽。早期仅捕获单次 ServerActivate 的结果不计入 GameFrame 通过证据；当前夹具和真实游戏均使用修正后的接口。

## UI

宿主自带 `--ui-check OUTPUT_DIRECTORY`：运行时连接、三页面渲染、模组启用状态持久化与还原、设置保存通过。
渲染文件在 `artifacts/ui-check/page-0.png` 至 `page-2.png`。另通过桌面可访问性树确认宿主窗口响应、模组列表和主要按钮存在。
桌面捕获服务返回过 `window capture timed out`；渲染图来自 WinForms 自身的 DrawToBitmap，不作为真实 GMod 画面截图。

## 已知边界

验收针对本机 GMod 与指定接口，未宣称所有 GMod 版本、第三方注入器或既有 addon 组合兼容。隔离不是安全沙箱；Native 错误可使所在进程崩溃。高级 Lua/实体 API、资源自动挂载及 CGH-LAPI 均留给后续独立模组。
