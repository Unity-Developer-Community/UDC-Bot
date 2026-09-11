---
post_title: "Local Development and Debugging"
author1: "UDC-Bot Contributors"
post_slug: "development"
microsoft_alias: "N/A"
featured_image: ""
categories: []
tags: ["development", "debugging", "vscode", "docker"]
summary: "Build, run, test, and debug UDC-Bot on Linux, macOS, and Windows."
post_date: "2026-08-29"
---

# Local development and debugging

This is the canonical local-development guide. Unless a section says otherwise, commands are the same on Linux, macOS, Windows PowerShell, and WSL and should be run from the repository root.

## Prerequisites

Required:

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), or a newer SDK capable of targeting .NET 8 with the .NET 8 runtime installed;
- Git;
- a development Discord application and guild;
- PostgreSQL 16 or Docker with the current `docker compose` command.

Recommended editor setup:

- [Visual Studio Code](https://code.visualstudio.com/);
- the workspace-recommended Microsoft C# Dev Kit extension.

Confirm the CLI and repository are visible from the same environment:

```text
dotnet --info
dotnet sln DiscordBot.sln list
```

### Platform notes

- **Linux:** No additional editor integration is required. This is closest to the production container environment.
- **macOS:** The managed application builds normally, but profile rendering currently uses an x64-specific Magick.NET package. Apple Silicon needs a deliberate x64/Rosetta toolchain or a future native-package change.
- **Windows (native):** Use an x64 .NET SDK. Docker Desktop can provide the local PostgreSQL container.
- **Windows (WSL):** Install the VS Code WSL extension, open the repository in a `WSL: <distribution>` window, and install the .NET SDK and C# Dev Kit inside that WSL environment. A Windows SDK installation is not visible to Linux processes in WSL.
- **ARM Linux:** The current `Magick.NET-Q8-x64` dependency and production image are x64-only.

For reliable file watching, keep the checkout on the native filesystem of the active environment. For example, a WSL workflow should prefer a path under the Linux home directory instead of `/mnt/c`.

## First-time setup

### 1. Restore and build

From the repository root:

```text
dotnet restore DiscordBot.sln
dotnet build DiscordBot.sln
dotnet test DiscordBot.sln
```

The existing `NU1701` warning for `Pathoschild.NaturalTimeParser` is known: that legacy package only supplies .NET Framework assets. Treat new errors or warnings separately from that baseline.

### 2. Create local settings

Copy the two configuration examples. Their non-example filenames are gitignored.

On Linux, macOS, Git Bash, or WSL:

```text
cp DiscordBot/Settings/CoreSettings.example.json DiscordBot/Settings/CoreSettings.json
cp DiscordBot/Settings/FeatureSettings.example.json DiscordBot/Settings/FeatureSettings.json
```

On Windows PowerShell:

```powershell
Copy-Item DiscordBot/Settings/CoreSettings.example.json DiscordBot/Settings/CoreSettings.json
Copy-Item DiscordBot/Settings/FeatureSettings.example.json DiscordBot/Settings/FeatureSettings.json
```

At minimum, review these values:

- `DiscordConnection:Token` and `Database:ConnectionString` in `CoreSettings.json`;
- `DiscordGuild:GuildId` in `CoreSettings.json`;
- core channel and role IDs in `CoreSettings.json`;
- channel and role IDs used by the features you intend to exercise.

Weather and airport credentials sit beside their feature settings under `Weather` and `Airport` in `FeatureSettings.json`. Never put tokens, API keys, or real connection strings in `.vscode`, `launchSettings.json`, documentation, test fixtures, or the tracked `.example.json` templates.

The old flat `Settings.json` remains a read-only compatibility source. Startup reports that the legacy file is active and which modular files or values are missing; no settings file is generated, migrated, or repaired automatically.

### 3. Start PostgreSQL

The quickest local database is the Compose `db` service:

```text
docker compose up --detach db
docker compose ps
```

The checked-in development values are:

```text
Host=localhost;Port=5432;Database=udcbot;Username=udcbot;Password=123456789
```

`localhost` is correct when the bot runs directly on the host under the CLI or debugger. When the bot itself runs in Compose, use `Host=db` because containers address each other by service name.

The optional Adminer service is available with:

```text
docker compose up --detach adminer
```

Then open `http://localhost:8080` and use `db` as the server name.

## Command-line workflow

The project defines its development run directory as `DiscordBot/`, so the following command can be invoked consistently from the repository root:

```text
dotnet run --project DiscordBot/DiscordBot.csproj
```

For automatic rebuild/restart while editing:

```text
dotnet watch --project DiscordBot/DiscordBot.csproj run
```

Stop either process with Ctrl+C.

Running the compiled DLL directly does not use project launch metadata. If that is necessary, first make `DiscordBot/` the current directory so `Settings/`, `Assets/`, and `SERVER/` resolve correctly:

```text
cd DiscordBot
dotnet bin/Debug/net8.0/DiscordBot.dll
```

Prefer `dotnet run` for normal development because it does not require a target-framework-specific output path.

## Visual Studio Code tasks

Open **Terminal → Run Task** or run **Tasks: Run Task** from the command palette.

| Task | Purpose |
| --- | --- |
| `dotnet: restore solution` | Restore both projects. |
| `dotnet: build solution` | Build the application and tests; this is the default build task. |
| `dotnet: run bot` | Run the bot with the correct project working directory. |
| `dotnet: watch bot` | Rebuild or hot reload/restart after source changes. |
| `dotnet: test solution` | Run the MSTest project; this is the default test task. |
| `dotnet: publish bot` | Produce a Release publish using the current target framework. |
| `docker: start PostgreSQL` | Start the local database in the background. |

The tasks intentionally do not copy settings, insert credentials, or automatically start/stop containers when F5 is pressed.

## Launch under the debugger

1. Open the repository root in VS Code.
2. Install the recommended C# Dev Kit extension in the same environment that runs `dotnet`.
3. Open **Run and Debug**.
4. Select `C#: Debug DiscordBot`.
5. Put a breakpoint in `DiscordBot/Program.cs` or a command/service you intend to exercise.
6. Press F5.

The launch entry points at `DiscordBot.csproj`, not a DLL under `bin/Debug/<framework>`. C# Dev Kit determines the current build output, and the project supplies the working directory. A future target-framework change therefore does not require another launch-path edit.

F5 starts a real Discord gateway client. Use a development bot and guild unless you deliberately intend to test against another environment.

## Attach to a running process

Use attach when the bot was started through `dotnet: run bot`, `dotnet: watch bot`, or a terminal:

1. Start the bot and leave it running.
2. Open **Run and Debug**.
3. Select `C#: Attach to .NET process`.
4. Press F5 and choose the process associated with `DiscordBot`.

The process picker can only see processes in the VS Code extension host's environment:

- native Linux/macOS/Windows VS Code sees processes on that operating system;
- a WSL-connected window sees processes inside its WSL distribution;
- it does not cross automatically into Docker containers, SSH hosts, or another WSL distribution.

`dotnet watch` may replace its child process after a source change. If the attached process exits, run attach again and choose the new process.

Container attachment is not part of the primary local workflow. Run PostgreSQL in Compose and the bot directly under the debugger for the shortest edit/debug cycle.

## Exception logging

The debugger's `Exception thrown: 'System.FormatException' in System.Private.CoreLib.dll`
line reports a throw, even when a catch block later handles it. It does not by itself
mean the bot failed. Normal logging reports failures at the handlers wired into the
logger; temporary first-chance tracing below can also show throws handled silently
inside application code or dependencies.

Exception reports include the exception type, message, inner exceptions, and a source
location. Debug builds and Release builds with `DOTNET_ENVIRONMENT=Development` include
up to three frames per exception, preferring bot frames. Normal Release builds use a
single-line summary with one frame per exception. Discord log-channel reports always
use the compact format. Reports are capped at eight exceptions and 1,800 characters;
long messages/traces are truncated. No complete stack dump is stored by this formatter.

Example compact output:

```text
Birthday check failed | FormatException: Invalid date @ BirthdayAnnouncementService.cs:275 (GoogleSheetsBirthdaySource.TryParseBirthdayDate)
```

File/line information requires matching `.pdb` files alongside the deployed assemblies.
Keep the PDBs produced by `dotnet publish` if production line numbers are useful.
Without symbols, reports use class/method names. Async state-machine frames are rendered
as the owning method. If an exception has never been thrown, exception logging APIs use
the caller's file/line and explicitly label it `logged here`. Source paths are reduced
to filenames; exception messages themselves are not automatically redacted.

In catch blocks, pass the exception object instead of interpolating `exception.Message`
or `exception.ToString()`:

```csharp
catch (Exception exception)
{
    await _loggingService.LogException(exception, "Birthday check failed");
}
```

The default destinations are console and file. Pass `LogBehaviour.ConsoleChannelAndFile`
to include the configured log channel, and `severity:` to override Error.
`LoggingService.LogExceptionToConsole(exception, "Operation failed")` is available to
synchronous code without an injected logger. Keep expected cancellation handling ahead
of general catches. Some older handlers still use their existing string logging; migrate
them to these APIs when working in those areas.

Discord gateway/library logs retain their exception objects. Prefix and interaction
command completion events report execution exceptions, including commands dispatched
asynchronously. Host `ILogger` messages use the same console format. Existing host
logging levels still apply. File logging serializes append/rotation, creates missing
files on demand, and falls back to the console on write failure. A failed Discord log
send is reported locally and cannot prevent the file entry from being written.

### Temporarily trace caught exceptions

Set `UDCBOT_TRACE_EXCEPTION_TYPE` to the exact full exception type name before starting
the bot. This diagnostic is disabled when the variable is unset or empty.

Bash (one launch only):

```bash
UDCBOT_TRACE_EXCEPTION_TYPE=System.FormatException dotnet run --project DiscordBot/DiscordBot.csproj
```

PowerShell:

```powershell
$env:UDCBOT_TRACE_EXCEPTION_TYPE = "System.FormatException"
dotnet run --project DiscordBot/DiscordBot.csproj
Remove-Item Env:UDCBOT_TRACE_EXCEPTION_TYPE
```

For F5, set the same variable in your local debugger environment. Restart the process
after changing it. Tracing writes the first 20 matching throws to the console with
`First-chance (may be handled)` and development detail, then stops reporting. It includes
dependency exceptions and can duplicate a later catch-block report; it is a temporary
diagnostic, not an error count. Only the exact type matches (no wildcard or derived-type
matching). Restart to capture another batch. It does not change debugger break settings,
swallow exceptions, or send first-chance reports to Discord.

## Tests and rendering diagnostics

Run the normal suite from the repository root:

```text
dotnet test DiscordBot.sln --configuration Release
```

Profile rendering can be checked without a Discord token or database connection:

```text
dotnet run --project DiscordBot/DiscordBot.csproj -- \
  --render-smoke /tmp/profile-card.png Assets

dotnet run --project DiscordBot/DiscordBot.csproj -- \
  --render-stress 100 4 Assets
```

The project working-directory setting makes `Assets` resolve to `DiscordBot/Assets`. On Windows, replace the `/tmp` output path with a writable Windows path. The stress command uses default output handling and does not need an output path.

## Full Compose workflow

The normal development loop runs only PostgreSQL in Compose. To build and run the bot container too:

```text
docker compose up --build --remove-orphans
```

Before doing this:

- ensure `DiscordBot/Settings/CoreSettings.json` and `FeatureSettings.json` exist (or retain the legacy `Settings.json` during its compatibility window);
- export `UDCBOT_DiscordConnection__Token`; Compose forwards it to the bot container;
- optionally export the documented weather/airport variables and `POSTGRES_PASSWORD`;
- Compose constructs the bot's `Host=db` connection string from its PostgreSQL values;
- remember that the bot service will connect to Discord immediately;
- use `docker compose logs --follow bot` for output.

Stop the local stack with:

```text
docker compose down
```

Compose is for local development only. See the [deployment guide](deployment.md) for production operations.

## Troubleshooting

### F5 reports that no launchable target exists

- Confirm C# Dev Kit is installed and enabled in the current VS Code environment.
- Confirm the opened folder is the repository root containing `DiscordBot.sln`.
- Run `dotnet build DiscordBot.sln` in VS Code's integrated terminal and resolve project-load errors first.

### Attach does not show the bot

- Confirm the bot is still running.
- Confirm VS Code and the bot run in the same native, remote, or WSL environment.
- For `dotnet watch`, look for a newly created child after a restart.

### Settings or assets cannot be found

- Run through the checked-in task, F5 profile, or `dotnet run --project DiscordBot/DiscordBot.csproj`.
- For a directly executed DLL, make `DiscordBot/` the current directory first.
- Confirm `DiscordBot/Settings/CoreSettings.json` and `FeatureSettings.json` exist and have not been moved into `SERVER/` or `bin/`.
- Confirm `DiscordConnection:Token` and `Database:ConnectionString` are filled in within `CoreSettings.json`.

### PostgreSQL connection fails

- Run `docker compose ps` and confirm `db` is running.
- Use `Host=localhost` from the host and `Host=db` from another Compose service.
- Confirm port `5432` is not already owned by another local PostgreSQL instance.
- Check `docker compose logs db` for database startup errors.

### File changes are not detected

- Keep the checkout on the active environment's native filesystem.
- On WSL, avoid `/mnt/c` for the normal edit/watch loop.
- For an unavoidable network or virtual filesystem, set `DOTNET_USE_POLLING_FILE_WATCHER=1` before starting `dotnet watch`.

### NuGet restore cannot reach the Discord.Net feed

The repository uses the Discord.Net MyGet source from `NuGet.config`. Check network/proxy access before clearing caches. If cache cleanup is required, be aware that it removes all locally cached packages:

```text
dotnet nuget locals all --clear
dotnet restore DiscordBot.sln
```
