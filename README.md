# 仓渡 RepoTransit

仓渡是 Windows 桌面文件上传客户端。它把任意类型的文件拖拽上传到你拥有写入权限的 GitHub 或 Gitee 私有仓库，支持多个账号与多个仓库。文件默认存放在 `YYYY-MM/`，名称为 `原文件名_时间戳.扩展名`；发生路径冲突时追加序号，不覆盖已有文件。

## 运行环境

- Windows 10/11，x64
- 从源码运行需要 .NET 8 SDK；发布的独立版本不需要预先安装 .NET
- GitHub 或 Gitee 上已存在的私有仓库及写入权限

## 启动与构建

```powershell
dotnet run --project src/RepoTransit/RepoTransit.csproj
dotnet run --project tests/RepoTransit.Tests/RepoTransit.Tests.csproj
dotnet publish src/RepoTransit/RepoTransit.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

独立版本启动文件为 `publish/RepoTransit.exe`。构建输出与本地凭据不会提交到仓库。

## 首次配置

1. 打开“账号与仓库设置”，新增 GitHub 或 Gitee 账号。
2. 在对应平台创建自己的 OAuth 应用，并将 Client ID 填入客户端。GitHub OAuth 应用需要启用 Device Flow；客户端会打开浏览器并复制一次性验证码。Gitee 还需填写该应用的 Client Secret，并将应用回调地址设为 `http://127.0.0.1:47831/callback`，或与客户端设置的端口一致。
3. 浏览器授权完成后，在“目标仓库”填写已有私有仓库的拥有者、名称和分支，点击“验证并保存”。可重复添加账号和仓库，并设置一个默认仓库。
4. 在主窗口选择目标，将任意文件拖入窗口或点击“选择文件”，核对确认框后开始上传。

成功项可复制私有仓库文件页链接或仓库内路径。只有有仓库访问权限的人才能打开文件页链接；该链接不能用作公开网页图片直链。

GitHub OAuth 的 `repo` 授权覆盖账号可访问的私有仓库，上传目标选择不会缩小令牌本身的权限。请在授权页面核对权限。Gitee 需使用你自己注册的第三方应用，程序安装包不含共享应用密钥。

## 本地数据与限制

非敏感配置保存在 `%APPDATA%/RepoTransit/config.json`。访问令牌、刷新令牌及 Gitee Client Secret 保存在 Windows 凭据管理器中。点击“断开本机授权”会删除本机凭据；如需在平台侧撤销授权，还需到平台的授权管理页操作。

GitHub 普通仓库文件限制为 100 MB；Gitee 文件大小由其接口及仓库限制决定，超限时会显示平台错误。客户端按文件逐项上传，部分失败不会回滚已成功的文件。Gitee 本机回调与令牌刷新仍需要用户使用自己的第三方应用进行现场验证，详见 [设计文档](docs/design.md)。
