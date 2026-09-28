# 架构与维护指南

仓渡由两个项目组成：`RepoTransit.Core` 负责规则、平台交互与配置，`RepoTransit` 负责 WPF 窗口。测试项目直接引用核心类库，不依赖窗口。没有引入服务容器或外部 UI 框架。

GitHub Actions 在 Windows 运行构建、行为检查和发布打包，工作流位于 `.github/workflows/build.yml`。

## 模块

| 位置 | 职责 |
| --- | --- |
| `src/RepoTransit.Core/Domain` | 账号、仓库、上传请求与结果模型；年月目录和文件命名规则 |
| `src/RepoTransit.Core/Storage` | JSON 配置和 Windows 凭据管理器读写 |
| `src/RepoTransit.Core/Auth` | GitHub 设备授权、Gitee 授权码回调、令牌解析和刷新 |
| `src/RepoTransit.Core/Platforms` | 平台仓库 API 接口、GitHub/Gitee 适配器及 HTTP 错误映射 |
| `src/RepoTransit.Core/Application` | 凭据续期、账号管理、仓库管理、上传协调及对象装配 |
| `src/RepoTransit` | 主窗口、设置窗口和仅用于界面绑定的上传队列状态 |
| `tests/RepoTransit.Tests` | 无真实凭据的规则、存储、授权回调、平台请求及服务检查 |

## 调用路径

`主窗口 → UploadCoordinator → TokenManager → CredentialStore` 负责取得有效令牌；随后 `UploadCoordinator → IRepositoryClient → GitHub/Gitee API` 检查路径并上传。上传服务只返回 `UploadResult`，不引用 WPF 控件或界面状态。

`设置窗口 → AccountManager` 保存授权后的账号与凭据；`设置窗口 → RepositoryManager → IRepositoryClient` 验证和保存目标仓库。两个窗口共享 `AppServices` 中的服务实例。更换平台接口实现时，优先只改对应适配器和其请求测试。

## 约束

- 非敏感配置在 `%APPDATA%/RepoTransit/config.json`；访问令牌、刷新令牌和 Gitee Client Secret 在 Windows 凭据管理器。日志和错误提示不得输出秘密。
- 核心类库不引用 WPF。`UploadItem` 属于界面层，核心只接收 `UploadRequest` 并返回 `UploadResult`。
- 新增平台时实现 `IRepositoryClient`，在 `RepositoryClientFactory` 注册，并增加相应授权客户端及模拟请求测试。
- 新增或修改上传规则时，先扩展 `tests/RepoTransit.Tests` 的行为检查，再修改核心实现。

## 验证边界

自动化检查使用模拟 HTTP 响应和本机回调，能够验证客户端构造的请求、状态校验及本地保存行为；它们不能证明平台实际接受 OAuth 应用设置、私有仓库写入或令牌续期。发布前还需要用用户自己的 GitHub 与 Gitee 测试私有仓库分别完成真实授权、上传和刷新验证。
