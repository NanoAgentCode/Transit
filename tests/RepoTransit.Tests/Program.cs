using RepoTransit;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

var failures = new List<string>();
await Check("授权按钮随凭据状态切换", () =>
{
    var account = new AccountConfig { Login = "tester" };
    var credentials = new CredentialStore();
    try
    {
        Equal(("待授权", "浏览器授权"), AuthorizationViewState.Read(account, credentials));
        credentials.Save(account.Id, "access", "test-token");
        Equal(("已授权：tester", "重新授权"), AuthorizationViewState.Read(account, credentials));
        credentials.Delete(account.Id, "access");
        Equal(("待授权", "浏览器授权"), AuthorizationViewState.Read(account, credentials));
    }
    finally { credentials.Delete(account.Id, "access"); }
    return Task.CompletedTask;
});
await Check("窗口贴边与越界恢复", () =>
{
    var workArea = new Rectangle(-1920, 0, 1920, 1040);
    Equal(new Rectangle(-1920, 0, 780, 600), WindowSnap.Snap(new Rectangle(-1908, 8, 780, 600), workArea, 16));
    Equal(new Rectangle(-780, 440, 780, 600), WindowSnap.Snap(new Rectangle(-770, 429, 780, 600), workArea, 16));
    Equal(new Rectangle(-1500, 200, 780, 600), WindowSnap.Snap(new Rectangle(-1500, 200, 780, 600), workArea, 16));
    Equal(new Rectangle(-780, 440, 780, 600), WindowSnap.KeepVisible(new Rectangle(100, 900, 780, 600), workArea));
    Equal(new Rectangle(-1920, 0, 2200, 1200), WindowSnap.KeepVisible(new Rectangle(3000, 2000, 2200, 1200), workArea));
    True(WindowSnap.TitleBarVisible(new Rectangle(-800, 100, 780, 600), [workArea]), "跨屏时可见的标题栏被误判为越界");
    True(!WindowSnap.TitleBarVisible(new Rectangle(100, 900, 780, 600), [workArea]), "屏幕外的标题栏未被识别");
    return Task.CompletedTask;
});
await Check("单实例互斥与释放", () =>
{
    var name = $@"Local\RepoTransit.Test.{Guid.NewGuid():N}";
    using var first = SingleInstanceGate.TryAcquire(name);
    True(first is not null, "首次启动未取得互斥锁");
    using var second = SingleInstanceGate.TryAcquire(name);
    True(second is null, "第二次启动取得了互斥锁");
    using var activated = new ManualResetEventSlim();
    using (var subscription = first!.OnActivation(() => activated.Set()))
    {
        True(SingleInstanceGate.SignalExisting(name), "第二次启动未能通知原实例");
        True(activated.Wait(TimeSpan.FromSeconds(2)), "原实例未收到唤醒信号");
    }
    first!.Dispose();
    using var third = SingleInstanceGate.TryAcquire(name);
    True(third is not null, "首次实例退出后仍无法启动");
    return Task.CompletedTask;
});
await Check("命名与年月目录", () =>
{
    var time = new DateTimeOffset(2026, 9, 24, 15, 30, 12, 345, TimeSpan.FromHours(8));
    Equal("2026-09/示意 图_20260924T153012345.png", PathRules.Build("示意 图.png", time));
    Equal("2026-09/README_20260924T153012345_2", PathRules.Build("README", time, 2));
    Equal("2026-09/%E7%A4%BA%E6%84%8F%20%E5%9B%BE.png", PathRules.EncodePath("2026-09/示意 图.png"));
    return Task.CompletedTask;
});
await Check("配置持久化与多个仓库", () =>
{
    var root = NewTestDirectory();
    try
    {
        var store = new ConfigStore(Path.Combine(root, "config.json"));
        var account = new AccountConfig { Platform = Platform.GitHub, Login = "tester" };
        var first = new RepositoryConfig { AccountId = account.Id, Owner = "owner", Name = "one" };
        var second = new RepositoryConfig { AccountId = account.Id, Owner = "owner", Name = "two" };
        store.Save(new AppConfig { Accounts = [account], Repositories = [first, second], DefaultRepositoryId = second.Id });
        var loaded = store.Load();
        Equal(2, loaded.Repositories.Count);
        Equal(second.Id, loaded.DefaultRepositoryId);
        Equal(account.Id, loaded.Repositories[0].AccountId);
        return Task.CompletedTask;
    }
    finally { Directory.Delete(root, true); }
});
await Check("Windows 凭据存储", () =>
{
    var store = new CredentialStore();
    var id = Guid.NewGuid().ToString("N");
    var otherId = Guid.NewGuid().ToString("N");
    try
    {
        store.Save(id, "access", "测试-token-✓");
        store.Save(otherId, "access", "second-token");
        Equal("测试-token-✓", store.Read(id, "access"));
        Equal("second-token", store.Read(otherId, "access"));
        store.Delete(id, "access");
        Equal("second-token", store.Read(otherId, "access"));
    }
    finally { store.Delete(id, "access"); store.Delete(otherId, "access"); }
    Equal<string?>(null, store.Read(id, "access"));
    return Task.CompletedTask;
});
await Check("GitHub 和 Gitee 上传请求及路径冲突", async () =>
{
    foreach (var platform in new[] { Platform.GitHub, Platform.Gitee })
    {
        var root = NewTestDirectory();
        var account = new AccountConfig { Platform = platform, Login = "tester" };
        var repo = new RepositoryConfig { AccountId = account.Id, Owner = "owner", Name = "private", Branch = "main" };
        var config = new AppConfig { Accounts = [account], Repositories = [repo], DefaultRepositoryId = repo.Id };
        var secrets = new CredentialStore();
        secrets.Save(account.Id, "access", "example-token");
        try
        {
            var file = Path.Combine(root, "示意 图.txt");
            await File.WriteAllTextAsync(file, "content", Encoding.UTF8);
            var info = new FileInfo(file);
            var request = new UploadRequest(file, info.Length, info.LastWriteTimeUtc);
            var handler = new FakeHandler(platform);
            var clients = new RepositoryClientFactory(new HttpClient(handler));
            var tokens = new TokenManager(new OAuthService(), secrets, new ConfigStore(Path.Combine(root, "config.json")));
            var coordinator = new UploadCoordinator(clients, tokens);
            await clients.For(platform).ValidateRepositoryAsync("example-token", repo);
            var result = await coordinator.UploadAsync(config, repo, request);
            True(result.RemotePath.StartsWith(DateTimeOffset.Now.ToString("yyyy-MM") + "/"), "月份目录不正确");
            True(result.RemotePath.EndsWith("_2.txt"), "远端碰撞未追加序号");
            Equal("https://example.test/file", result.Url);
            Equal(platform == Platform.GitHub ? "PUT" : "POST", handler.UploadMethod);
            Equal("example-token", handler.Authorization);
            Equal("main", handler.UploadBranch);
            Equal(Convert.ToBase64String(await File.ReadAllBytesAsync(file)), handler.UploadContent);
        }
        finally { secrets.Delete(account.Id, "access"); Directory.Delete(root, true); }
    }
});
await Check("GitHub 设备授权及令牌刷新", async () =>
{
    var handler = new FakeOAuthHandler();
    var oauth = new OAuthService(new HttpClient(handler));
    var challenge = await oauth.StartGitHubAsync("client-id");
    Equal("ABCD-EFGH", challenge.UserCode);
    Equal("repo", handler.DeviceScope);
    var token = await oauth.FinishGitHubAsync("client-id", challenge);
    Equal("gh-token", token.AccessToken);
    Equal("gh-refresh", token.RefreshToken);
    var refreshed = await oauth.RefreshGitHubAsync("client-id", "old-refresh");
    Equal("gh-token", refreshed.AccessToken);
    Equal("old-refresh", handler.RefreshToken);
});
await Check("Gitee 本机回调校验与换令牌", async () =>
{
    var handler = new FakeOAuthHandler();
    Task callback = Task.CompletedTask;
    var port = FreePort();
    var oauth = new OAuthService(new HttpClient(handler), url => callback = Task.Run(async () =>
    {
        var values = new Uri(url).Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Equal("projects", values["scope"]);
        using var client = new HttpClient();
        var redirect = values["redirect_uri"];
        var wrong = await client.GetStringAsync($"{redirect}?code=bad&state=wrong");
        True(wrong.Contains("不匹配"), "错误 state 未被拒绝");
        Equal(0, handler.GiteeExchanges);
        var valid = await client.GetStringAsync($"{redirect}?code=correct&state={values["state"]}");
        True(valid.Contains("授权已完成"), "正确回调未被接收");
    }));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var result = await oauth.AuthorizeGiteeAsync("gitee-client", "local-secret", port, timeout.Token);
    await callback;
    Equal("gt-token", result.AccessToken);
    Equal(1, handler.GiteeExchanges);
    Equal("correct", handler.GiteeCode);
    Equal("local-secret", handler.GiteeSecret);
    var refreshed = await oauth.RefreshGiteeAsync("gitee-client", "local-secret", "old-refresh");
    Equal("gt-token", refreshed.AccessToken);
    Equal("old-refresh", handler.RefreshToken);
    Equal("gitee-client", handler.GiteeRefreshClientId);
    Equal("local-secret", handler.GiteeRefreshSecret);
});
await Check("账号与仓库服务保持多配置和默认目标", async () =>
{
    var root = NewTestDirectory();
    var credentials = new CredentialStore();
    var configStore = new ConfigStore(Path.Combine(root, "config.json"));
    var clients = new RepositoryClientFactory(new HttpClient(new FakeHandler(Platform.GitHub)));
    var accountManager = new AccountManager(clients, credentials, configStore);
    var tokens = new TokenManager(new OAuthService(), credentials, configStore);
    var repositoryManager = new RepositoryManager(clients, tokens, configStore);
    var config = new AppConfig();
    var account = new AccountConfig { Platform = Platform.GitHub };
    try
    {
        var login = await accountManager.CompleteAuthorizationAsync(config, account, "client-id", "个人账号", 47831,
            new TokenResult("token-1", "refresh-1", DateTimeOffset.UtcNow.AddHours(1)));
        Equal("tester", login);
        Equal("token-1", credentials.Read(account.Id, "access"));
        var one = new RepositoryConfig();
        var two = new RepositoryConfig();
        await repositoryManager.SaveAsync(config, one, account, new RepositoryConfig { Owner = "owner", Name = "private", Branch = "main" });
        await repositoryManager.SaveAsync(config, two, account, new RepositoryConfig { Owner = "owner", Name = "private", Branch = "main" });
        Equal(2, config.Repositories.Count);
        Equal(one.Id, config.DefaultRepositoryId);
        repositoryManager.SetDefault(config, two);
        repositoryManager.Remove(config, two);
        Equal(one.Id, config.DefaultRepositoryId);
        accountManager.Disconnect(config, account);
        Equal<string?>(null, credentials.Read(account.Id, "access"));
        Equal(1, configStore.Load().Repositories.Count);
    }
    finally
    {
        foreach (var kind in new[] { "access", "refresh", "client-secret" }) credentials.Delete(account.Id, kind);
        Directory.Delete(root, true);
    }
});
await Check("到期令牌刷新并保存新凭据", async () =>
{
    var root = NewTestDirectory();
    var credentials = new CredentialStore();
    var store = new ConfigStore(Path.Combine(root, "config.json"));
    var account = new AccountConfig { Platform = Platform.Gitee, ClientId = "gitee-client", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
    var config = new AppConfig { Accounts = [account] };
    credentials.Save(account.Id, "access", "expired");
    credentials.Save(account.Id, "refresh", "old-refresh");
    credentials.Save(account.Id, "client-secret", "local-secret");
    try
    {
        var manager = new TokenManager(new OAuthService(new HttpClient(new FakeOAuthHandler())), credentials, store);
        Equal("gt-token", await manager.GetTokenAsync(config, account));
        Equal("gt-token", credentials.Read(account.Id, "access"));
        Equal("gt-refresh", credentials.Read(account.Id, "refresh"));
        True(store.Load().Accounts[0].ExpiresAt > DateTimeOffset.UtcNow, "新到期时间未保存");
    }
    finally
    {
        credentials.Delete(account.Id, "access"); credentials.Delete(account.Id, "refresh");
        credentials.Delete(account.Id, "client-secret");
        Directory.Delete(root, true);
    }
});
if (failures.Count > 0) { Console.Error.WriteLine(string.Join(Environment.NewLine, failures)); return 1; }
Console.WriteLine("全部 11 组检查通过。");
return 0;

async Task Check(string name, Func<Task> test)
{
    try { await test(); Console.WriteLine("通过：" + name); }
    catch (Exception ex) { failures.Add("失败：" + name + " — " + ex); }
}
static void Equal<T>(T expected, T actual)
{
    if (!Equals(expected, actual)) throw new Exception($"预期 [{expected}]，实际 [{actual}]");
}
static void True(bool result, string message)
{
    if (!result) throw new Exception(message);
}
static string NewTestDirectory()
{
    var root = Path.Combine(AppContext.BaseDirectory, "TestData", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
}
static int FreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}
sealed class FakeHandler(Platform platform) : HttpMessageHandler
{
    private int _contentGets;
    public string? UploadMethod { get; private set; }
    public string? Authorization { get; private set; }
    public string? UploadBranch { get; private set; }
    public string? UploadContent { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Authorization = request.Headers.Authorization?.Parameter;
        var expectedHost = platform == Platform.GitHub ? "api.github.com" : "gitee.com";
        if (request.RequestUri!.Host != expectedHost) throw new Exception("请求发送到了错误的平台。");
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/user") return Json(HttpStatusCode.OK, "{\"login\":\"tester\"}");
        if (path.EndsWith("/repos/owner/private")) return Json(HttpStatusCode.OK, "{\"private\":true,\"permissions\":{\"push\":true}}");
        if (path.EndsWith("/branches/main")) return Json(HttpStatusCode.OK, "{}");
        if (path.Contains("/contents/") && request.Method == HttpMethod.Get)
        { _contentGets++; return _contentGets == 1 ? Json(HttpStatusCode.OK, "{\"type\":\"file\"}") : Json(HttpStatusCode.NotFound, "{}"); }
        if (path.Contains("/contents/") && request.Method != HttpMethod.Get)
        {
            UploadMethod = request.Method.Method;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            UploadBranch = body.RootElement.GetProperty("branch").GetString();
            UploadContent = body.RootElement.GetProperty("content").GetString();
            return Json(HttpStatusCode.Created, "{\"content\":{\"html_url\":\"https://example.test/file\"}}");
        }
        throw new Exception($"意外请求：{request.Method} {request.RequestUri}");
    }
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

sealed class FakeOAuthHandler : HttpMessageHandler
{
    public string? DeviceScope { get; private set; }
    public string? RefreshToken { get; private set; }
    public string? GiteeCode { get; private set; }
    public string? GiteeSecret { get; private set; }
    public string? GiteeRefreshClientId { get; private set; }
    public string? GiteeRefreshSecret { get; private set; }
    public int GiteeExchanges { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var form = (await request.Content!.ReadAsStringAsync(ct)).Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p.ElementAtOrDefault(1) ?? ""));
        var uri = request.RequestUri!;
        if (uri.Host == "github.com" && uri.AbsolutePath == "/login/device/code")
        {
            DeviceScope = form["scope"];
            return Json("{\"user_code\":\"ABCD-EFGH\",\"verification_uri\":\"https://github.com/login/device\",\"device_code\":\"device-id\",\"interval\":1,\"expires_in\":60}");
        }
        if (uri.Host == "github.com" && uri.AbsolutePath == "/login/oauth/access_token")
        {
            if (form.TryGetValue("refresh_token", out var refresh)) RefreshToken = refresh;
            return Json("{\"access_token\":\"gh-token\",\"refresh_token\":\"gh-refresh\",\"expires_in\":3600}");
        }
        if (uri.Host == "gitee.com" && uri.AbsolutePath == "/oauth/token")
        {
            if (form.TryGetValue("refresh_token", out var refresh))
            {
                RefreshToken = refresh;
                GiteeRefreshClientId = form["client_id"];
                GiteeRefreshSecret = form["client_secret"];
            }
            else
            {
                GiteeExchanges++;
                GiteeCode = form["code"];
                GiteeSecret = form["client_secret"];
            }
            return Json("{\"access_token\":\"gt-token\",\"refresh_token\":\"gt-refresh\",\"expires_in\":3600}");
        }
        throw new Exception("意外 OAuth 请求：" + uri);
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
