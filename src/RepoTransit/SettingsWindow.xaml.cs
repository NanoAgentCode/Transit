using System.Windows;
using System.Windows.Controls;

namespace RepoTransit;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly AppServices _services;
    private AccountConfig? _draftAccount;
    private RepositoryConfig? _draftRepo;
    private CancellationTokenSource? _authCancel;

    public SettingsWindow(AppConfig config, AppServices services)
    {
        InitializeComponent();
        _config = config; _services = services;
        RefreshLists();
    }

    private AccountConfig? SelectedAccount => _draftAccount ?? AccountsList.SelectedItem as AccountConfig;
    private RepositoryConfig? SelectedRepo => _draftRepo ?? RepositoriesList.SelectedItem as RepositoryConfig;

    private void RefreshLists()
    {
        var accountId = (AccountsList.SelectedItem as AccountConfig)?.Id;
        var repoId = (RepositoriesList.SelectedItem as RepositoryConfig)?.Id;
        AccountsList.ItemsSource = null;
        AccountsList.ItemsSource = _config.Accounts.ToList();
        AccountsList.SelectedItem = _config.Accounts.FirstOrDefault(a => a.Id == accountId);
        RepoAccount.ItemsSource = null;
        RepoAccount.ItemsSource = _config.Accounts.ToList();
        RepositoriesList.ItemsSource = null;
        RepositoriesList.ItemsSource = _config.Repositories.ToList();
        RepositoriesList.SelectedItem = _config.Repositories.FirstOrDefault(r => r.Id == repoId);
    }

    private void AccountsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountsList.SelectedItem is not AccountConfig account) return;
        _draftAccount = null;
        ShowAccount(account);
    }
    private void ShowAccount(AccountConfig account)
    {
        AccountPlatform.Text = account.Platform.ToString();
        AccountName.Text = account.DisplayName;
        ClientId.Text = account.ClientId;
        ClientSecret.Password = "";
        CallbackPort.Text = account.CallbackPort.ToString();
        ShowAuthorizationState(account);
    }
    private void ShowAuthorizationState(AccountConfig account)
    {
        var state = AuthorizationViewState.Read(account, _services.Credentials);
        AccountStatus.Text = state.Status;
        AuthorizeButton.Content = state.ButtonText;
    }
    private void NewGithub_Click(object sender, RoutedEventArgs e) => NewAccount(Platform.GitHub);
    private void NewGitee_Click(object sender, RoutedEventArgs e) => NewAccount(Platform.Gitee);
    private void NewAccount(Platform platform)
    {
        AccountsList.SelectedItem = null;
        _draftAccount = new AccountConfig { Platform = platform, DisplayName = platform == Platform.GitHub ? "我的 GitHub" : "我的 Gitee" };
        ShowAccount(_draftAccount);
    }

    private async void Authorize_Click(object sender, RoutedEventArgs e)
    {
        var account = SelectedAccount;
        if (account == null) { AccountStatus.Text = "先新建或选择账号。"; return; }
        var clientId = ClientId.Text.Trim();
        if (clientId.Length == 0) { AccountStatus.Text = "请填写 Client ID。"; return; }
        if (!int.TryParse(CallbackPort.Text, out var port) || port is < 1024 or > 65535) { AccountStatus.Text = "回调端口无效。"; return; }
        SetAuthBusy(true);
        _authCancel = new CancellationTokenSource();
        try
        {
            TokenResult result;
            string? clientSecret = null;
            if (account.Platform == Platform.GitHub)
            {
                var challenge = await _services.OAuth.StartGitHubAsync(clientId, _authCancel.Token);
                OAuthService.OpenBrowser(challenge.VerificationUri);
                Clipboard.SetText(challenge.UserCode);
                MessageBox.Show(this, $"浏览器中输入代码：{challenge.UserCode}\n\n代码已复制到剪贴板。授权完成后点击确定，客户端会继续等待结果。", "GitHub 授权");
                result = await _services.OAuth.FinishGitHubAsync(clientId, challenge, _authCancel.Token);
            }
            else
            {
                clientSecret = ClientSecret.Password.Length > 0 ? ClientSecret.Password : _services.Credentials.Read(account.Id, "client-secret");
                if (string.IsNullOrEmpty(clientSecret)) throw new InvalidOperationException("请填写 Gitee Client Secret。");
                MessageBox.Show(this, $"请确认 Gitee 应用的回调地址为：\nhttp://127.0.0.1:{port}/callback\n\n点击确定后将在浏览器中授权。", "Gitee 授权");
                result = await _services.OAuth.AuthorizeGiteeAsync(clientId, clientSecret, port, _authCancel.Token);
            }
            await _services.Accounts.CompleteAuthorizationAsync(_config, account, clientId,
                AccountName.Text, port, result, clientSecret, _authCancel.Token);
            _draftAccount = null;
            RefreshLists();
            AccountsList.SelectedItem = account;
            ShowAuthorizationState(account);
        }
        catch (OperationCanceledException) { AccountStatus.Text = "授权已取消。"; }
        catch (Exception ex) { AccountStatus.Text = "授权失败：" + ex.Message; }
        finally
        {
            _authCancel.Dispose(); _authCancel = null;
            SetAuthBusy(false);
        }
    }

    private void CancelAuth_Click(object sender, RoutedEventArgs e) => _authCancel?.Cancel();

    private void SetAuthBusy(bool busy)
    {
        AuthorizeButton.IsEnabled = !busy;
        CancelAuthButton.IsEnabled = busy;
        AccountsList.IsEnabled = !busy;
        NewGithubButton.IsEnabled = !busy;
        NewGiteeButton.IsEnabled = !busy;
        AccountName.IsEnabled = !busy;
        ClientId.IsEnabled = !busy;
        ClientSecret.IsEnabled = !busy;
        CallbackPort.IsEnabled = !busy;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_authCancel != null)
        {
            _authCancel.Cancel();
            e.Cancel = true;
            AccountStatus.Text = "正在取消授权，请稍后关闭窗口。";
        }
        base.OnClosing(e);
    }

    private void SaveAccountName_Click(object sender, RoutedEventArgs e)
    {
        var account = SelectedAccount;
        if (account == null || !_config.Accounts.Contains(account)) return;
        _services.Accounts.Rename(_config, account, AccountName.Text);
        RefreshLists();
    }
    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        var account = SelectedAccount;
        if (account == null || !_config.Accounts.Contains(account)) return;
        if (MessageBox.Show(this, "清除该账号在本机保存的授权？已上传文件不会删除。", "断开授权", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _services.Accounts.Disconnect(_config, account);
        ShowAuthorizationState(account);
    }
    private void GithubApps_Click(object sender, RoutedEventArgs e) => OAuthService.OpenBrowser("https://github.com/settings/developers");
    private void GiteeApps_Click(object sender, RoutedEventArgs e) => OAuthService.OpenBrowser("https://gitee.com/oauth/applications");

    private void RepositoriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RepositoriesList.SelectedItem is not RepositoryConfig repo) return;
        _draftRepo = null;
        RepoAccount.SelectedItem = _config.Accounts.FirstOrDefault(a => a.Id == repo.AccountId);
        RepoOwner.Text = repo.Owner; RepoName.Text = repo.Name; RepoBranch.Text = repo.Branch; RepoDisplayName.Text = repo.DisplayName;
        RepoStatus.Text = repo.Id == _config.DefaultRepositoryId ? "当前默认仓库" : "";
    }
    private void NewRepo_Click(object sender, RoutedEventArgs e)
    {
        RepositoriesList.SelectedItem = null;
        _draftRepo = new RepositoryConfig();
        RepoAccount.SelectedItem = _config.Accounts.FirstOrDefault();
        RepoOwner.Text = ""; RepoName.Text = ""; RepoBranch.Text = "main"; RepoDisplayName.Text = "";
        RepoStatus.Text = "填写已有的私有仓库。";
    }
    private async void SaveRepo_Click(object sender, RoutedEventArgs e)
    {
        var repo = SelectedRepo;
        var account = RepoAccount.SelectedItem as AccountConfig;
        if (repo == null || account == null) { RepoStatus.Text = "先选择账号并新建仓库配置。"; return; }
        var owner = RepoOwner.Text.Trim(); var name = RepoName.Text.Trim(); var branch = RepoBranch.Text.Trim();
        if (new[] { owner, name, branch }.Any(string.IsNullOrWhiteSpace) || owner.Contains('/') || name.Contains('/') || branch.Contains(' '))
        { RepoStatus.Text = "请填写有效的拥有者、仓库名和分支。"; return; }
        var candidate = new RepositoryConfig { AccountId = account.Id, Owner = owner, Name = name, Branch = branch, DisplayName = RepoDisplayName.Text.Trim() };
        IsEnabled = false;
        try
        {
            await _services.Repositories.SaveAsync(_config, repo, account, candidate);
            _draftRepo = null;
            RefreshLists(); RepositoriesList.SelectedItem = repo;
            RepoStatus.Text = "验证成功，已保存。";
        }
        catch (Exception ex) { RepoStatus.Text = "验证失败：" + ex.Message; }
        finally { IsEnabled = true; }
    }
    private void SetDefault_Click(object sender, RoutedEventArgs e)
    {
        var repo = SelectedRepo;
        if (repo == null || !_config.Repositories.Contains(repo)) return;
        _services.Repositories.SetDefault(_config, repo);
        RepoStatus.Text = "当前默认仓库";
    }
    private void RemoveRepo_Click(object sender, RoutedEventArgs e)
    {
        var repo = SelectedRepo;
        if (repo == null || !_config.Repositories.Contains(repo)) return;
        if (MessageBox.Show(this, "删除本地仓库配置？远端文件不会被删除。", "删除配置", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _services.Repositories.Remove(_config, repo);
        RefreshLists();
        RepoStatus.Text = "配置已删除。";
    }
}
