using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;

namespace RepoTransit;

public sealed class OAuthService
{
    private readonly GitHubOAuthClient _github;
    private readonly GiteeOAuthClient _gitee;

    public OAuthService(HttpClient? http = null, Action<string>? openBrowser = null)
    {
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RepoTransit/1.0");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _github = new GitHubOAuthClient(http);
        _gitee = new GiteeOAuthClient(http, openBrowser ?? OpenBrowser);
    }

    public Task<DeviceChallenge> StartGitHubAsync(string clientId, CancellationToken ct = default) => _github.StartAsync(clientId, ct);
    public Task<TokenResult> FinishGitHubAsync(string clientId, DeviceChallenge challenge, CancellationToken ct = default) => _github.FinishAsync(clientId, challenge, ct);
    public Task<TokenResult> RefreshGitHubAsync(string clientId, string refreshToken, CancellationToken ct = default) => _github.RefreshAsync(clientId, refreshToken, ct);
    public Task<TokenResult> AuthorizeGiteeAsync(string clientId, string secret, int port, CancellationToken ct = default) => _gitee.AuthorizeAsync(clientId, secret, port, ct);
    public Task<TokenResult> RefreshGiteeAsync(string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) =>
        _gitee.RefreshAsync(clientId, clientSecret, refreshToken, ct);

    public static void OpenBrowser(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
