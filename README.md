# MCP Manager

A cross-platform desktop application for managing Model Context Protocol (MCP) servers and exporting configurations to multiple AI coding clients. Built with .NET 10 and Avalonia.

## Features

- **Multi-Server Management**: Configure stdio, HTTP, SSE, and Streamable HTTP MCP servers in one place
- **Servers × Places Overview**: One matrix of servers and places; one checkbox includes a server in a place
- **Places**: Deploy to global harnesses (Claude Code, Claude Desktop, OpenCode, Codex, Cursor, VS Code, Windsurf)
  or to project folders with one or more harnesses
- **Disk State**: The matrix shows what is in your project configs, with unknown servers and differences, without touching the files
- **Save and Deploy**: Store changes in MCP Manager, then deploy them to the harness config files when you choose
- **Diff Screen**: Field-level comparison with masked secrets for reviews, conflicts and external changes
- **Per-Place Tool Permissions**: Control which tools are always-allowed per server per place
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

### Overview

The **Overview** is a matrix: servers are rows (A–Z, with search), places are columns, grouped into **Global**
(one column per global harness) and **Projects** (one column per folder, with harness chips such as CC, OC, CX).
Checking a cell includes the server in every harness of that place. Click a server name to open the server
editor; click a column header (or a place in the sidebar) to open the place editor. A marker on a column shows
that the place is behind, not deployed yet, or changed outside MCP Manager.

- **Server**: an entry in your library.
- **Place**: a destination — a global harness, or a project folder with one or more harnesses.
- **Save**: stores your changes in MCP Manager (`workspace.json`).
- **Deploy**: writes the stored configuration into the harness config files.

Clipboard and Quick Export are one-time destinations under the **Export…** menu; they are not places.

### Places with several harnesses

A project place can cover several harnesses in the same folder (Claude Code, OpenCode, Codex, Cursor, VS Code).
Tick them in the place editor. Every harness of a place gets the same servers; each config file is deployed and
checked for external changes on its own. The place editor lists every config file it writes.

For OpenCode, `<folder>/.opencode/opencode.json` is used when it exists, for both reading and deploy. Otherwise
`opencode.jsonc` or `opencode.json` in the folder root is used.

### Matrix shows disk

The Overview reads your project folders itself, so there is no separate scan step. Folders are looked for in the
folders set under **Preferences** (default `~/Repos`), up to four levels deep. The project picker lists them under
**Found on disk**; **Other folder…** adds one that is not in that list. Choosing a folder that is not a place yet
shows it as a candidate; the first change you make there makes it a project. A chosen folder without any config
becomes a project right away, so you can pick its harnesses. Reading never writes to config files; changes are
kept by a Save.

Servers found in a project's configs that are not in the library appear as **Found in your configs, not in your
library** rows. **+ Library** adds one to the library. An orange dot in a cell means the config on disk differs
from the library and you have not chosen yet; it opens a comparison dialog, where each choice says what it does:

- **Keep library**: the library stays the same; on deploy the config gets the library version.
- **Put this version in the library**: the library server changes, also for every other place that uses it.
- **Import as separate server**: adds a copy named after the place; on deploy the entry in the config is renamed.

A grey dot means the difference is settled: deploying writes the library version there.

### Save and Deploy

Any edit — in the matrix, a server or a place — shows **● Unsaved changes** with **Cancel**, **Also deploy** and
**Save**. Save validates first (for example, a stdio server needs a command) and stores the workspace. With
**Also deploy (N files)** checked, it then deploys the places affected by the change; N counts the config files
that would actually change. The checkbox is unchecked every time new changes start. Cancel returns to the last
saved state, and leaving a page with unsaved changes asks to save, discard or stay.

A place is **behind** when it was deployed before and its stored configuration no longer matches what was
deployed. When places are behind, a bar shows *"N places not deployed"* with **Review** and **Deploy now**.
Review opens the diff screen for every server that would change; Deploy now deploys all behind places at once.
A place that was never deployed is only written when you deploy it yourself: **Deploy** in the place editor, or
Save with Also deploy on a change that affects it. Deploys are refused while there are unsaved changes.

### Diff screen

Reviews, differences found on disk, conflicts and external changes all use the same diff screen. Each server is compared
field by field (command, args, URL, type, every environment variable and header, tool permissions); equal fields
are dimmed, additions are green and removals are red. Values that look like secrets (keys containing TOKEN, KEY,
SECRET, PASSWORD or AUTH, and Authorization headers) are masked with their last four characters; click **Show**
to reveal one value. With several servers, use Previous/Next.

### External changes

When a deployed file was changed outside MCP Manager, deploying that place is blocked and its column shows a
marker. Open the place and choose **Compare** to see the file on disk next to what MCP Manager will write, then:

- **Overwrite with MCP Manager** deploys the place over the external change (a backup is kept), or
- **Keep file** leaves the files untouched and stops treating them as changed outside MCP Manager. This is saved
  right away.

After Keep file, MCP Manager still lists the place as not deployed; deploying it later replaces the file (a backup
is kept).

### Deploy safety

- Unmanaged server entries and unrelated harness settings are preserved by the existing JSON/TOML merge.
- Same-name unmanaged entries require a choice: replace, keep existing, or cancel the batch. Servers adopted by
  the diff screen replace their same-name entries without asking, because it already asked.
- Existing managed servers missing from the library must be imported before a place's first deploy.
- Multiple places writing the same file are rejected before writing.

Server lists and tool lists support search, A–Z ordering and actions on the visible results. **Fetch tools**
discovers tool names. Reading a destination config does not change your server choices or import tool
permissions. Unrecognized servers can be imported explicitly from the place editor.

Profiles from earlier versions are kept in the workspace and still apply to the places linked to them, but they
are no longer edited in the UI.

All configurations are prepared before writing. Changed files are staged alongside their destinations and
existing files receive a `.mcp-manager.bak` backup. A failure restores files already written in that batch;
if restoration itself fails, the error directs you to those backups. Unchanged files are not rewritten.
This provides recovery from write errors, not a filesystem-wide atomic transaction or power-loss guarantee.
Client JSON/TOML formatting and comments may change during serialization.

Places record hashes and timestamps of deployed files, not copies of their secrets. The central workspace
can contain configured credentials and should be treated as private. Restart or reload clients when needed;
MCP Manager updates their files, not their running connections.

### Workspace format

The new UI uses `workspace.json` in the application's `McpManager` data directory, with `formatVersion: 2`.
There is **no migration, legacy mode, or reader for the previous `mcp-registry.json` format**. Configure a new
workspace. The old file is not read or modified. Unsupported or corrupt workspace files produce a load error
instead of silently resetting data. Client configuration files retain their normal native formats.

Project-local Codex places write `<project>/.codex/config.toml`; global Codex writes `~/.codex/config.toml`
or the configured global path. Project-local settings do not modify global settings. Codex only loads project
config for trusted projects.

Managed entries use stable IDs (`MCP_MANAGER_ID` in stdio environments or `X-MCP-Manager-Id` in HTTP headers).
Renaming a server replaces its previous managed entry; deselecting it removes that managed entry on deploy.
These markers indicate ownership and are not credentials.

### Bridge Configuration

HTTP-based MCP servers need an stdio wrapper (mcp-proxy) for Claude Desktop. Configure bridge commands in **Preferences**:

- `{url}` — replaced with the server URL
- `{args}` — replaced with per-place bridge arguments
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
