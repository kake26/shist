using System.Diagnostics;
using Shist;

const int PickRun = 2;
const int PickCancel = 1;

const string BashInit = """
# shist bash integration
_shist_bin="$(command -v shist)"
[[ -z "$_shist_bin" ]] && { echo "shist: command not found" >&2; return 1; }
_shist_last=""
_shist_record() {
  local last
  last="$(HISTTIMEFORMAT= builtin fc -ln -1 2>/dev/null | sed 's/^[[:space:]]*//')"
  [[ -z "$last" || "$last" == "$_shist_last" ]] && return 0
  _shist_last="$last"
  ( set +m; "$_shist_bin" record -- "$last" >/dev/null 2>&1 & )
}
_shist_pick() {
  local cmd status
  cmd="$("$_shist_bin" pick "$@" < /dev/tty)"
  status=$?
  (( status == 1 )) && return 1
  [[ -z "$cmd" ]] && return 1
  if (( status == 2 )); then
    history -s "$cmd"
    eval "$cmd" >/dev/tty 2>&1
  else
    READLINE_LINE=$cmd
    READLINE_POINT=${#cmd}
  fi
}
PROMPT_COMMAND="_shist_record${PROMPT_COMMAND:+; $PROMPT_COMMAND}"
bind -x '"\C-r": "_shist_pick"'
""";

try
{
    var (valkey, cmdArgs) = ParseLeadingFlags(args);
    if (cmdArgs.Length == 0)
    {
        await using var store = await HistoryStore.OpenAsync(Settings.Resolve(valkey));
        return await Pick(store, null);
    }

    return cmdArgs[0].ToLowerInvariant() switch
    {
        "init" => Init(cmdArgs.ElementAtOrDefault(1)),
        _ => await RunWithStore(valkey, cmdArgs),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"shist: {ex.Message}");
    return 1;
}

static (string? Valkey, string[] CmdArgs) ParseLeadingFlags(string[] args)
{
    string? valkey = null;
    var rest = new List<string>(args.Length);

    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--valkey="))
        {
            valkey = args[i]["--valkey=".Length..];
            if (string.IsNullOrWhiteSpace(valkey))
                throw new InvalidOperationException("--valkey requires a connection string");
            continue;
        }
        if (args[i] == "--valkey")
        {
            if (i + 1 >= args.Length)
                throw new InvalidOperationException("--valkey requires a connection string");
            valkey = args[++i];
            continue;
        }
        if (args[i].StartsWith("-H") && args[i].Length > 2)
        {
            valkey = args[i][2..];
            if (string.IsNullOrWhiteSpace(valkey))
                throw new InvalidOperationException("-H requires a connection string");
            continue;
        }
        if (args[i] == "-H")
        {
            if (i + 1 >= args.Length)
                throw new InvalidOperationException("-H requires a connection string");
            valkey = args[++i];
            continue;
        }

        rest.Add(args[i]);
    }

    return (valkey, rest.ToArray());
}

static async Task<int> RunWithStore(string? valkey, string[] rest)
{
    await using var store = await HistoryStore.OpenAsync(Settings.Resolve(valkey));

    return rest[0].ToLowerInvariant() switch
    {
        "record" => await Record(store, Tail(rest)),
        "list" => await List(store, rest[1..]),
        "search" => await Search(store, rest[1..]),
        "pick" => await Pick(store, Query(rest[1..])),
        _ => Usage(),
    };
}

static string Tail(string[] args) =>
    args.Length > 1 && args[1] == "--" ? string.Join(' ', args[2..]) : string.Join(' ', args[1..]);

static string? Query(string[] args)
{
    var positional = new List<string>();
    var afterDoubleDash = false;
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--")
        {
            afterDoubleDash = true;
            continue;
        }
        if (!afterDoubleDash && (args[i] is "-n" or "--limit") && i + 1 < args.Length)
        {
            i++;
            continue;
        }
        if (!afterDoubleDash && args[i].StartsWith('-'))
            continue;
        positional.Add(args[i]);
    }
    return positional.Count > 0 ? string.Join(' ', positional) : null;
}

static int Limit(string[] args, int fallback = Config.PageSize)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] is "-n" or "--limit" && int.TryParse(args[i + 1], out var n) && n > 0)
            return n;
    return fallback;
}

static async Task<int> Record(HistoryStore store, string command)
{
    await store.Record(command, Directory.GetCurrentDirectory());
    return 0;
}

static async Task<int> List(HistoryStore store, string[] args)
{
    Print((await store.Fetch(null, 0, Limit(args))).Items);
    return 0;
}

static async Task<int> Search(HistoryStore store, string[] args)
{
    var query = Query(args);
    if (string.IsNullOrWhiteSpace(query))
        throw new InvalidOperationException("search requires a query");
    Print((await store.Fetch(query, 0, Limit(args))).Items);
    return 0;
}

static void Print(List<Entry> entries)
{
    if (entries.Count == 0)
    {
        Console.Error.WriteLine("No matching history entries.");
        return;
    }

    for (var i = 0; i < entries.Count; i++)
    {
        var when = DateTimeOffset.FromUnixTimeSeconds(entries[i].At).ToLocalTime();
        Console.WriteLine($"{Ansi.Dim}{i + 1,4}  {when:yyyy-MM-dd HH:mm:ss}{Ansi.R}  {entries[i].Command}");
    }
}

