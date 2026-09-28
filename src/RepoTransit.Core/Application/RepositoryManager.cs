namespace RepoTransit;

public sealed class RepositoryManager(RepositoryClientFactory clients, TokenManager tokens, ConfigStore configStore)
{
    public async Task SaveAsync(AppConfig config, RepositoryConfig target, AccountConfig account, RepositoryConfig candidate, CancellationToken ct = default)
    {
        var token = await tokens.GetTokenAsync(config, account, ct);
        await clients.For(account.Platform).ValidateRepositoryAsync(token, candidate, ct);
        target.AccountId = account.Id;
        target.Owner = candidate.Owner;
        target.Name = candidate.Name;
        target.Branch = candidate.Branch;
        target.DisplayName = candidate.DisplayName;
        if (!config.Repositories.Contains(target)) config.Repositories.Add(target);
        config.DefaultRepositoryId ??= target.Id;
        configStore.Save(config);
    }

    public void SetDefault(AppConfig config, RepositoryConfig repo)
    {
        if (!config.Repositories.Contains(repo)) throw new InvalidOperationException("仓库配置不存在。");
        config.DefaultRepositoryId = repo.Id;
        configStore.Save(config);
    }

    public void Remove(AppConfig config, RepositoryConfig repo)
    {
        if (!config.Repositories.Remove(repo)) return;
        if (config.DefaultRepositoryId == repo.Id) config.DefaultRepositoryId = config.Repositories.FirstOrDefault()?.Id;
        configStore.Save(config);
    }
}
