using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RepoTransit;

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
