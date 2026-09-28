# 使用与验证

## 系统托盘

启动时会显示主窗口和托盘图标。最小化或点击窗口关闭按钮后，程序继续在托盘运行；双击图标或通过右键菜单的“打开仓渡”恢复窗口。需要真正结束程序时，在托盘右键菜单选择“退出”。窗口隐藏期间再次启动仓渡，也会唤起已有窗口，不会创建第二个实例。

## GitHub 授权

1. 登录 GitHub，打开 [Developer settings → OAuth apps](https://github.com/settings/developers)，点击 **New OAuth App**（首次可能显示 **Register a new application**）。
2. 填写应用名称（例如 `RepoTransit`）、Homepage URL（例如项目仓库主页），并在 Authorization callback URL 填入 `http://127.0.0.1/`。仓渡使用设备授权，此回调地址不会在授权时使用。
3. 勾选 **Enable Device Flow**，点击 **Register application**。保留默认的 **Expire user access tokens** 设置即可；仓渡支持设备授权产生的令牌刷新。
4. 在应用页面复制 **Client ID**，填入仓渡“账号与仓库设置”中的 GitHub 账号，点击授权。设备授权不需要填写 GitHub Client Secret。
5. 客户端会打开授权页并复制一次性验证码。在浏览器输入验证码、确认授权，返回客户端等待完成。

仓渡申请 `repo` 权限，用于访问私有仓库；选择一个上传目标不会缩小令牌的权限范围。客户端只在 Windows 凭据管理器保存返回的令牌，不保存 GitHub 密码。详见 [GitHub 创建 OAuth App](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/creating-an-oauth-app)和[设备授权说明](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps)。

## Gitee 授权

在 Gitee 第三方应用设置中创建应用，把回调地址设为 `http://127.0.0.1:47831/callback`。如需改端口，应用设置和客户端必须一致。客户端填写 Client ID 与 Client Secret，再点击“浏览器授权”。浏览器授权成功后会跳回本机临时回调页，客户端取得令牌。若端口被占用、回调地址不匹配或平台拒绝本机地址，客户端会显示错误；此时不要反复提交文件，应先修正应用设置。

## 仓库与上传

- 目标仓库必须已存在且为私有仓库。新建配置时会验证仓库及分支是否可访问。
- 主窗口选择目标，拖入文件，确认平台、仓库与分支，再开始上传。
- 每个文件独立报告结果；失败项可重试。重试使用原目标仓库，即使主窗口当前选择了其他仓库。
- 上传成功后选择结果行，点击“复制文件链接”或“复制仓库路径”。

## 开发验证

运行 `dotnet run --project tests/RepoTransit.Tests/RepoTransit.Tests.csproj`，覆盖路径规则、配置持久化、Windows 凭据管理器、两个平台的请求方法与路径碰撞、授权回调、配置服务和令牌刷新。`dotnet run --project tests/RepoTransit.IntegrationTests/RepoTransit.IntegrationTests.csproj` 会使用本机默认配置只读验证账号和私有仓库；明确加上 `-- --upload` 才会向默认仓库写入一个测试文本文件。GitHub 私有仓库授权、小文件上传和远端文件查询已于 2026-09-28 现场通过。Gitee 真实授权与上传、令牌刷新仍需相应测试仓库和 OAuth 应用验证；模拟接口测试不能代替这一步。
