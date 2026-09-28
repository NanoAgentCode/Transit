using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RepoTransit;

public abstract class RepositoryClientBase(HttpClient http) : IRepositoryClient
{
    protected abstract string BaseUrl { get; }
    protected abstract HttpMethod CreateMethod { get; }
    protected virtual void ConfigureRequest(HttpRequestMessage request) { }
    private static string RepoPath(RepositoryConfig repo) => $"/repos/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}";

    private async Task<HttpResponseMessage> SendAsync(string token, HttpMethod method, string path, HttpContent? content = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, BaseUrl + path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        ConfigureRequest(request);
        return await http.SendAsync(request, ct);
    }

    public async Task<string> GetLoginAsync(string token, CancellationToken ct = default)
    {
        using var response = await SendAsync(token, HttpMethod.Get, "/user", ct: ct);
        await EnsureSuccess(response, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("login").GetString() ?? throw new InvalidOperationException("平台未返回账号名。");
    }

    public async Task ValidateRepositoryAsync(string token, RepositoryConfig repo, CancellationToken ct = default)
    {
        using var response = await SendAsync(token, HttpMethod.Get, RepoPath(repo), ct: ct);
        await EnsureSuccess(response, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        if (!root.TryGetProperty("private", out var privateField) || !privateField.GetBoolean())
            throw new InvalidOperationException("目标仓库不是私有仓库。");
        if (root.TryGetProperty("permissions", out var permissions) && permissions.ValueKind == JsonValueKind.Object &&
            permissions.TryGetProperty("push", out var push) && !push.GetBoolean())
            throw new UnauthorizedAccessException("当前账号没有仓库写入权限。");
        using var branch = await SendAsync(token, HttpMethod.Get,
            RepoPath(repo) + "/branches/" + Uri.EscapeDataString(repo.Branch), ct: ct);
        await EnsureSuccess(branch, ct);
    }

    public async Task<bool> ExistsAsync(string token, RepositoryConfig repo, string path, CancellationToken ct = default)
    {
        using var response = await SendAsync(token, HttpMethod.Get,
            RepoPath(repo) + "/contents/" + PathRules.EncodePath(path) + "?ref=" + Uri.EscapeDataString(repo.Branch), ct: ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureSuccess(response, ct);
        return true;
    }

    public async Task<string> UploadAsync(string token, RepositoryConfig repo, string path, byte[] bytes, CancellationToken ct = default)
    {
        var body = new { content = Convert.ToBase64String(bytes), message = $"Upload {path} via RepoTransit", branch = repo.Branch };
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await SendAsync(token, CreateMethod, RepoPath(repo) + "/contents/" + PathRules.EncodePath(path), content, ct);
        await EnsureSuccess(response, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (json.RootElement.TryGetProperty("content", out var file) && file.TryGetProperty("html_url", out var url) &&
            url.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(url.GetString())) return url.GetString()!;
        return FilePageUrl(repo, path);
    }

    protected abstract string FilePageUrl(RepositoryConfig repo, string path);

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
