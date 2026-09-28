using System.Net;
using System.Net.Http;

namespace RepoTransit;

public sealed class UploadCoordinator(PlatformApi api, OAuthService oauth, CredentialStore secrets, ConfigStore configStore)
{
    public async Task<string> GetTokenAsync(AppConfig config, AccountConfig account, CancellationToken ct = default)
    {
        var access = secrets.Read(account.Id, "access") ?? throw new UnauthorizedAccessException("账号未授权，请在设置中重新授权。");
        if (account.ExpiresAt == null || account.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return access;
        var refresh = secrets.Read(account.Id, "refresh") ?? throw new UnauthorizedAccessException("授权已过期，请重新授权。");
        TokenResult result;
        try
        {
            result = account.Platform == Platform.GitHub
                ? await oauth.RefreshGitHubAsync(account.ClientId, refresh, ct)
                : await oauth.RefreshGiteeAsync(refresh, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            throw new UnauthorizedAccessException("刷新授权失败，请重新授权。", ex);
        }
        secrets.Save(account.Id, "access", result.AccessToken);
        if (result.RefreshToken != null) secrets.Save(account.Id, "refresh", result.RefreshToken);
        account.ExpiresAt = result.ExpiresAt;
        configStore.Save(config);
        return result.AccessToken;
    }

    public async Task UploadAsync(AppConfig config, RepositoryConfig repo, UploadItem item, CancellationToken ct = default)
    {
        var account = config.Accounts.SingleOrDefault(a => a.Id == repo.AccountId) ?? throw new InvalidOperationException("目标仓库的账号配置不存在。");
        var info = new FileInfo(item.LocalPath);
        if (!info.Exists) throw new FileNotFoundException("文件已被移动或删除。", item.LocalPath);
        if (info.Length != item.OriginalLength || info.LastWriteTimeUtc != item.OriginalWriteTimeUtc)
            throw new IOException("文件在排队后发生变化，请重新添加。");
        if (account.Platform == Platform.GitHub && info.Length > 100_000_000)
            throw new IOException("GitHub 不接受超过 100 MB 的普通仓库文件。");
        var token = await GetTokenAsync(config, account, ct);
        var now = DateTimeOffset.Now;
        string? path = null;
        for (var suffix = 1; suffix <= 100; suffix++)
        {
            ct.ThrowIfCancellationRequested();
            var candidate = PathRules.Build(info.Name, now, suffix);
            if (!await api.ExistsAsync(account, token, repo, candidate, ct)) { path = candidate; break; }
        }
        if (path == null) throw new IOException("同名文件过多，无法生成可用路径。");
        item.RemotePath = path;
        var bytes = await File.ReadAllBytesAsync(item.LocalPath, ct);
        try
        {
            info.Refresh();
            if (info.Length != item.OriginalLength || info.LastWriteTimeUtc != item.OriginalWriteTimeUtc)
                throw new IOException("文件在读取期间发生变化，请重新添加。");
            item.Url = await api.UploadAsync(account, token, repo, path, bytes, ct);
        }
        catch (PlatformException ex) when (IsPathConflict(ex))
        {
            for (var suffix = 2; suffix <= 100; suffix++)
            {
                var candidate = PathRules.Build(info.Name, now, suffix);
                if (await api.ExistsAsync(account, token, repo, candidate, ct)) continue;
                try
                {
                    item.Url = await api.UploadAsync(account, token, repo, candidate, bytes, ct);
                    item.RemotePath = candidate;
                    return;
                }
                catch (PlatformException retry) when (IsPathConflict(retry)) { }
            }
            throw new IOException("远端路径持续冲突，请稍后重试。", ex);
        }
        finally { Array.Clear(bytes); }
    }

    private static bool IsPathConflict(PlatformException ex) => ex.StatusCode == HttpStatusCode.Conflict ||
        (ex.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest &&
         (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
          ex.Message.Contains("sha", StringComparison.OrdinalIgnoreCase) ||
          ex.Message.Contains("已存在", StringComparison.Ordinal)));
}
