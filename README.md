# SoftwareCrawler

自动爬取网页、下载软件安装包的 Windows 应用。

## 安装

在 PowerShell 中执行：

```powershell
irm https://raw.githubusercontent.com/tifish/SoftwareCrawler/main/install.ps1 | iex
```

中国大陆可以使用镜像地址：

```powershell
irm https://ghfast.top/https://raw.githubusercontent.com/tifish/SoftwareCrawler/main/install.ps1 | iex
```

脚本会下载最新 release 到 `%LOCALAPPDATA%\Programs\SoftwareCrawler`，创建开始菜单快捷方式并启动程序。
缺少 .NET 10 桌面运行库时会自动运行 `Setup.cmd` 安装。

卸载：退出程序，删除安装目录和开始菜单快捷方式即可，安装过程不写注册表。

## 配置存放位置

- 软件列表 `Config\Software.tab` 随程序发布并入库，下载目录 `Config\DownloadDirectory.tab` 与它同目录，按软件名关联，因此增删或调整顺序都不会错位。
- 因为程序目录下始终带 `Config`，运行时使用便携模式，设置也写在这里。
- 本机相关设置（本机路径、代理等）：`%LOCALAPPDATA%\SoftwareCrawler\Config`。
- 设置窗口可切换存放位置（默认 AppData / 便携 / 自定义），切换时会询问是否移动 `Config` 目录。

## 开发

```powershell
git clone --recurse-submodules https://github.com/tifish/SoftwareCrawler
```

- `Build.cmd` 编译，`Run.cmd` 编译并启动，`Publish.cmd` 发布优化版本到 `bin`。
- 需要 .NET 10 SDK。
