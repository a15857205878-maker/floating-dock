# FloatingDock · 悬浮控制中心

浮动在 Windows 桌面上的玻璃启动栏，集中打开常用应用、查看下载和整理文件。底座平时位于普通窗口下方，可停靠在屏幕上边缘或下边缘。

## 下载与启动

1. 在 [Releases](../../releases/latest) 下载 Windows x64 便携 ZIP，将整个压缩包解压到自己有写入权限的目录。
2. 双击 `FloatingDock.exe`。便携包内含 .NET Desktop Runtime，解压后即可启动。
3. 首次打开会读取当前 Windows 桌面上的快捷方式。可以将其他快捷方式拖到栏上，或右键固定到常用、修改名称和分组。

面向 Windows 11 x64。Windows 10 请参照 .NET 10 的系统支持范围。 [Microsoft .NET 10 系统支持表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)

要配置键盘唤回，在桌面创建指向 `FloatingDock.exe` 的 Windows 快捷方式，在目标路径的引号后添加 ` --show`，再在快捷方式属性中设置 `Ctrl+Alt+D` 或其他可用组合键。通过这个快捷方式可启动、唤回或收起 Dock。移动便携目录后，记得更新快捷方式目标。

## 功能

- **应用启动栏**支持六类分组、固定常用项目、拖动排序、搜索和快捷方式失效后的修复提示。
- **文件收纳**集中查看最近文件、下载、桌面及自选目录，可按类型筛选。Windows Shell 负责文件复制、移动和冲突处理。
- **四个桌面组件**分别显示天气、下载收件箱、常用文件夹与网易云音乐。可以独立移动、调整大小、折叠或隐藏。
- **天气**默认显示东京，可在组件菜单中更换城市。预报请求发送所选城市的坐标，城市搜索发送输入的名称。免费 API 供非商业用途，数据采用 CC BY 4.0。 [Open-Meteo 服务条款](https://open-meteo.com/en/terms)
- **网易云音乐**通过 Windows 系统媒体会话读取播放状态。使用时在网易云音乐的系统设置中开启 SMTC，然后播放歌曲。可用的播放操作取决于客户端提供的能力。

`data/settings.json` 保存快捷方式、文件夹和布局。移动到新目录时可以复制整个运行目录。遇到启动问题可查看同目录下的 `data/lifecycle.log` 和 `data/error.log`。

## 从源码构建

在 Windows 上安装 .NET 10 SDK。在仓库根目录运行以下命令生成便携构建。 [Microsoft .NET 10 下载](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

```powershell
dotnet restore DesktopDock.csproj -r win-x64 --source https://api.nuget.org/v3/index.json -p:SelfContained=true -p:RuntimeFrameworkVersion=10.0.15
dotnet publish DesktopDock.csproj -c Release -r win-x64 --self-contained true --no-restore -p:RuntimeFrameworkVersion=10.0.15 -o artifacts/FloatingDock
```

从输出目录运行 `FloatingDock.exe`，并保留生成的运行时文件及 `scripts/NetEaseMediaBridge.ps1`。

用 `pwsh -NoProfile -File scripts/Check.ps1 -Mode Core` 运行常规检查。界面及 Windows Shell 检查需要正常交互式桌面会话，先执行 `dotnet build launch-probe/LaunchProbe.csproj -c Debug`，再用 `pwsh -NoProfile -File scripts/Check.ps1 -Mode Desktop`。

## 权利与第三方组件

FloatingDock 保留现有版权与授权。便携包中的 Microsoft .NET 运行时附有其原始许可和第三方声明。
