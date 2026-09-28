using System.Windows;
using System.Windows.Controls;

namespace RepoTransit;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly ConfigStore _store;
    private readonly CredentialStore _secrets;
    private readonly OAuthService _oauth;
    private readonly PlatformApi _api;
    private AccountConfig? _draftAccount;
    private RepositoryConfig? _draftRepo;
    private CancellationTokenSource? _authCancel;

    public SettingsWindow(AppConfig config, ConfigStore store, CredentialStore secrets, OAuthService oauth, PlatformApi api)
    {
        InitializeComponent();
        _config = config; _store = store; _secrets = secrets; _oauth = oauth; _api = api;
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
        AccountStatus.Text = _secrets.Read(account.Id, "access") == null ? "待授权" : $"已授权：{account.Login}";
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
            if (account.Platform == Platform.GitHub)
            {
                var challenge = await _oauth.StartGitHubAsync(clientId, _authCancel.Token);
                OAuthService.OpenBrowser(challenge.VerificationUri);
                Clipboard.SetText(challenge.UserCode);
                MessageBox.Show(this, $"浏览器中输入代码：{challenge.UserCode}\n\n代码已复制到剪贴板。授权完成后点击确定，客户端会继续等待结果。", "GitHub 授权");
                result = await _oauth.FinishGitHubAsync(clientId, challenge, _authCancel.Token);
            }
            else
            {
                var secret = ClientSecret.Password.Length > 0 ? ClientSecret.Password : _secrets.Read(account.Id, "client-secret");
                if (string.IsNullOrEmpty(secret)) throw new InvalidOperationException("请填写 Gitee Client Secret。");
                MessageBox.Show(this, $"请确认 Gitee 应用的回调地址为：\nhttp://127.0.0.1:{port}/callback\n\n点击确定后将在浏览器中授权。", "Gitee 授权");
                result = await _oauth.AuthorizeGiteeAsync(clientId, secret, port, _authCancel.Token);
                _secrets.Save(account.Id, "client-secret", secret);
            }
            var login = await _api.GetLoginAsync(account.Platform, result.AccessToken, _authCancel.Token);
            _secrets.Save(account.Id, "access", result.AccessToken);
            if (result.RefreshToken != null) _secrets.Save(account.Id, "refresh", result.RefreshToken);
            account.ClientId = clientId; account.CallbackPort = port; account.Login = login;
            account.DisplayName = string.IsNullOrWhiteSpace(AccountName.Text) ? login : AccountName.Text.Trim();
            account.ExpiresAt = result.ExpiresAt;
            if (!_config.Accounts.Contains(account)) _config.Accounts.Add(account);
            _store.Save(_config);
            _draftAccount = null;
            RefreshLists();
            AccountsList.SelectedItem = account;
            AccountStatus.Text = $"已授权：{login}";
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
        account.DisplayName = AccountName.Text.Trim();
        _store.Save(_config);
        RefreshLists();
    }
    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        var account = SelectedAccount;
        if (account == null || !_config.Accounts.Contains(account)) return;
        if (MessageBox.Show(this, "清除该账号在本机保存的授权？已上传文件不会删除。", "断开授权", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        foreach (var key in new[] { "access", "refresh", "client-secret" }) _secrets.Delete(account.Id, key);
        account.ExpiresAt = null;
        _store.Save(_config);
        AccountStatus.Text = "待授权";
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
            var coordinator = new UploadCoordinator(_api, _oauth, _secrets, _store);
            var token = await coordinator.GetTokenAsync(_config, account);
            await _api.ValidateRepositoryAsync(account, token, candidate);
            repo.AccountId = candidate.AccountId; repo.Owner = owner; repo.Name = name; repo.Branch = branch; repo.DisplayName = candidate.DisplayName;
            if (!_config.Repositories.Contains(repo)) _config.Repositories.Add(repo);
            if (_config.DefaultRepositoryId == null) _config.DefaultRepositoryId = repo.Id;
            _store.Save(_config);
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
        _config.DefaultRepositoryId = repo.Id;
        _store.Save(_config);
        RepoStatus.Text = "当前默认仓库";
    }
    private void RemoveRepo_Click(object sender, RoutedEventArgs e)
    {
        var repo = SelectedRepo;
        if (repo == null || !_config.Repositories.Contains(repo)) return;
        if (MessageBox.Show(this, "删除本地仓库配置？远端文件不会被删除。", "删除配置", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _config.Repositories.Remove(repo);
        if (_config.DefaultRepositoryId == repo.Id) _config.DefaultRepositoryId = _config.Repositories.FirstOrDefault()?.Id;
        _store.Save(_config);
        RefreshLists();
        RepoStatus.Text = "配置已删除。";
    }
}
