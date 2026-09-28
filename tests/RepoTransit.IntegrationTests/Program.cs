using RepoTransit;

var services = new AppServices();
var config = services.Config.Load();
var repo = config.Repositories.Single(r => r.Id == config.DefaultRepositoryId);
var account = config.Accounts.Single(a => a.Id == repo.AccountId);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var token = await services.Tokens.GetTokenAsync(config, account, timeout.Token);
var login = await services.Clients.For(account.Platform).GetLoginAsync(token, timeout.Token);
await services.Clients.For(account.Platform).ValidateRepositoryAsync(token, repo, timeout.Token);
Console.WriteLine($"认证与私有仓库访问通过：{account.Platform}，账号 {login}，仓库 {repo.Owner}/{repo.Name}，分支 {repo.Branch}");

if (!args.Contains("--upload")) return;

var localFile = Path.Combine(Path.GetTempPath(), "repotransit-test-" + Guid.NewGuid().ToString("N") + ".txt");
try
{
    await File.WriteAllTextAsync(localFile, "RepoTransit private repository upload test.\n");
    var info = new FileInfo(localFile);
    var result = await services.Uploads.UploadAsync(config, repo,
        new UploadRequest(localFile, info.Length, info.LastWriteTimeUtc), timeout.Token);
    if (!result.RemotePath.StartsWith(DateTimeOffset.Now.ToString("yyyy-MM") + "/repotransit-test-", StringComparison.Ordinal) ||
        !result.RemotePath.EndsWith(".txt", StringComparison.Ordinal))
        throw new Exception("远端路径不符合年月目录和原文件名规则。");
    if (!await services.Clients.For(account.Platform).ExistsAsync(token, repo, result.RemotePath, timeout.Token))
        throw new Exception("上传后无法从远端查询到测试文件。");
    Console.WriteLine($"真实上传通过：{result.RemotePath}");
    Console.WriteLine($"文件页：{result.Url}");
}
finally { File.Delete(localFile); }
