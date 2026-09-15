# MCP Manager

A cross-platform desktop application for managing Model Context Protocol (MCP) servers and exporting configurations to multiple AI coding clients. Built with .NET 10 and Avalonia.

## Features

- **Multi-Server Management**: Configure stdio, HTTP, SSE, and Streamable HTTP MCP servers in one place
- **Multi-Target Export**: Export configurations to Claude Code, Claude Desktop, OpenCode, and Codex (global or project-local)
- **Existing Target Selection**: Preselect managed servers already active in any selected output format
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

### Finding and Selecting Servers

The sidebar sorts servers A–Z by display name. Clipboard and Quick Export stay at the top of the target list,
followed by global targets and project folders, each sorted A–Z.

Each target shows its servers in aligned rows, with the server name, group, configuration key, and transport.
Use search to filter by name, configuration key, or group. The selection count includes servers hidden by
the search. **Select shown** and **Clear shown** only affect visible results. Checking a server does not move
its row, and clearing the search preserves your choices.

Server selection and tool permissions share one list. Each server row shows its selected tool count;
open **Tools** on that row to fetch tools, search them, and adjust the selection. Tool lists use A–Z order,
and their **Select shown** / **Clear shown** buttons only affect matching tools for that server. Collapsing
the row, searching servers, or temporarily excluding a server keeps its tool choices. Excluded servers'
tools can be inspected; include the server to edit them. Fetch progress and errors appear in the same row.
**Fetch missing tools** discovers tools for all included servers with no tools yet.

**Found in this target** lists servers in the selected destination files that are missing from your library.
Each row identifies the client and source file. **Import into MCP Manager** imports that server and selects it
for this target; use **Save** to persist your library changes. Import does not write destination files or start
servers. Servers already in the library are recognized by their ID or configuration name and are not offered
again. When the same name occurs in multiple files, choose the source you want to import.

Servers marked **Not owned by MCP Manager** still use the conflict dialog on export, where you can replace
the existing entry or keep it. Servers previously managed by MCP Manager retain their original ID on import.

### Adding MCP Servers

1. Click **+** next to SERVERS in the sidebar
2. Choose connection mode: Local Command (stdio) or Remote URL (HTTP)
3. For stdio: enter the command and arguments (e.g., `npx` with `@modelcontextprotocol/server-filesystem /path`)
4. For HTTP: enter the URL and select the protocol (SSE, Streamable HTTP, or basic HTTP)
5. Use **Detect** to auto-detect the transport type for HTTP servers
6. Use **Fetch Tools** to discover available tools

### Adding Export Targets

1. Click **+** next to TARGETS in the sidebar
2. Choose a global client target or a Project Folder; Quick Export also supports project folders
3. For project folders, enable Claude Code, OpenCode, and/or Codex
4. Existing managed servers are preselected using the union of the selected formats; adjust which servers to include
5. Configure per-server tool permissions if needed

Project-local Codex exports go to `<project>/.codex/config.toml`. The global Codex target still uses
`~/.codex/config.toml` (or the configured global path). Local exports preserve the project's other settings
and do not read or modify the global config. Codex only loads project config for trusted projects.

Selecting a target, changing its folder, or changing its export formats reads the existing destination configs.
Save and Export also re-read them. **Re-read config** checks them on demand; the list shows the read status
and the time of the last check. External file edits are picked up at the next check, with no background file watcher.
Servers are matched by their MCP Manager IDs, including renamed servers. A server active in any selected
format is preselected; saved inclusions, saved exclusions, and current manual choices take priority.
Tool checkboxes come from MCP Manager's saved per-target choices, falling back to the server's defaults.
Re-reading a target config does not import tool permissions. **Fetch tools** discovers tool names and preserves
existing tool choices. Servers not owned by MCP Manager are preserved
separately and are not automatically adopted or selected. Missing library entries and unreadable configs
are reported beside the selection. Servers without ownership markers are shown as **Not owned by MCP Manager**.
Choosing a destination does not write any client config files.

### Exporting Configurations

- **Export all**: Exports all targets at once from the sidebar
- **Export selected target**: Exports only the currently selected target
- Configurations are written to the appropriate locations for each client

Exports identify managed servers by their stable server ID: `MCP_MANAGER_ID` in the environment for
stdio processes (including Claude Desktop bridges), or `X-MCP-Manager-Id` in HTTP headers
(`http_headers` for Codex). These are ownership markers, not credentials; the HTTP marker is sent to the server.

On export, marked server entries in the destination file are replaced by the target's current selection.
Renames remove the old entry, and deselected managed servers are removed. Manual changes inside a marked
entry are overwritten. Servers not owned by MCP Manager and unrelated client settings are preserved.
If an existing server has the same name as an exported server but is not owned by MCP Manager, a dialog
shows the server name and destination file, with an optional configuration comparison. Choose per conflict:

- **Replace with MCP Manager**: replace that entry and let MCP Manager manage it in future exports.
- **Keep existing**: preserve that entry without taking ownership and continue exporting the other servers.
- **Cancel export**: stop without writing any destination files. Closing the dialog also cancels.

Choices apply to the current export and destination file. Older exports without ownership markers are not
automatically adopted. Preview uses the same merge as export. All destination files are prepared before
writing, including all targets when using **Export all**, so cancellation or invalid configuration stops the
export before any file is changed. **Export all** skips the clipboard target. JSON/TOML formatting and comments
are not preserved when files are serialized.

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
