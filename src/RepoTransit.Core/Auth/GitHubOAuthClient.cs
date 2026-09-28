using System.Net.Http;
using System.Text.Json;

namespace RepoTransit;

public sealed class GitHubOAuthClient(HttpClient http)
{
    public async Task<DeviceChallenge> StartAsync(string clientId, CancellationToken ct = default)
    {
        using var response = await http.PostAsync("https://github.com/login/device/code",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = clientId, ["scope"] = "repo" }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        return new DeviceChallenge(root.GetProperty("user_code").GetString()!, root.GetProperty("verification_uri").GetString()!,
            root.GetProperty("device_code").GetString()!, root.GetProperty("interval").GetInt32(),
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
    }

    public async Task<TokenResult> FinishAsync(string clientId, DeviceChallenge challenge, CancellationToken ct = default)
    {
        var interval = Math.Max(5, challenge.Interval);
        while (DateTimeOffset.UtcNow < challenge.ExpiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            using var response = await http.PostAsync("https://github.com/login/oauth/access_token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId, ["device_code"] = challenge.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                }), ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (root.TryGetProperty("access_token", out _)) return OAuthTokenParser.Parse(root);
            var error = root.GetProperty("error").GetString();
            if (error == "authorization_pending") continue;
            if (error == "slow_down") { interval += 5; continue; }
            throw new InvalidOperationException($"GitHub 授权失败：{error}");
        }
        throw new TimeoutException("GitHub 授权码已过期，请重试。");
    }

    public async Task<TokenResult> RefreshAsync(string clientId, string refreshToken, CancellationToken ct = default)
    {
        using var response = await http.PostAsync("https://github.com/login/oauth/access_token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken
            }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return OAuthTokenParser.Parse(json.RootElement);
    }
}