static async Task<int> Pick(HistoryStore store, string? query = null)
{
    var ui = Console.IsOutputRedirected ? Console.Error : Console.Out;
    var filter = query ?? "";
    var entries = new List<Entry>();
    var scanAt = 0;
    var hasMore = true;
    var index = 0;

    async Task ReloadAsync()
    {
        entries.Clear();
        scanAt = 0;
        hasMore = true;
        await LoadMoreAsync();
        index = 0;
    }

    async Task LoadMoreAsync()
    {
        if (!hasMore)
            return;

        var page = await store.Fetch(string.IsNullOrWhiteSpace(filter) ? null : filter, scanAt, Config.PageSize);
        entries.AddRange(page.Items);
        scanAt = page.Next;
        hasMore = page.More;
    }

    await ReloadAsync();
    if (entries.Count == 0)
    {
        ui.WriteLine($"{Ansi.Yellow}No matching history entries.{Ansi.R}");
        return PickCancel;
    }

    void Draw()
    {
        const int window = 12;
        if (ReferenceEquals(ui, Console.Out))
            Console.Clear();
        else
            ui.Write("\e[2J\e[H");

        ui.WriteLine(
            $"{Ansi.Bold}{Ansi.Cyan}shist{Ansi.R}  {Ansi.Dim}(↑/↓ scroll, type to search, " +
            $"{Ansi.Green}Enter{Ansi.Dim} run, {Ansi.Yellow}Tab{Ansi.Dim} edit, Esc cancel){Ansi.R}");
        ui.WriteLine(
            $"{Ansi.Magenta}filter:{Ansi.R} {(string.IsNullOrEmpty(filter) ? $"{Ansi.Dim}…{Ansi.R}" : $"{Ansi.Bold}{filter}{Ansi.R}")}");
        ui.WriteLine($"{Ansi.Dim}{Ansi.Blue}{new string('─', 72)}{Ansi.R}");

        if (entries.Count == 0)
        {
            ui.WriteLine($"{Ansi.Yellow}  (no matches){Ansi.R}");
            return;
        }

        index = Math.Clamp(index, 0, entries.Count - 1);
        var start = Math.Max(0, Math.Min(index - window / 2, entries.Count - window));
        for (var i = start; i < Math.Min(entries.Count, start + window); i++)
        {
            var line = entries[i].Command;
            if (line.Length > 68)
                line = line[..65] + "...";

            ui.WriteLine(i == index
                ? $"{Ansi.Bold}{Ansi.Green}> {Ansi.Cyan}{line}{Ansi.R}"
                : $"{Ansi.Dim}  {line}{Ansi.R}");
        }

        if (hasMore)
            ui.WriteLine($"{Ansi.Dim}{Ansi.Yellow}  ↓ scroll for more{Ansi.R}");
    }

    int Finish(string cmd, bool run)
    {
        ui.WriteLine();

        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(cmd);
            return run ? PickRun : 0;
        }

        if (run)
        {
            var bash = new ProcessStartInfo("/bin/bash") { UseShellExecute = false };
            bash.ArgumentList.Add("-c");
            bash.ArgumentList.Add(cmd);
            using var proc = Process.Start(bash);
            proc?.WaitForExit();
            return 0;
        }

        Console.WriteLine(cmd);
        return 0;
    }

    Draw();
    Console.CursorVisible = false;
    try
    {
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow when entries.Count > 0:
                    index = Math.Max(0, index - 1);
                    Draw();
                    break;
                case ConsoleKey.DownArrow when entries.Count > 0:
                    if (hasMore && index >= entries.Count - Config.Prefetch)
                        await LoadMoreAsync();
                    index = Math.Min(entries.Count - 1, index + 1);
                    Draw();
                    break;
                case ConsoleKey.Enter when entries.Count > 0:
                    return Finish(entries[index].Command, run: true);
                case ConsoleKey.Tab when entries.Count > 0:
                    return Finish(entries[index].Command, run: false);
                case ConsoleKey.Escape:
                    ui.WriteLine();
                    return PickCancel;
                case ConsoleKey.Backspace when filter.Length > 0:
                    filter = filter[..^1];
                    await ReloadAsync();
                    Draw();
                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        filter += key.KeyChar;
                        await ReloadAsync();
                        Draw();
                    }
                    break;
            }
        }
    }
    finally
    {
        Console.CursorVisible = true;
        if (Console.IsOutputRedirected)
            Console.Error.Write("\e[2J\e[H");
    }
}

static int Init(string? shell)
{
    if (!string.Equals(shell, "bash", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("usage: shist init bash");
    Console.WriteLine(BashInit);
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("""
usage:
  shist [--valkey HOST]         interactive picker
  shist record [--] <command>   store a command
  shist list [-n N]             list recent commands
  shist search <query> [-n N]   search history
  shist pick [query]            interactive picker
  shist init bash               print bash integration snippet

valkey (first match wins):
  --valkey, -H                  CLI flag
  SHIST_VALKEY / SHIST_REDIS    environment variable
  ~/.config/shist/config        config file (valkey=HOST:PORT)
  ~/.shist                      alternate config file
  localhost:6379                default

limits: 10,000 entries (auto-pruned), picker loads 50 at a time
""");
    return 1;
}