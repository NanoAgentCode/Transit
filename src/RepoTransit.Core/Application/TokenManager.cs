using System.Net.Http;

namespace RepoTransit;

public sealed class TokenManager(OAuthService oauth, CredentialStore secrets, ConfigStore configStore)
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
                : await oauth.RefreshGiteeAsync(account.ClientId,
                    secrets.Read(account.Id, "client-secret") ?? throw new UnauthorizedAccessException("Gitee 应用密钥缺失，请重新授权。"), refresh, ct);
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
}
