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

卸载：退出程序，删除安装目录和开始菜单快捷方式即可，安装过程不写注册表。开启过"开机启动"的话，先在设置里关掉它（或删掉 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `SoftwareCrawler` 值），那是程序唯一会写的注册表项。

## 定时下载

程序常驻在通知区域，自己管两套时间表，不需要注册系统计划任务：

- **全量下载**：设置窗口的"Download all at"填若干个时刻（默认 `00:00 08:00 13:00 18:30`），到点跑所有启用项。留空则不跑。空格、逗号、分号随便用，`9:00` 这种省略前导零的写法也认，保存后统一显示成空格分隔；写错的地方会当场提示，不会被悄悄丢掉。
- **高频检查**：在软件列表里勾选某一项的 `Frequent check`，它就会每 10 分钟（可在设置里改）单独检查一次。适合发版很勤、想第一时间拿到新包的少数几项——别勾太多，对方站点也扛不住。

两者都只在有新版本时才下载，没更新就什么都不做。

为了不打扰你的工作，后台轮次在这几种情况下不会开跑：已经有下载在进行、系统报告你正忙（全屏、投影、勿扰）。遇到这些直接跳过，等下一个时刻，不会补跑。主窗口开不开着不影响——否则你去看它的时候它恰好就不工作。

爬取用的浏览器窗口平时不占地方，需要看它在干什么就点主界面的 **Show browser**（"Show DevTools" 也会顺带把它调出来）。关掉那个浏览器窗口只是把它收起来，不会影响正在跑的下载。

关掉主窗口是收进通知区域，不是退出。**左键单击托盘图标可以显示或隐藏主窗口**，右键是菜单，退出在菜单里。设置窗口里可以打开"开机启动"，它在 `HKCU\...\Run` 下写一个值（这是程序唯一会写的注册表项），随后登录时直接进通知区域。想临时停掉，任务管理器的"启动应用"里也能禁用。

命令行 `SoftwareCrawler.exe --download-all --auto-close` 仍然可用，跑完就退，适合手工触发或交给外部调度。

## 失败自动修复

设置窗口的 "Fix failed rounds with" 可以选 Claude Code、Codex CLI 或 Grok Build（默认关闭）。选了之后，定时轮次里有项下载失败时，程序会在后台启动这个 agent：它读日志、用 Debug MCP 排查，修好配方或代码并验证，提交后 `git pull --rebase` 再 `git push`，最后用 `bin\RestartClient.ps1` 重启客户端。

- 需要一个 git checkout 作为工作目录："Repository to fix in" 留空时用程序自己所在的仓库（从仓库 `bin` 里跑 Debug 版就是这种情况），否则填一个。对应的 CLI 要已安装并登录，并且能直接 push。
- agent 以全权限无人值守运行（`--dangerously-skip-permissions` / `--dangerously-bypass-approvals-and-sandbox` / `--always-approve`），开启前请确认你接受这一点。
- 同一项交给 agent 后 24 小时内不会再交；成功一次就解除。agent 干活期间定时轮次暂停，最长 2 小时。
- 每次会话的说明和完整输出在 `bin\Logs\AutoFix\`。

## 配置存放位置

- 软件列表 `Templates\Software.tab` 只含爬取规则，随程序发布并入库。正式版首次运行时把它复制成 `Config\Software.tab` 使用，之后归用户所有，升级不会覆盖；开发版直接读写模板本身，改动即可提交。
- 本机设置 `Config\LocalSettings.tab` 保存启用状态和下载目录，不入库。它与软件列表按软件名关联，因此增删或调整顺序都不会错位。
- 因为程序目录下始终带 `Config`，运行时使用便携模式，设置也写在这里。
- 本机相关设置（本机路径、代理等）：`%LOCALAPPDATA%\SoftwareCrawler\Config`。
- 设置窗口可切换存放位置（默认 AppData / 便携 / 自定义），切换时会询问是否移动 `Config` 目录。

## 开发

```powershell
git clone --recurse-submodules https://github.com/tifish/SoftwareCrawler
```

- `Build.cmd` 做 Release 出包到 `bin`，`Run.cmd` 做 Debug 编译并启动。
- 需要 .NET 10 SDK。
