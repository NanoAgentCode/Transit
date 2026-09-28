using System.Net.Http;
using System.Net.Http.Headers;

namespace RepoTransit;

public sealed class RepositoryClientFactory
{
    private readonly IRepositoryClient _github;
    private readonly IRepositoryClient _gitee;

    public RepositoryClientFactory(HttpClient? http = null)
    {
        http ??= new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RepoTransit/1.0");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _github = new GitHubRepositoryClient(http);
        _gitee = new GiteeRepositoryClient(http);
    }

    public IRepositoryClient For(Platform platform) => platform switch
    {
        Platform.GitHub => _github,
        Platform.Gitee => _gitee,
        _ => throw new ArgumentOutOfRangeException(nameof(platform))
    };
}
