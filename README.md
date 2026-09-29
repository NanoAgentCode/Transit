# 仓渡 RepoTransit

仓渡是 Windows 桌面文件上传客户端。它把任意类型的文件拖拽上传到你拥有写入权限的 GitHub 或 Gitee 私有仓库，支持多个账号与多个仓库。文件默认存放在 `YYYY-MM/`，名称为 `原文件名_时间戳.扩展名`；发生路径冲突时追加序号，不覆盖已有文件。

项目由独立的核心类库和 WPF 界面组成；界面采用贴近 Windows 11 的浅色卡片与圆角控件，并提供专用应用图标。模块边界与扩展方式见 [架构与维护指南](docs/architecture.md)。

想了解产品方案可读 [设计文档](docs/design.md)；希望按代码路径学习与实践可读 [学习路线](docs/learning-roadmap.md)。

## 运行环境

- Windows 10/11，x64
- 从源码运行需要 .NET 8 SDK；发布的独立版本不需要预先安装 .NET
- GitHub 或 Gitee 上已存在的私有仓库及写入权限

## 启动与构建

```powershell
dotnet run --project src/RepoTransit/RepoTransit.csproj
dotnet run --project tests/RepoTransit.Tests/RepoTransit.Tests.csproj
dotnet run --project tests/RepoTransit.IntegrationTests/RepoTransit.IntegrationTests.csproj
dotnet publish src/RepoTransit/RepoTransit.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

独立版本启动文件为 `publish/RepoTransit.exe`。构建输出与本地凭据不会提交到仓库。

## Windows 安装版

安装 [Inno Setup 6](https://jrsoftware.org/isdl.php) 后，在仓库根目录运行：

```powershell
.\scripts\build-installer.ps1 -Version 0.1.0
.\tests\Installer.Tests.ps1 -Version 0.1.0
```

安装包生成在 `artifacts/installer/RepoTransit-0.1.0-win-x64-setup.exe`，同目录附带 SHA-256 校验文件。安装到当前用户目录，无需管理员权限；安装、升级或卸载前须从托盘菜单退出正在运行的仓渡。卸载仅删除程序和快捷方式，保留 `%APPDATA%/RepoTransit` 配置及 Windows 凭据管理器中的授权信息。构建与验证步骤见 [安装版说明](docs/installer.md)。

同一台电脑一次只运行一个仓渡实例。再次启动时，程序会尝试将已打开的窗口切到前台，然后退出新进程。

启动后主窗口与系统托盘图标同时显示。最小化或点击窗口关闭按钮会将程序隐藏到托盘；双击托盘图标或选择“打开仓渡”可恢复窗口，选择“退出”才会结束程序。

## 首次配置

1. 打开“账号与仓库设置”，新增 GitHub 或 Gitee 账号。
2. 在对应平台创建自己的 OAuth 应用，并将 Client ID 填入客户端。GitHub OAuth 应用需要启用 Device Flow；客户端会打开浏览器并复制一次性验证码。Gitee 还需填写该应用的 Client Secret，并将应用回调地址设为 `http://127.0.0.1:47831/callback`，或与客户端设置的端口一致。
3. 浏览器授权完成后，在“目标仓库”填写已有私有仓库的拥有者、名称和分支，点击“验证并保存”。可重复添加账号和仓库，并设置一个默认仓库。
4. 在主窗口选择目标，将任意文件拖入窗口或点击“选择文件”，核对确认框后开始上传。

成功项可复制私有仓库文件页链接或仓库内路径。只有有仓库访问权限的人才能打开文件页链接；该链接不能用作公开网页图片直链。

GitHub OAuth 的 `repo` 授权覆盖账号可访问的私有仓库，上传目标选择不会缩小令牌本身的权限。请在授权页面核对权限。Gitee 需使用你自己注册的第三方应用，程序安装包不含共享应用密钥。

## 本地数据与限制

非敏感配置保存在 `%APPDATA%/RepoTransit/config.json`。访问令牌、刷新令牌及 Gitee Client Secret 保存在 Windows 凭据管理器中。点击“断开本机授权”会删除本机凭据；如需在平台侧撤销授权，还需到平台的授权管理页操作。

GitHub 普通仓库文件限制为 100 MB；Gitee 文件大小由其接口及仓库限制决定，超限时会显示平台错误。客户端按文件逐项上传，部分失败不会回滚已成功的文件。GitHub 私有仓库授权和小文件上传已现场验证；Gitee 本机回调与令牌刷新仍需要用户使用自己的第三方应用进行现场验证，详见 [设计文档](docs/design.md)。
