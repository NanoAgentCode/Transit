using System.Net.Http;

namespace RepoTransit;

public sealed class GiteeRepositoryClient(HttpClient http) : RepositoryClientBase(http)
{
    protected override string BaseUrl => "https://gitee.com/api/v5";
    protected override HttpMethod CreateMethod => HttpMethod.Post;
    protected override string FilePageUrl(RepositoryConfig repo, string path) =>
        $"https://gitee.com/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}/blob/{Uri.EscapeDataString(repo.Branch)}/{PathRules.EncodePath(path)}";
}
