using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RepoTransit;

public sealed class PlatformApi
{
    private readonly HttpClient _http;
    public PlatformApi(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RepoTransit/1.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string Base(Platform platform) => platform == Platform.GitHub ? "https://api.github.com" : "https://gitee.com/api/v5";
    private static string RepoPath(RepositoryConfig repo) => $"/repos/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}";

    private async Task<HttpResponseMessage> SendAsync(Platform platform, string token, HttpMethod method, string path, HttpContent? content = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, Base(platform) + path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (platform == Platform.GitHub) request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return await _http.SendAsync(request, ct);
    }

    public async Task<string> GetLoginAsync(Platform platform, string token, CancellationToken ct = default)
    {
        using var response = await SendAsync(platform, token, HttpMethod.Get, "/user", ct: ct);
        await EnsureSuccess(response, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("login").GetString() ?? throw new InvalidOperationException("平台未返回账号名。");
    }

    public async Task ValidateRepositoryAsync(AccountConfig account, string token, RepositoryConfig repo, CancellationToken ct = default)
    {
        using var response = await SendAsync(account.Platform, token, HttpMethod.Get, RepoPath(repo), ct: ct);
        await EnsureSuccess(response, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        if (!root.TryGetProperty("private", out var privateField) || !privateField.GetBoolean())
            throw new InvalidOperationException("目标仓库不是私有仓库。");
        if (root.TryGetProperty("permissions", out var permissions) && permissions.ValueKind == JsonValueKind.Object &&
            permissions.TryGetProperty("push", out var push) && !push.GetBoolean())
            throw new UnauthorizedAccessException("当前账号没有仓库写入权限。");
        using var branch = await SendAsync(account.Platform, token, HttpMethod.Get,
            RepoPath(repo) + "/branches/" + Uri.EscapeDataString(repo.Branch), ct: ct);
        await EnsureSuccess(branch, ct);
    }

    public async Task<bool> ExistsAsync(AccountConfig account, string token, RepositoryConfig repo, string path, CancellationToken ct = default)
    {
        var query = account.Platform == Platform.GitHub ? "?ref=" : "?ref=";
        using var response = await SendAsync(account.Platform, token, HttpMethod.Get,
            RepoPath(repo) + "/contents/" + PathRules.EncodePath(path) + query + Uri.EscapeDataString(repo.Branch), ct: ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureSuccess(response, ct);
        return true;
    }

    public async Task<string> UploadAsync(AccountConfig account, string token, RepositoryConfig repo, string path, byte[] bytes, CancellationToken ct = default)
    {
        var body = new { content = Convert.ToBase64String(bytes), message = $"Upload {path} via RepoTransit", branch = repo.Branch };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var method = account.Platform == Platform.GitHub ? HttpMethod.Put : HttpMethod.Post;
        using var response = await SendAsync(account.Platform, token, method, RepoPath(repo) + "/contents/" + PathRules.EncodePath(path), content, ct);
        await EnsureSuccess(response, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (json.RootElement.TryGetProperty("content", out var file) && file.TryGetProperty("html_url", out var url) &&
            url.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(url.GetString())) return url.GetString()!;
        var host = account.Platform == Platform.GitHub ? "https://github.com" : "https://gitee.com";
        return $"{host}/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}/blob/{Uri.EscapeDataString(repo.Branch)}/{PathRules.EncodePath(path)}";
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(ct);
        string message;
        try
        {
            using var json = JsonDocument.Parse(detail);
            message = json.RootElement.TryGetProperty("message", out var field) ? field.ToString() : response.ReasonPhrase ?? "请求失败";
        }
        catch (JsonException) { message = response.ReasonPhrase ?? "请求失败"; }
        var prefix = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "授权失效",
            HttpStatusCode.Forbidden => "无权限或触发平台限流",
            HttpStatusCode.NotFound => "仓库、分支或文件不存在",
            HttpStatusCode.Conflict => "远端路径冲突",
            HttpStatusCode.RequestEntityTooLarge => "文件超过平台限制",
            _ => "平台请求失败"
        };
        throw new PlatformException(response.StatusCode, $"{prefix}（{(int)response.StatusCode}）：{message}");
    }
}

public sealed class PlatformException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
