namespace RepoTransit;

// Composition root shared by the two WPF windows. No service container is needed for this app.
public sealed class AppServices
{
    public ConfigStore Config { get; } = new();
    public CredentialStore Credentials { get; } = new();
    public OAuthService OAuth { get; } = new();
    public RepositoryClientFactory Clients { get; } = new();
    public TokenManager Tokens { get; }
    public UploadCoordinator Uploads { get; }
    public AccountManager Accounts { get; }
    public RepositoryManager Repositories { get; }

    public AppServices()
    {
        Tokens = new TokenManager(OAuth, Credentials, Config);
        Uploads = new UploadCoordinator(Clients, Tokens);
        Accounts = new AccountManager(Clients, Credentials, Config);
        Repositories = new RepositoryManager(Clients, Tokens, Config);
    }
}
