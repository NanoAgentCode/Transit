using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace RepoTransit;

public partial class MainWindow : Window
{
    private readonly AppServices _services = new();
    private readonly ObservableCollection<UploadItem> _items = [];
    private AppConfig _config;
    private CancellationTokenSource? _uploadCancel;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        WindowSnapBehavior.Attach(this);
        _config = _services.Config.Load();
        FilesGrid.ItemsSource = _items;
        RefreshTargets();
        UpdateSummary();
    }

    private sealed record TargetChoice(RepositoryConfig Repository, string DisplayLabel, string DetailLabel);
    private RepositoryConfig? SelectedRepository => (TargetBox.SelectedItem as TargetChoice)?.Repository;

    private void RefreshTargets(bool preferDefault = false)
    {
        var selected = preferDefault ? _config.DefaultRepositoryId : SelectedRepository?.Id ?? _config.DefaultRepositoryId;
        TargetBox.ItemsSource = _config.Repositories.Select(r =>
        {
            var account = _config.Accounts.FirstOrDefault(a => a.Id == r.AccountId);
            return new TargetChoice(r,
                $"{r.Owner}/{r.Name}",
                $"{account?.Platform.ToString() ?? "未关联"} · {r.Owner}/{r.Name} · {r.Branch} · {account?.Login ?? "待授权"}");
        }).ToList();
        TargetBox.SelectedItem = ((IEnumerable<TargetChoice>)TargetBox.ItemsSource).FirstOrDefault(r => r.Repository.Id == selected);
        TargetHint.Text = _config.Repositories.Count == 0 ? "请先添加账号与私有仓库" : "";
    }

    private void UpdateSummary() => QueueSummary.Text = $"{_items.Count} 个文件 · {_items.Sum(i => i.OriginalLength) / 1024.0 / 1024.0:F1} MB";

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var info = new FileInfo(path);
            if (!info.Exists) continue;
            _items.Add(new UploadItem { LocalPath = info.FullName, OriginalLength = info.Length, OriginalWriteTimeUtc = info.LastWriteTimeUtc });
        }
        UpdateSummary();
    }

    private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (_busy || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        AddFiles(((string[])e.Data.GetData(DataFormats.FileDrop)!).Where(File.Exists));
    }
    private void ChooseFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "所有文件|*.*" };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_config, _services) { Owner = this };
        window.ShowDialog();
        _config = _services.Config.Load();
        RefreshTargets(preferDefault: true);
    }
    private void TargetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedRepository != null) TargetHint.Text = $"目录：{DateTimeOffset.Now:yyyy-MM}/";
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        foreach (var item in _items.Where(i => i.Status == "成功").ToList()) _items.Remove(item);
        UpdateSummary();
    }
    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is UploadItem { Url.Length: > 0 } item) Clipboard.SetText(item.Url);
    }
    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is UploadItem { Status: "成功", RemotePath.Length: > 0 } item) Clipboard.SetText(item.RemotePath);
    }
    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || FilesGrid.SelectedItem is not UploadItem item || !item.Status.StartsWith("失败")) return;
        var original = _config.Repositories.FirstOrDefault(r => r.Id == item.TargetRepositoryId);
        if (original == null) { MessageBox.Show(this, "原目标仓库配置已删除，请重新添加文件。", "仓渡"); return; }
        await RunUploadAsync([item], original);
    }
    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RunUploadAsync(_items.Where(i => i.Status == "等待中").ToList());
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _uploadCancel?.Cancel();

    private async Task RunUploadAsync(IReadOnlyList<UploadItem> items, RepositoryConfig? retryTarget = null)
    {
        var repo = retryTarget ?? SelectedRepository;
        if (repo == null) { MessageBox.Show(this, "请先选择目标仓库。", "仓渡"); return; }
        if (items.Count == 0) { MessageBox.Show(this, "没有待上传的文件。", "仓渡"); return; }
        var account = _config.Accounts.FirstOrDefault(a => a.Id == repo.AccountId);
        if (account == null) { MessageBox.Show(this, "目标仓库没有可用账号，请在设置中重新关联。", "仓渡"); return; }
        if (MessageBox.Show(this, $"确认上传 {items.Count} 个文件到 {account.Platform} · {repo.Owner}/{repo.Name} · {repo.Branch}？", "确认目标仓库", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _busy = true;
        UploadButton.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        TargetBox.IsEnabled = false;
        CancelButton.IsEnabled = true;
        _uploadCancel = new CancellationTokenSource();
        try
        {
            var token = await _services.Tokens.GetTokenAsync(_config, account, _uploadCancel.Token);
            await _services.Clients.For(account.Platform).ValidateRepositoryAsync(token, repo, _uploadCancel.Token);
            foreach (var item in items)
            {
                if (_uploadCancel.IsCancellationRequested) break;
                item.Status = "上传中";
                item.TargetRepositoryId = repo.Id;
                item.RemotePath = "";
                item.Url = "";
                try
                {
                    var result = await _services.Uploads.UploadAsync(_config, repo,
                        new UploadRequest(item.LocalPath, item.OriginalLength, item.OriginalWriteTimeUtc), _uploadCancel.Token);
                    item.RemotePath = result.RemotePath;
                    item.Url = result.Url;
                    item.Status = "成功";
                }
                catch (OperationCanceledException) { item.Status = "等待中"; break; }
                catch (Exception ex)
                {
                    item.Status = "失败：" + ex.Message;
                    if (ex is UnauthorizedAccessException || ex is PlatformException { StatusCode: System.Net.HttpStatusCode.Unauthorized }) break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法开始上传", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally
        {
            _uploadCancel.Dispose();
            _uploadCancel = null;
            _busy = false;
            UploadButton.IsEnabled = true;
            SettingsButton.IsEnabled = true;
            TargetBox.IsEnabled = true;
            CancelButton.IsEnabled = false;
            StatusText.Text = $"成功 {_items.Count(i => i.Status == "成功")} · 失败 {_items.Count(i => i.Status.StartsWith("失败"))}";
        }
    }
}
