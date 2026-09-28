namespace RepoTransit;

public enum Platform { GitHub, Gitee }

public sealed class AccountConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Platform Platform { get; set; }
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ClientId { get; set; } = "";
    public int CallbackPort { get; set; } = 47831;
    public DateTimeOffset? ExpiresAt { get; set; }
    public override string ToString() => $"{Platform} · {DisplayName} ({Login})";
}

public sealed class RepositoryConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";
    public string Branch { get; set; } = "main";
    public string DisplayName { get; set; } = "";
    public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? $"{Owner}/{Name} · {Branch}" : $"{DisplayName} · {Owner}/{Name} · {Branch}";
}

public sealed class AppConfig
{
    public List<AccountConfig> Accounts { get; set; } = [];
    public List<RepositoryConfig> Repositories { get; set; } = [];
    public string? DefaultRepositoryId { get; set; }
}

public sealed record UploadRequest(string LocalPath, long OriginalLength, DateTime OriginalWriteTimeUtc);
public sealed record UploadResult(string RemotePath, string Url);
