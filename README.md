# MCP Manager

A cross-platform desktop application for managing Model Context Protocol (MCP) servers and exporting configurations to multiple AI coding clients. Built with .NET 10 and Avalonia.

## Features

- **Multi-Server Management**: Configure stdio, HTTP, SSE, and Streamable HTTP MCP servers in one place
- **Multi-Target Export**: Export configurations to Claude Code, Claude Desktop, OpenCode, and Codex (global or project-local)
- **Shared Profiles**: Reuse server selections and tool preferences across clients, with explicit target overrides
- **Update Overview**: Preview affected files, apply changes together and detect external changes
- **Per-Target Tool Permissions**: Control which tools are always-allowed per server per target
- **Transport Detection**: Automatically detect the transport type of HTTP-based MCP servers
- **Server Testing**: Test MCP server connectivity directly or via bridge
- **Tool Discovery**: Fetch and manage available tools from each server
- **Bridge Support**: Automatic mcp-proxy bridge wrapping for HTTP servers in Claude Desktop
- **Cross-Platform**: Works on macOS, Linux, and Windows

## Architecture

```
┌──────────────────────────────────────────────────┐
│                  MCP Manager GUI                 │
│                   (Avalonia)                     │
├──────────────────────────────────────────────────┤
│                 McpManager.Core                  │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ │
│  │  Registry   │ │  Config     │ │  Transport  │ │
│  │  Service    │ │  Generators │ │  Detection  │ │
│  └─────────────┘ └─────────────┘ └─────────────┘ │
│                        │                         │
│         ┌──────────────┼──────────────┐          │
│         ▼              ▼              ▼          │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ │
│  │ Claude Code │ │   Claude    │ │  OpenCode   │ │
│  │  .mcp.json  │ │  Desktop    │ │  opencode   │ │
│  │             │ │  config     │ │   .json     │ │
│  └─────────────┘ └─────────────┘ └─────────────┘ │
└──────────────────────────────────────────────────┘
```

## Installation (macOS)

```bash
brew tap frankhommers/tap
brew install --cask frankhommers/tap/mcp-manager
```

> **Note:** The app is not notarized by Apple. On first launch macOS may show a warning. The Homebrew cask automatically removes the quarantine flag. If you downloaded manually, run:
> ```bash
> xattr -d com.apple.quarantine "/Applications/MCP Manager.app"
> ```

## Requirements

- **.NET 10 SDK** or later
- Supported Operating Systems:
  - macOS 10.15 or later
  - Linux
  - Windows 10 or later

## Getting Started

### Building from Source

```bash
git clone https://github.com/frankhommers/mcp-manager.git
cd mcp-manager
dotnet build McpManager.slnx
```

### Running

```bash
dotnet run --project src/McpManager/McpManager.csproj
```

### Running Tests

```bash
dotnet test McpManager.slnx
```

## Usage

### MCP Protocol Support

Connection tests, transport detection, and tool discovery use the official MCP C# SDK 2.2.0.
They prefer MCP **2026-07-28** using `server/discover` and per-request protocol metadata. The SDK
negotiates an older supported revision when required by the server. Connection results show the actual
negotiated protocol version; servers that omit optional identity metadata are accepted.

**Test Bridge** uses the same SDK connection over stdio, passing the configured bridge command and
arguments directly. The configured bridge (for example, `mcp-proxy`) controls its own connection to the
remote server. Exporting a configuration does not upgrade the destination client's protocol support.

### Central management

MCP Manager uses a central server library, reusable profiles, and one target per client and location.
The **Overview** lists managed targets by scope with their update status and a file change preview.
Clipboard and Quick Export are one-time destinations and are excluded from **Update all**.

1. Add servers under **Servers**. Connection testing, transport detection and tool discovery are available here.
   **Used by** shows the targets that currently include a server.
2. Create a **Profile**, name it, and select shared servers and tool preferences.
3. Add a **Target**, choose its client and location, and link a profile. Multiple clients in the same project
   are separate targets. Select several targets in Overview to assign a profile or update them together.
4. Review **Preview changes**, then choose **Update all**, **Update selected**, or update an individual target.
   Updating also saves the central workspace. **Save all** saves only the workspace.

