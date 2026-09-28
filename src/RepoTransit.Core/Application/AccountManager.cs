namespace RepoTransit;

public sealed class AccountManager(RepositoryClientFactory clients, CredentialStore credentials, ConfigStore configStore)
{
    public async Task<string> CompleteAuthorizationAsync(AppConfig config, AccountConfig account, string clientId,
        string displayName, int callbackPort, TokenResult result, string? clientSecret = null, CancellationToken ct = default)
    {
        var login = await clients.For(account.Platform).GetLoginAsync(result.AccessToken, ct);
        credentials.Save(account.Id, "access", result.AccessToken);
        if (result.RefreshToken != null) credentials.Save(account.Id, "refresh", result.RefreshToken);
        if (clientSecret != null) credentials.Save(account.Id, "client-secret", clientSecret);
        account.ClientId = clientId;
        account.CallbackPort = callbackPort;
        account.Login = login;
        account.DisplayName = string.IsNullOrWhiteSpace(displayName) ? login : displayName.Trim();
        account.ExpiresAt = result.ExpiresAt;
        if (!config.Accounts.Contains(account)) config.Accounts.Add(account);
        configStore.Save(config);
        return login;
    }

    public void Rename(AppConfig config, AccountConfig account, string displayName)
    {
        if (!config.Accounts.Contains(account)) throw new InvalidOperationException("账号配置不存在。");
        account.DisplayName = displayName.Trim();
        configStore.Save(config);
    }

    public void Disconnect(AppConfig config, AccountConfig account)
    {
        if (!config.Accounts.Contains(account)) throw new InvalidOperationException("账号配置不存在。");
        foreach (var key in new[] { "access", "refresh", "client-secret" }) credentials.Delete(account.Id, key);
        account.ExpiresAt = null;
        configStore.Save(config);
    }
}
