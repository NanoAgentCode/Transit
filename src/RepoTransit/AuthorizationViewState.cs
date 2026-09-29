namespace RepoTransit;

internal static class AuthorizationViewState
{
    internal static (string Status, string ButtonText) Read(AccountConfig account, CredentialStore credentials)
    {
        var authorized = credentials.Read(account.Id, "access") != null;
        return (authorized ? $"已授权：{account.Login}" : "待授权",
            authorized ? "重新授权" : "浏览器授权");
    }
}