Targets inherit server inclusion and tool preferences from their profile, with server defaults as the fallback
for tool preferences. Editing a target choice creates an explicit override, including an empty tool list or an
excluded server. Inherited values are never copied into the target's saved overrides. **Reset server overrides**
restores both server inclusion and tool preferences; **Reset all overrides** resets every server in the target.
Changing or detaching a profile keeps explicit overrides. A profile cannot be deleted while targets use it.
Client exports differ in which tool preferences they write; the target editor describes this limitation.

Server lists and tool lists support search, A–Z ordering and actions on the visible results. **Fetch tools**
discovers tool names. Reading a destination config does not change central server choices or import tool
permissions. Unrecognized servers can be explicitly imported from the target's existing configuration panel.

### Updating and conflicts

**Check targets** compares the generated configuration with the current destination files. Edits in the app
refresh the overview after a short delay. External file changes are checked on demand and before updating;
there is no background file watcher or automatic write-on-save mode.

- A shared server or profile edit affects all linked targets except their explicit overrides.
- Unmanaged server entries and unrelated client settings are preserved by the existing JSON/TOML merge.
- Same-name unmanaged entries require a choice: replace, keep existing, or cancel the batch.
- Files changed since the last successful update block the batch. Open the affected target, inspect its file,
  and explicitly choose **Reapply central settings over external changes** if the central configuration should win.
- Existing managed servers missing from the new library must be imported before a target's first update.
- Multiple targets writing the same destination are rejected before writing.

All configurations are prepared before writing. Changed files are staged alongside their destinations and
existing files receive a `.mcp-manager.bak` backup. A failure restores files already written in that batch;
if restoration itself fails, the error directs you to those backups. Unchanged files are not rewritten.
This provides recovery from write errors, not a filesystem-wide atomic transaction or power-loss guarantee.
Client JSON/TOML formatting and comments may change during serialization.

Targets record hashes and timestamps of applied files, not copies of their secrets. The central workspace
can contain configured credentials and should be treated as private. Restart or reload clients when needed;
MCP Manager updates their files, not their running connections.

### Workspace format

The new UI uses `workspace.json` in the application's `McpManager` data directory, with `formatVersion: 2`.
There is **no migration, legacy mode, or reader for the previous `mcp-registry.json` format**. Configure a new
workspace. The old file is not read or modified. Unsupported or corrupt workspace files produce a load error
instead of silently resetting data. Client configuration files retain their normal native formats.

Project-local Codex targets write `<project>/.codex/config.toml`; global Codex writes `~/.codex/config.toml`
or the configured global path. Project-local settings do not modify global settings. Codex only loads project
config for trusted projects.

Managed entries use stable IDs (`MCP_MANAGER_ID` in stdio environments or `X-MCP-Manager-Id` in HTTP headers).
Renaming a server replaces its previous managed entry; deselecting it removes that managed entry on update.
These markers indicate ownership and are not credentials.

### Bridge Configuration

HTTP-based MCP servers need an stdio wrapper (mcp-proxy) for Claude Desktop. Configure bridge commands in **Preferences**:

- `{url}` — replaced with the server URL
- `{args}` — replaced with per-target bridge arguments
- `{headerArgs}` — standalone placeholder replaced with the configured header argument pattern, repeated for every HTTP header

The header argument pattern defaults to `--headers {key} {value}` for `mcp-proxy`. Change it for bridges
that use a different syntax, such as `-H '{key}: {value}'` or `--header={key}={value}`.

## Key Technologies

- [.NET 10](https://dotnet.microsoft.com/)
- [Avalonia](https://avaloniaui.net/) — Cross-platform UI framework
- [FluentTheme](https://github.com/avaloniaui/avalonia) — Fluent design system
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) — MVVM framework
- [ModelContextProtocol SDK](https://github.com/modelcontextprotocol/csharp-sdk) — Official MCP client library
- [Material Design Icons](https://pictogrammers.com/library/mdi/) — Iconography

## License

This project is licensed under the MIT License. See `LICENSE`.

## Support

If you find this project useful, consider supporting development:

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/frankhommers)
