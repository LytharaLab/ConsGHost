# ConsGHost

[English](../../README.md) · **简体中文** · [Русский](README.ru.md) · [Español](README.es.md) · [Français](README.fr.md)

ConsGHost（CGH）为 Windows x64 版 Garry's Mod 加载 C# 和 C++ 模组。模组可以在游戏内运行，也可以使用独立进程。模组还可以附带目录形式或 `.gma` 格式的常规 GMod 插件。

## 安装与启动

1. 使用 Windows 10 或 11，安装 [Windows x64 版 .NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)，登录 Steam，并确认已安装带有 `gmod_win64.exe` 的 Garry's Mod。
2. 从 [GitHub Releases](https://github.com/LytharaLab/ConsGHost/releases) 下载 `CGH-<版本>-win-x64.zip`，完整解压到有写入权限的文件夹。
3. 运行 `ConsGHost.Host.exe`，在侧栏最后一项 **设置** 中选择 `gmod_win64.exe` 所在的目录。可从 Steam 的 **管理 → 浏览本地文件** 打开该目录。
4. 在模组管理中查看已启用的模组，然后从 CGH 启动游戏。发行包附带 BridgeDemo 和 Observer，分别演示 C# 与 C++ 之间的调用和跨进程消息。

CGH 默认使用 `-insecure` 启动游戏，适用于本地开发。请在本地会话中使用，只加载你信任的模组和额外 DLL。独立进程可以隔离崩溃，仍使用当前 Windows 用户的权限。

CGH 默认跟随 Windows 显示语言，遇到未支持的语言时使用英文。可在 **设置 → 界面语言** 选择英文、简体中文、俄文、西班牙语或法语。更改立即生效，并会在下次启动时保留。

## 安装模组

在 `ConsGHost.Host.exe` 旁的 `Mods/` 下为每个模组建一个目录。每个模组必须有 `cghm_metadata.json`，可按需包含 `Managed/`、`Native/`、`Resources/`、`Addons/` 和 `config.default.json`。CGH 首次发现带模板的模组时会生成 `config.json`。SDK 和元数据格式见[模组开发文档](../mod-development.md)。

设置保存在 `cgh_settings.json`，会话日志保存在 `Sessions/`，模组数据保存在 `Data/<mod-id>/`。升级时先解压到新目录，再迁移需要保留的模组与数据。保留模组配置，并在启动前检查设置。

## 从源码构建

需要 .NET 10 SDK、带 **使用 C++ 的桌面开发** 工作负载的 Visual Studio 2022 Build Tools、Windows SDK 和 CMake 3.24 或更高版本。在仓库根目录打开 PowerShell，执行以下命令。

```powershell
./build.ps1
./artifacts/CGH/ConsGHost.Host.exe
```

`./test.ps1` 运行原生、集成和桌面界面测试。`./package.ps1` 在 `artifacts/` 下生成 Windows x64 ZIP 和 SHA-256 校验文件。参见[验证指南](../verification.md)。

## 项目文档

- [架构](../architecture.md)
- [更新日志](../../CHANGELOG.md)
- [许可证和第三方声明](../../NOTICE.md)
- [安全报告与诊断数据](../../SECURITY.md)

ConsGHost 是独立的社区项目。项目自有代码和文档采用 [MIT 许可证](../../LICENSE)。
