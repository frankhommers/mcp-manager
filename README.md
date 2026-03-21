# MCP Manager

A cross-platform desktop application for managing Model Context Protocol (MCP) servers and exporting configurations to multiple AI coding clients. Built with .NET 10 and Avalonia.

## Features

- **Multi-Server Management**: Configure stdio, HTTP, SSE, and Streamable HTTP MCP servers in one place
- **Multi-Target Export**: Export configurations to Claude Code, Claude Desktop, and OpenCode simultaneously
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

### Adding MCP Servers

1. Click **+** next to SERVERS in the sidebar
2. Choose connection mode: Local Command (stdio) or Remote URL (HTTP)
3. For stdio: enter the command and arguments (e.g., `npx` with `@modelcontextprotocol/server-filesystem /path`)
4. For HTTP: enter the URL and select the protocol (SSE, Streamable HTTP, or basic HTTP)
5. Use **Detect** to auto-detect the transport type for HTTP servers
6. Use **Fetch Tools** to discover available tools

### Adding Export Targets

1. Click **+** next to TARGETS in the sidebar
2. Choose a project folder, or use the built-in Claude Desktop and Clipboard targets
3. Enable which clients to export for (Claude Code, Claude Desktop, OpenCode)
4. Select which servers to include
5. Configure per-server tool permissions if needed

### Exporting Configurations

- **Export all**: Exports all targets at once from the sidebar
- **Export selected target**: Exports only the currently selected target
- Configurations are written to the appropriate locations for each client

### Bridge Configuration

HTTP-based MCP servers need an stdio wrapper (mcp-proxy) for Claude Desktop. Configure bridge commands in **Preferences**:

- `{url}` — replaced with the server URL
- `{args}` — replaced with per-target bridge arguments

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
