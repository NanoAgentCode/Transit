using System.Net;

namespace RepoTransit;

public interface IRepositoryClient
{
    Task<string> GetLoginAsync(string token, CancellationToken ct = default);
    Task ValidateRepositoryAsync(string token, RepositoryConfig repo, CancellationToken ct = default);
    Task<bool> ExistsAsync(string token, RepositoryConfig repo, string path, CancellationToken ct = default);
    Task<string> UploadAsync(string token, RepositoryConfig repo, string path, byte[] bytes, CancellationToken ct = default);
}

public sealed class PlatformException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
