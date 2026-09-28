namespace RepoTransit;

public static class PathRules
{
    public static string Build(string originalName, DateTimeOffset localTime, int suffix = 1)
    {
        if (string.IsNullOrWhiteSpace(originalName) || originalName != Path.GetFileName(originalName))
            throw new ArgumentException("必须提供有效的文件名。", nameof(originalName));
        if (suffix < 1) throw new ArgumentOutOfRangeException(nameof(suffix));
        var extension = Path.GetExtension(originalName);
        var stem = originalName[..^extension.Length];
        var extra = suffix == 1 ? "" : $"_{suffix}";
        return $"{localTime:yyyy-MM}/{stem}_{localTime:yyyyMMdd'T'HHmmssfff}{extra}{extension}";
    }

    public static string EncodePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
