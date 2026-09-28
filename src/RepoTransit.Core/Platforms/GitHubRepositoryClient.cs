using System.Net.Http;

namespace RepoTransit;

public sealed class GitHubRepositoryClient(HttpClient http) : RepositoryClientBase(http)
{
    protected override string BaseUrl => "https://api.github.com";
    protected override HttpMethod CreateMethod => HttpMethod.Put;
    protected override void ConfigureRequest(HttpRequestMessage request) => request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    protected override string FilePageUrl(RepositoryConfig repo, string path) =>
        $"https://github.com/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}/blob/{Uri.EscapeDataString(repo.Branch)}/{PathRules.EncodePath(path)}";
}
