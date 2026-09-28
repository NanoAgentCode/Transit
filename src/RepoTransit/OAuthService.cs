using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RepoTransit;

public sealed record TokenResult(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);
public sealed record DeviceChallenge(string UserCode, string VerificationUri, string DeviceCode, int Interval, DateTimeOffset ExpiresAt);

public sealed class OAuthService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    public OAuthService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RepoTransit/1.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<DeviceChallenge> StartGitHubAsync(string clientId, CancellationToken ct = default)
    {
        using var response = await _http.PostAsync("https://github.com/login/device/code",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = clientId, ["scope"] = "repo" }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        return new DeviceChallenge(root.GetProperty("user_code").GetString()!, root.GetProperty("verification_uri").GetString()!,
            root.GetProperty("device_code").GetString()!, root.GetProperty("interval").GetInt32(),
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
    }

    public async Task<TokenResult> FinishGitHubAsync(string clientId, DeviceChallenge challenge, CancellationToken ct = default)
    {
        var interval = Math.Max(5, challenge.Interval);
        while (DateTimeOffset.UtcNow < challenge.ExpiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            using var response = await _http.PostAsync("https://github.com/login/oauth/access_token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId, ["device_code"] = challenge.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                }), ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (root.TryGetProperty("access_token", out var access))
            {
                var refresh = root.TryGetProperty("refresh_token", out var rf) ? rf.GetString() : null;
                DateTimeOffset? expires = root.TryGetProperty("expires_in", out var duration) ? DateTimeOffset.UtcNow.AddSeconds(duration.GetInt32()) : null;
                return new TokenResult(access.GetString()!, refresh, expires);
            }
            var error = root.GetProperty("error").GetString();
            if (error == "authorization_pending") continue;
            if (error == "slow_down") { interval += 5; continue; }
            throw new InvalidOperationException($"GitHub 授权失败：{error}");
        }
        throw new TimeoutException("GitHub 授权码已过期，请重试。");
    }

    public async Task<TokenResult> AuthorizeGiteeAsync(string clientId, string clientSecret, int port, CancellationToken ct = default)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var redirect = $"http://127.0.0.1:{port}/callback";
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            var url = "https://gitee.com/oauth/authorize?" + FormQuery(new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = "projects", ["state"] = state
            });
            OpenBrowser(url);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            while (true)
            {
                using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = socket.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                var first = await reader.ReadLineAsync(timeout.Token);
                if (first == null) continue;
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(timeout.Token))) { }
                var requestTarget = first.Split(' ').ElementAtOrDefault(1);
                if (requestTarget == null || !requestTarget.StartsWith("/callback?", StringComparison.Ordinal))
                {
                    await Reply(stream, "无效的回调请求。", ct);
                    continue;
                }
                var values = ParseQuery(new Uri("http://127.0.0.1" + requestTarget).Query);
                if (!values.TryGetValue("state", out var returnedState) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(returnedState), Encoding.ASCII.GetBytes(state)))
                {
                    await Reply(stream, "授权状态不匹配，请返回客户端重试。", ct);
                    continue;
                }
                if (!values.TryGetValue("code", out var code))
                {
                    await Reply(stream, "授权被取消，请返回客户端。", ct);
                    throw new InvalidOperationException("Gitee 授权被取消。");
                }
                await Reply(stream, "授权已完成，可以关闭此页面。", ct);
                return await ExchangeGiteeAsync(clientId, clientSecret, redirect, code, ct);
            }
        }
        finally { listener.Stop(); }
    }

    public async Task<TokenResult> RefreshGiteeAsync(string refreshToken, CancellationToken ct = default)
    {
        using var response = await _http.PostAsync("https://gitee.com/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }), ct);
        response.EnsureSuccessStatusCode();
        return ParseToken(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<TokenResult> RefreshGitHubAsync(string clientId, string refreshToken, CancellationToken ct = default)
    {
        using var response = await _http.PostAsync("https://github.com/login/oauth/access_token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken
            }), ct);
        response.EnsureSuccessStatusCode();
        return ParseToken(await response.Content.ReadAsStringAsync(ct));
    }

    private async Task<TokenResult> ExchangeGiteeAsync(string clientId, string clientSecret, string redirect, string code, CancellationToken ct)
    {
        using var response = await _http.PostAsync("https://gitee.com/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["code"] = code, ["client_id"] = clientId,
                ["client_secret"] = clientSecret, ["redirect_uri"] = redirect
            }), ct);
        response.EnsureSuccessStatusCode();
        return ParseToken(await response.Content.ReadAsStringAsync(ct));
    }

    private static TokenResult ParseToken(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (!root.TryGetProperty("access_token", out var token)) throw new InvalidOperationException("平台未返回访问令牌。");
        var refresh = root.TryGetProperty("refresh_token", out var rf) ? rf.GetString() : null;
        DateTimeOffset? expiry = root.TryGetProperty("expires_in", out var seconds) ? DateTimeOffset.UtcNow.AddSeconds(seconds.GetInt32()) : null;
        return new TokenResult(token.GetString()!, refresh, expiry);
    }

    public static void OpenBrowser(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private static string FormQuery(Dictionary<string, string> values) => string.Join('&', values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2)).ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => Uri.UnescapeDataString(parts.ElementAtOrDefault(1) ?? ""));
    private static async Task Reply(NetworkStream stream, string message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes($"<html><meta charset=\"utf-8\"><body><h2>{message}</h2></body></html>");
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(bytes, ct);
    }
}
