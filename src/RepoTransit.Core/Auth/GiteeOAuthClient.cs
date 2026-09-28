using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RepoTransit;

public sealed class GiteeOAuthClient(HttpClient http, Action<string> openBrowser)
{
    public async Task<TokenResult> AuthorizeAsync(string clientId, string clientSecret, int port, CancellationToken ct = default)
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
            openBrowser(url);
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
                return await ExchangeAsync(clientId, clientSecret, redirect, code, ct);
            }
        }
        finally { listener.Stop(); }
    }

    public async Task<TokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        using var response = await http.PostAsync("https://gitee.com/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return OAuthTokenParser.Parse(json.RootElement);
    }

    private async Task<TokenResult> ExchangeAsync(string clientId, string clientSecret, string redirect, string code, CancellationToken ct)
    {
        using var response = await http.PostAsync("https://gitee.com/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["code"] = code, ["client_id"] = clientId,
                ["client_secret"] = clientSecret, ["redirect_uri"] = redirect
            }), ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return OAuthTokenParser.Parse(json.RootElement);
    }

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
