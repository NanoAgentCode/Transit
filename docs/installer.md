# Windows 安装版

## 构建

需要 Windows 10/11、.NET 8 SDK 和 [Inno Setup 6](https://jrsoftware.org/isdl.php)。在仓库根目录运行：

```powershell
dotnet run --project tests/RepoTransit.Tests/RepoTransit.Tests.csproj -c Release
.\scripts\build-installer.ps1 -Version 0.1.0
```

脚本先发布 `win-x64` 自包含单文件应用，再用 `installer/RepoTransit.iss` 编译安装程序。找不到编译器时可传入 `-IsccPath`。版本号须为 `x.y.z`，同时写入应用及安装程序。输出为 `artifacts/installer/RepoTransit-<版本>-win-x64-setup.exe` 和同名 `.sha256` 文件；构建暂存目录位于 `artifacts/installer-staging`，两者均不提交到 Git。

`installer/ChineseSimplified.isl` 来自 [Inno Setup 官方源码仓库](https://github.com/jrsoftware/issrc/blob/main/Files/Languages/ChineseSimplified.isl)，随项目固定，避免构建机器缺少简体中文语言文件。

## 安装与升级

安装程序将仓渡放入当前用户的 `%LOCALAPPDATA%/Programs/RepoTransit`，创建开始菜单快捷方式，并提供可选的桌面快捷方式。无需管理员权限。安装程序使用固定的 AppId，后续版本可覆盖升级；应用运行时的命名互斥锁会阻止安装或卸载，请先从托盘菜单选择“退出”。

卸载删除程序文件和快捷方式，不删除 `%APPDATA%/RepoTransit/config.json` 或 Windows 凭据管理器中的令牌。需要彻底清除本机授权时，先在应用内“断开本机授权”，再卸载；平台侧撤销授权须在 GitHub/Gitee 单独操作。

## 验证与发布

在未安装仓渡的测试环境运行 `./tests/Installer.Tests.ps1 -Version 0.1.0`。脚本以独立目录静默安装，检查应用版本与启动，再静默卸载；检测到现有安装时会拒绝执行。正式发布前，还应手动检查安装向导、开始菜单快捷方式、托盘退出、覆盖升级和卸载后的配置保留。

`.github/workflows/installer.yml` 支持手动输入版本号或推送 `vX.Y.Z` 标签，校验并安装固定版本的 Inno Setup 6.7.3，执行构建并上传安装包及校验文件为 Actions 产物。它不自动发布 GitHub Release。公开分发前需自行决定代码签名方案；Gitee 授权已由用户确认，真实上传及两个平台的令牌刷新仍待现场验证。
