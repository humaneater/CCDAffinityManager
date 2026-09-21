# X3D CCD 亲和度管理器

<p align="center">
  <img src="assets/icon-preview.png" width="160" alt="X3D CCD 亲和度管理器图标">
</p>

这是一个 Windows x64 图形工具，用于把指定程序绑定到 AMD X3D 处理器的大缓存
CCD。工具会按 EXE 完整路径缓存规则，之后启动同一程序时自动应用，不需要重新
选择进程。

## 下载

最新安装版和免安装版位于
[GitHub Releases](https://github.com/humaneater/CCDAffinityManager/releases/latest)。

- 安装版：包含开始菜单、可选桌面快捷方式和卸载入口。
- 免安装版：解压后直接运行，不需要安装 .NET。

在 9950X3D 上，程序通常会自动识别：

```text
大缓存 CCD：CPU 0-15
亲和度掩码：0x0000FFFF
L3 缓存：96 MB
```

## 功能

- 从正在运行的进程或通过文件选择器添加 EXE。
- 进程列表显示全部可见进程；无法读取 EXE 路径的进程会明确标记并按进程名匹配。
- 自动识别 L3 缓存较大的 CCD，并允许手动选择逻辑处理器。
- 只处理规则中指定路径的 EXE，其他程序不受影响。
- 规则保存在本机配置文件中，重启工具后自动恢复。
- 点击“启用监控”后立即应用一次，并监听随后启动的匹配进程。
- 管理员模式下使用 WMI 进程启动事件。
- 标准权限下使用约 3 秒的新 PID 差量检测，只检查新出现的进程。
- 暂停规则、暂停监控或退出工具时恢复进程原来的亲和度。
- 可选设置 Windows 登录后自动启动，默认关闭。

## 使用方法

1. 运行 `CcdAffinityManager.exe`，在 Windows UAC 提示中选择“是”。
2. 使用“添加进程”选择当前运行的游戏，或使用“添加 EXE”选择游戏主程序。
3. 默认亲和度会自动选择大缓存 CCD。需要调整时点击“修改亲和度”。
4. 点击“启用监控”。当前已经运行的匹配程序会立即绑定。
5. 之后再次启动该 EXE 时，工具会自动应用缓存规则。
6. 点击“暂停监控”会停止检测并恢复原亲和度。
7. 点击窗口关闭按钮会退出工具并尝试恢复所有由本工具修改的进程。

游戏如果从 Steam、Epic 或厂商启动器启动，应选择实际游戏 EXE，而不是启动器
EXE。启动器派生的其他进程不会自动加入规则。

如果目标进程由管理员启动、受系统保护或反作弊组件阻止读取路径，进程选择器仍
会显示它，并标记为“按进程名匹配”。这种规则会影响所有使用同名 EXE 的进程；
程序已经以管理员权限运行，可以直接重新添加规则并使用“启动程序”。

## 权限模式

程序清单使用 `requireAdministrator`，每次启动都会由 Windows 请求一次 UAC
确认，确认后立即以管理员权限运行，并使用事件驱动的进程启动通知。受保护进程、
以管理员启动的游戏或部分反作弊环境通常也需要管理员权限。

## 恢复规则

程序在第一次修改目标进程前记录原亲和度。以下操作会尝试恢复：

- 在规则列表中取消启用。
- 删除规则。
- 点击“暂停监控”。
- 正常关闭工具。

如果工具被任务管理器强制结束，无法执行退出恢复。目标程序退出后亲和度会自然
失效；仍在运行且需要恢复时，可以重新打开工具并暂停监控。

## 构建

需要 .NET 9 SDK 和 Windows：

```powershell
dotnet restore .\CcdAffinityManager.sln
dotnet build .\CcdAffinityManager.sln -c Release
```

## 发布单文件 EXE

```powershell
.\publish.ps1
```

发布结果：

```text
publish\CcdAffinityManager.exe
```

该 EXE 是自包含的 Windows x64 单文件，不要求目标机器预装 .NET。

## 生成分享包

安装 Inno Setup 6 后执行：

```powershell
.\package.ps1
```

会生成以下两个文件：

```text
dist\CcdAffinityManager-1.0.1-portable.zip
dist\CcdAffinityManager-Setup-1.0.1.exe
```

免安装 ZIP 解压后直接运行；安装版包含开始菜单、可选桌面快捷方式和卸载入口。
两个文件旁也会生成对应的 `.sha256` 校验文件。

## 集成测试

集成测试会启动一个临时 `ping.exe`，验证拓扑识别、进程启动检测、亲和度应用和
原始值恢复：

```powershell
dotnet run --project .\tests\CcdAffinityManager.SmokeTest -c Debug
```

## 配置位置

```text
%LOCALAPPDATA%\CcdAffinityManager\settings.json
```

配置文件损坏时会被重命名为 `settings.json.broken`，工具会使用空配置继续启动。
