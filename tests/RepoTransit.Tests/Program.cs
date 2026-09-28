using RepoTransit;
using System.Net;
using System.Text;
using System.Text.Json;

var failures = new List<string>();
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
            var item = new UploadItem { LocalPath = file, OriginalLength = info.Length, OriginalWriteTimeUtc = info.LastWriteTimeUtc };
            var handler = new FakeHandler(platform);
            var api = new PlatformApi(new HttpClient(handler));
            var coordinator = new UploadCoordinator(api, new OAuthService(), secrets, new ConfigStore(Path.Combine(root, "config.json")));
            await api.ValidateRepositoryAsync(account, "example-token", repo);
            await coordinator.UploadAsync(config, repo, item);
            True(item.RemotePath.StartsWith(DateTimeOffset.Now.ToString("yyyy-MM") + "/"), "月份目录不正确");
            True(item.RemotePath.EndsWith("_2.txt"), "远端碰撞未追加序号");
            Equal("https://example.test/file", item.Url);
            Equal(platform == Platform.GitHub ? "PUT" : "POST", handler.UploadMethod);
            Equal("example-token", handler.Authorization);
            Equal("main", handler.UploadBranch);
            Equal(Convert.ToBase64String(await File.ReadAllBytesAsync(file)), handler.UploadContent);
        }
        finally { secrets.Delete(account.Id, "access"); Directory.Delete(root, true); }
    }
});
if (failures.Count > 0) { Console.Error.WriteLine(string.Join(Environment.NewLine, failures)); return 1; }
Console.WriteLine("全部 4 组检查通过。");
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
