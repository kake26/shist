# shist

Shell history (shist) backed by Valkey. A minimal alternative to Atuin — record commands, search, and scroll through history without a local SQLite database. 

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (to build)
- [Valkey](https://valkey.io/) or Redis server
- Bash

## Build

I recommend AOT compilation both for speed and simplicity of use. The line below give you a AOT compiled binary that is dumped into the dist folder. 

```bash
dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishAot=true -o ./dist/x64
```

The binary lands at `bin/Release/net10.0/shist`. Add it to your `PATH`, or reference the full path in your shell config.

## Bash setup

Point every machine at your shared Valkey instance (e.g. a Pi on your LAN), then enable bash integration.

**Option A — config file** (recommended; background `record` picks this up automatically):

```bash
mkdir -p ~/.config/shist
echo 'valkey=pi.local:6379' > ~/.config/shist/config
```

**Option B — environment variable** in `~/.bashrc`:

```bash
export SHIST_VALKEY="pi.local:6379"
```

Then add shist to your path and load the bash hook:

```bash
export PATH="$PATH:/path/to/shist/dist/x64"
eval "$(shist init bash)"
```

Then reload:

```bash
source ~/.bashrc
```

After setup:

- Every command is recorded to Valkey in the background
- **Ctrl+R** opens the interactive history picker (replaces default reverse-search)

## Commands

| Command | Description |
|---|---|
| `shist` | Interactive picker (recent history) |
| `shist pick [query]` | Picker with optional initial filter |
| `shist list [-n N]` | List recent commands (default: 50) |
| `shist search <query> [-n N]` | Search history (default: 50 results) |
| `shist record -- <cmd>` | Manually store a command |
| `shist init bash` | Print bash integration snippet |

### Picker keys

- **↑ / ↓** — scroll
- **Type** — filter results
- **Enter** — run the selected command immediately
- **Tab** — put the command on the line without running (edit first)

After upgrading, refresh the bash hook: `eval "$(shist init bash)"`
- **Esc** — cancel

## What gets recorded

Each entry stores three fields:

| Field | Description |
|---|---|
| Command | The bash command line |
| Timestamp | UTC time when the command finished |
| Working directory | Shell cwd at record time |

Stored in Valkey as tab-separated values: `timestamp`, `cwd`, `command`.

Duplicate commands in a row are skipped. The picker shows commands only; `shist list` also shows date and time.

## Limits

- **10,000 entries** max — oldest entries are pruned automatically on each record
- Picker loads **50 entries** at a time; scrolling near the bottom fetches more

## Valkey connection

The server does not need to be local. Use a shared instance so every computer sees the same history.

**Priority** (first match wins):

1. CLI flag: `shist --valkey pi.local:6379 list`
2. Environment: `SHIST_VALKEY` or `SHIST_REDIS`
3. Config file: `~/.config/shist/config`
4. Alternate config: `~/.shist`
5. Default: `localhost:6379`

Config file format:

```ini
# ~/.config/shist/config
valkey=192.168.1.50:6379
```

With authentication or TLS:

```ini
valkey=pi.local:6379,password=secret,ssl=true
```

One-off override:

```bash
shist -H pi.local:6379 search dotnet
```

## Environment variables

| Variable | Default | Description |
|---|---|---|
| `SHIST_VALKEY` | — | Valkey/Redis connection string |
| `SHIST_REDIS` | — | Alias for `SHIST_VALKEY` |
| `SHIST_CONFIG` | — | Path to a custom config file |
| (default) | `localhost:6379` | Used when nothing else is set |