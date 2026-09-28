using System.ComponentModel;
using System.Runtime.CompilerServices;

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

public sealed class UploadItem : INotifyPropertyChanged
{
    private string _status = "等待中";
    private string _remotePath = "";
    private string _url = "";
    public string LocalPath { get; init; } = "";
    public string? TargetRepositoryId { get; set; }
    public long OriginalLength { get; init; }
    public DateTime OriginalWriteTimeUtc { get; init; }
    public string Name => Path.GetFileName(LocalPath);
    public string Status { get => _status; set { _status = value; Changed(); } }
    public string RemotePath { get => _remotePath; set { _remotePath = value; Changed(); } }
    public string Url { get => _url; set { _url = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
