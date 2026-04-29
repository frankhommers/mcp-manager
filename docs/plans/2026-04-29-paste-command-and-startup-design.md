# Paste Command from Clipboard & Server Start Button

Date: 2026-04-29
Status: Design

## Goal

Make adding/configuring MCP servers faster by letting users paste a `docker run` (or any) command line and have MCP Manager fill in the server fields automatically. As a follow-up, give users a one-click "Start" for HTTP/SSE/Streamable servers whose container or process must be running before the MCP transport works.

## Problem

Today every server field (command, args, env vars, URL, transport) must be filled in by hand. Most MCP server READMEs ship a copy/paste `docker run ...` line. Users translate that line into fields manually, which is error prone — especially for HTTP servers where the container exposes a port and the MCP URL must be derived from `-p`/`--port`.

For HTTP servers there is also no way to start the container from the app. Users have to drop into a terminal.

## Scope

In:
- Parse a pasted command into `Command`, `Args[]`, `EnvironmentVariables` (stdio).
- Detect HTTP transport hints in the command (`--transport http|sse|streamable`, `--port N`, `-p HOST:CONT`) and configure as HTTP server with derived URL + the original command stored in `StartupCommand`.
- "Paste command from clipboard" button in the server detail view (overrides current selection) and next to "Add Server" (creates new server).
- "Start" button on detail view for servers that have a `StartupCommand`. Runs the command via shell, detached from MCP Manager (new session / process group) so it survives quitting the app. Fire-and-forget; no PID tracking, no Stop.

Out:
- Process supervision (status, logs, stop, restart).
- Bash substitution evaluation (`$(...)`, `${VAR}`) — kept as literal text.
- Auto-start on test/export.

## Design

### CommandLineParser (`McpManager.Core/Services/CommandLineParser.cs`)

Pure, no I/O. Returns a `ParsedCommand`:

```csharp
public record ParsedCommand(
  string Command,
  IReadOnlyList<string> Args,
  IReadOnlyDictionary<string, string> EnvironmentVariables,
  McpTransportType SuggestedTransport,
  string? SuggestedUrl,
  string? StartupCommand,
  string OriginalCommandLine);
```

Steps:
1. **Normalize**: collapse line continuations (`\\\n`), trim.
2. **Tokenize**: shell-style with quote handling (single/double), preserve quoted segments verbatim.
3. **Recognize `docker run` / `docker container run`**:
   - Walk Docker flags until the image token (first non-flag, non-flag-value token).
   - Extract `-e KEY=VAL` / `--env KEY=VAL` → env dict.
   - Extract `-p H:C` / `--publish H:C` → remember host port.
   - Keep image token + everything after as the "inner" command.
   - Strip safe flags from being shown in args (`--rm`, `--init`, `-it`, `-i`, `-t`, `--name X`, `-d`, `--detach`).
4. **Detect HTTP hints** in the inner args:
   - `--transport http|sse|streamable-http` → HTTP transport family.
   - `--port N` (or `-p H:C` with C matching `--port`) → port.
   - URL = `http://localhost:<hostPort>` (host side of `-p`, fallback to `--port`).
5. **Output**:
   - HTTP detected: `SuggestedTransport=Http/Sse/StreamableHttp`, `SuggestedUrl=...`, `StartupCommand=originalLine` with `-d` injected after `docker run`/`docker container run` if missing, env vars kept on the StartupCommand (not as MCP env).
   - Otherwise: `Command=<first token of inner>`, `Args=rest`, `EnvironmentVariables=collected`, `SuggestedTransport=Stdio`.
6. **Non-docker commands** (`npx ...`, `uvx ...`, `node x.js`, `./bin/server`):
   - First token = `Command`, rest = `Args`, no env unless preceded by `KEY=val` shell-prefix syntax (e.g., `FOO=bar npx server`).

### Apply parsed result

A small helper on the view-model side, e.g. `McpServerViewModel.ApplyParsed(ParsedCommand p)`:
- Copy fields onto the bound `McpServer`.
- Update observable properties (raise `PropertyChanged`).
- Set status message: `"Parsed as HTTP server on port 8080"` or `"Parsed as stdio command (npx)"`.

### UI

**Add server (create + paste in one step)**: split-button or extra icon button next to `+`:
- `+` → unchanged behaviour
- `📋` icon → reads clipboard, parses, creates a new server pre-filled. If clipboard is empty/unparseable, show status `"Clipboard does not contain a runnable command"` and do nothing.

**Detail view**: button "Paste command from clipboard" near the Command/URL field. Confirms before overwriting non-default fields (skip confirmation if server is fresh/empty).

**Start button**: shown on detail view when `StartupCommand` is non-empty. Label: "Start" + small hint text below: `"Stop manually, e.g. docker stop <name>"`.

### StartupCommandRunner (`McpManager.Core/Services/StartupCommandRunner.cs`)

Detached execution:
- macOS/Linux: `/bin/sh -c "<cmd>"` with `setsid` if available, else fallback to `Process.Start` with `UseShellExecute=false` and `nohup` prefix; we set `ProcessStartInfo.CreateNoWindow=true`. To survive parent exit, on Unix we either spawn via `setsid /bin/sh -c "<cmd>"` (if `setsid` exists) or via `nohup /bin/sh -c "<cmd> >/dev/null 2>&1 &"`.
- Windows: `cmd.exe /C start "" /B <cmd>` with `CREATE_NEW_PROCESS_GROUP | DETACHED_PROCESS` flags via `Process.StartInfo` is not directly settable from .NET without P/Invoke; simplest portable approach: `cmd.exe /C start "" <cmd>` which detaches.

Returns success if the launcher exits with 0 within ~3 seconds (the actual long-running child is already detached). Errors bubble up as a status message.

### Tests

In `tests/McpManager.Tests/CommandLineParserTests.cs`:
- `docker run --rm -i mcp/filesystem` → stdio, command=`docker`, args=`run --rm -i mcp/filesystem` (kept).
- `npx -y @scope/server arg` → stdio.
- `FOO=1 BAR=2 npx server` → stdio with env `{FOO:1, BAR:2}`.
- The bytebase/dbhub example → HTTP, url=`http://localhost:8080`, env `{}`, StartupCommand contains original with `-d` injected.
- Multi-line with `\` continuations → same as one-line.
- Quoted DSN with `&` and special chars → preserved as a single arg.
- `docker run -p 9000:8080 image --port 8080` → HTTP, url=`http://localhost:9000`.

## Risks

- Detached process behaviour differs across OSes; cover with manual testing on macOS first (the user's platform), Linux best-effort, Windows TBD.
- Heuristics for "is this an HTTP server?" might mis-classify; user can switch transport in detail view afterwards.

## File List

- New `src/McpManager.Core/Services/CommandLineParser.cs`
- New `src/McpManager.Core/Models/ParsedCommand.cs`
- New `src/McpManager.Core/Services/StartupCommandRunner.cs` + interface
- New `tests/McpManager.Tests/CommandLineParserTests.cs`
- Edit `src/McpManager/ViewModels/MainWindowViewModel.cs` — `PasteServerFromClipboardCommand`, `StartServerCommand`
- Edit `src/McpManager/ViewModels/McpServerViewModel.cs` — `ApplyParsed`, expose `HasStartupCommand`
- Edit `src/McpManager/Views/MainWindow.axaml` — paste button next to Add Server, paste + start buttons in detail view
