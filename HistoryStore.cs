using StackExchange.Redis;

namespace Shist;

internal static class Config
{
    public const int MaxEntries = 10_000;
    public const int PageSize = 50;
    public const int ScanBatch = 500;
    public const int Prefetch = 5;
}

internal sealed record Entry(long At, string Command, string? Dir)
{
    public string Serialize() =>
        $"{At}\t{Dir ?? ""}\t{Command.Replace("\t", " ").Replace("\n", " ")}";

    public static Entry? Parse(string raw)
    {
        var p = raw.Split('\t', 3);
        return p.Length == 3 && long.TryParse(p[0], out var at)
            ? new(at, p[2], string.IsNullOrEmpty(p[1]) ? null : p[1])
            : null;
    }
}

internal sealed class HistoryStore(ConnectionMultiplexer mux) : IAsyncDisposable
{
    private const string Entries = "shist:entries";
    private const string Last = "shist:last";

    public static async Task<HistoryStore> OpenAsync(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.ConnectTimeout = options.SyncTimeout = 2000;
        return new(await ConnectionMultiplexer.ConnectAsync(options));
    }

    public async Task Record(string command, string? dir)
    {
        command = command.Trim();
        if (command.Length == 0)
            return;

        if ((await mux.GetDatabase().StringGetAsync(Last)).ToString() == command)
            return;

        var db = mux.GetDatabase();
        await db.ListLeftPushAsync(Entries, new Entry(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), command, dir).Serialize());
        await db.ListTrimAsync(Entries, 0, Config.MaxEntries - 1);
        await db.StringSetAsync(Last, command);
    }

    public async Task<(List<Entry> Items, int Next, bool More)> Fetch(string? filter, int offset, int count)
    {
        var db = mux.GetDatabase();
        var total = await db.ListLengthAsync(Entries);
        if (count <= 0 || offset >= total)
            return ([], offset, false);

        filter = filter?.Trim();
        if (string.IsNullOrEmpty(filter))
        {
            var end = (int)Math.Min(offset + (long)count - 1, total - 1);
            var items = Parse(await db.ListRangeAsync(Entries, offset, end));
            var next = end + 1;
            return (items, next, next < total);
        }

        var matches = new List<Entry>(count);
        var scan = offset;
        while (matches.Count < count && scan < total)
        {
            var end = (int)Math.Min(scan + Config.ScanBatch - 1, total - 1);
            foreach (var entry in Parse(await db.ListRangeAsync(Entries, scan, end)))
            {
                if (entry.Command.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    matches.Add(entry);
                if (matches.Count == count)
                    break;
            }

            scan = end + 1;
        }

        return (matches, scan, scan < total);
    }

    private static List<Entry> Parse(RedisValue[] values) =>
        values.Select(v => Entry.Parse(v.ToString())).Where(e => e is not null).Cast<Entry>().ToList();

    public ValueTask DisposeAsync()
    {
        mux.Dispose();
        return default;
    }
}