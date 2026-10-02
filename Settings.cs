namespace Shist;

internal static class Settings
{
    public const string Default = "localhost:6379";

    public static string Resolve(string? cliOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(cliOverride))
            return cliOverride.Trim();

        var env = Environment.GetEnvironmentVariable("SHIST_VALKEY")
            ?? Environment.GetEnvironmentVariable("SHIST_REDIS");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        foreach (var path in ConfigPaths())
        {
            var value = ReadValkey(path);
            if (value is not null)
                return value;
        }

        return Default;
    }

    private static IEnumerable<string> ConfigPaths()
    {
        var custom = Environment.GetEnvironmentVariable("SHIST_CONFIG");
        if (!string.IsNullOrWhiteSpace(custom))
            yield return custom;

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
            yield return Path.Combine(xdg, "shist", "config");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "shist",
            "config");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".shist");
    }

    private static string? ReadValkey(string path)
    {
        if (!File.Exists(path))
            return null;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var eq = line.IndexOf('=');
            if (eq < 0)
                continue;

            var key = line[..eq].Trim();
            if (!key.Equals("valkey", StringComparison.OrdinalIgnoreCase)
                && !key.Equals("redis", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = line[(eq + 1)..].Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}