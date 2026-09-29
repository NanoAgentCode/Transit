# 仓渡 RepoTransit 学习路线

面向想通过本项目学习 Windows 桌面开发、OAuth 和仓库 API 的开发者。按“读一段代码 → 做一个小练习 → 运行验证”推进；无需先注册真实 OAuth 应用。建议先完成前五阶段，再进行真实平台联调。各阶段按掌握程度推进，不按固定天数安排。

## 开始前

- 环境：Windows 10/11、.NET 8 SDK、Git。使用 Visual Studio 或支持 C# 与 XAML 的编辑器即可。
- 克隆仓库后在仓库根目录运行 `dotnet build RepoTransit.sln` 和 `dotnet run --project tests/RepoTransit.Tests/RepoTransit.Tests.csproj`。先确保项目可构建且行为检查通过。
- 对照 [设计文档](design.md)了解目标与边界；遇到实现细节以当前代码为准。真实授权前阅读 [使用与验证](usage.md)，不要把令牌或 Client Secret 写入测试代码、截图、Issue 或提交记录。

## 阶段 1：理解项目全貌

**学习目标**：能说明 UI、业务规则、平台接口和存储的边界。

阅读 `src/RepoTransit/App.xaml.cs`、`src/RepoTransit/MainWindow.xaml.cs`、`src/RepoTransit.Core/Application/AppServices.cs` 和 [架构与维护指南](architecture.md)。沿着“选择文件 → 确认目标 → `UploadCoordinator.UploadAsync` → 平台客户端”的路径画出调用链，并说明单实例互斥锁在什么时间获取和释放。

**完成标志**：不看文档，也能指出“文件命名”“上传请求”“窗口状态”各在哪个项目实现。

## 阶段 2：C# 与业务规则

**学习目标**：掌握模型、异步方法、异常和可独立测试的纯规则。

阅读 `Domain/Models.cs`、`Domain/PathRules.cs` 与 `Application/UploadCoordinator.cs`。手算一个中文文件名、无扩展名文件和同名碰撞的目标路径，再运行 `tests/RepoTransit.Tests/Program.cs` 中的命名用例核对。尝试先写一个新的边界用例，例如文件名只有扩展名或序号为零，再观察规则如何处理。

**完成标志**：能解释 `UploadRequest` 为什么记录入队时的大小和修改时间，以及文件读取前后为何要复核。

## 阶段 3：本地配置与凭据

**学习目标**：理解多账号、多仓库的关系及敏感数据分离。

阅读 `Storage/ConfigStore.cs`、`Storage/CredentialStore.cs`、`Application/AccountManager.cs` 和 `Application/RepositoryManager.cs`。画出账号 ID、仓库配置与凭据条目的关联。用现有测试确认配置能往返保存、多个仓库能共享一个账号；再查看 `%APPDATA%/RepoTransit/config.json`，确认其中没有明文访问令牌。

**完成标志**：能说明删除本地配置、断开本机授权和在平台侧撤销授权的区别。

## 阶段 4：OAuth 与 HTTP API

**学习目标**：理解两种授权流程、令牌生命周期与平台适配器。

先读 `Auth/GitHubOAuthClient.cs`：标出设备码、用户码、轮询间隔及刷新令牌的用途。再读 `Auth/GiteeOAuthClient.cs`：标出本机回调、随机 `state` 校验和 Client Secret 的使用点。最后读 `Platforms/IRepositoryClient.cs`、`RepositoryClientBase.cs` 和两个平台适配器，比较上传方法及返回的文件页链接。

运行模拟 HTTP 测试，检查请求方法、授权头、分支、Base64 内容与冲突处理。不要把模拟测试当作真实平台验证。

**完成标志**：能解释 GitHub `repo` 授权范围为何不会随“目标仓库”下拉框缩小，以及 Gitee 为什么需要用户自己的第三方应用。

## 阶段 5：WPF 界面与单实例

**学习目标**：理解 XAML 样式、事件处理、界面状态和应用生命周期。

阅读 `App.xaml`、`MainWindow.xaml`、`MainWindow.xaml.cs`、`SettingsWindow.xaml.cs` 与 `UploadItem.cs`。跟踪拖拽事件如何加入队列、上传后如何更新状态、失败重试为何使用原目标。阅读 `SingleInstanceGate.cs` 和 `App.xaml.cs`，验证最小化进入托盘、托盘恢复，以及窗口隐藏时再次启动只唤醒原实例。

**完成标志**：能在不改业务服务的前提下调整一个界面文案或布局，并确认上传流程仍正常。

## 阶段 6：真实平台联调

**前提**：自己控制的测试私有仓库和 OAuth 应用；不要使用正式业务仓库做首次实验。

按 [使用与验证](usage.md)先完成 GitHub 授权和只读仓库检查，再上传一个无敏感内容的小文本文件，确认分支、月份目录和文件名。Gitee 按相同步骤验证本机回调、授权、上传；两平台分别验证令牌过期后的刷新或重新授权。`tests/RepoTransit.IntegrationTests` 默认只读，只有显式传 `--upload` 才写入测试文件。

**完成标志**：保存测试时间、平台、仓库、预期和实际结果；能区分自动化通过与真实平台通过。当前项目记录中，GitHub 私有仓库授权及小文件上传已验证，Gitee 授权已由用户确认；Gitee 上传和两平台刷新仍待验证。

## 阶段 7：独立完成一次小改动

挑选一个与当前功能直接相关的小任务，例如完善一个错误提示或增加一个路径边界用例。先写下完成标准；涉及规则或新功能时先写能复现的用例，再改实现。运行相关测试、`dotnet build RepoTransit.sln`，检查 `git diff --check` 和变更范围。新增或删除功能时同步更新 `README.md` 与 `docs`。

**完成标志**：变更只影响目标文件，测试能说明新行为，文档与代码一致。

## 查阅顺序

| 想弄清的问题 | 先看哪里 |
| --- | --- |
| 文件路径怎么生成 | `Domain/PathRules.cs` → `Application/UploadCoordinator.cs` |
| 账号和仓库怎么关联 | `Domain/Models.cs` → `AccountManager.cs` → `RepositoryManager.cs` |
| 令牌保存和刷新 | `Storage/CredentialStore.cs` → `Application/TokenManager.cs` |
| GitHub/Gitee 请求差异 | `Platforms/IRepositoryClient.cs` → `RepositoryClientBase.cs` → 两个适配器 |
| 拖拽和队列状态 | `MainWindow.xaml` → `MainWindow.xaml.cs` → `UploadItem.cs` |
| 自动化与真实联调边界 | `tests/RepoTransit.Tests` → `tests/RepoTransit.IntegrationTests` → [使用与验证](usage.md) |
