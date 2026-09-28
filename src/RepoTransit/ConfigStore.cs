using System.Text.Json;

namespace RepoTransit;

public sealed class ConfigStore
{
    private readonly string _file;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public ConfigStore(string? file = null) => _file = file ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RepoTransit", "config.json");

    public AppConfig Load()
    {
        if (!File.Exists(_file)) return new AppConfig();
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_file), JsonOptions) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(temp, _file, true);
    }
}
