using System.Text.Json;

namespace RepoTransit;

internal static class OAuthTokenParser
{
    public static TokenResult Parse(JsonElement root)
    {
        if (!root.TryGetProperty("access_token", out var token)) throw new InvalidOperationException("平台未返回访问令牌。");
        var refresh = root.TryGetProperty("refresh_token", out var rf) ? rf.GetString() : null;
        DateTimeOffset? expiry = root.TryGetProperty("expires_in", out var seconds) ? DateTimeOffset.UtcNow.AddSeconds(seconds.GetInt32()) : null;
        return new TokenResult(token.GetString()!, refresh, expiry);
    }
}
