# Avalonia MCP Manager - Design Plan

## Problem Statement

Managing MCP (Model Context Protocol) servers across multiple clients is complex:
- **Different transport types**: stdio, HTTP, SSE, streamable-http
- **Different runtimes**: Docker containers, native commands (uvx, npx, node), local processes
- **Different clients**: Claude Code (.mcp.json), OpenCode (.opencode/opencode.json), Claude Desktop
- **Platform limitations**: Claude Desktop only supports stdio, requiring HTTP-to-stdio wrappers
- **Configuration drift**: Same MCP server needs different configs for each client
- **Secrets management**: Tokens and credentials scattered across config files

## Goals

1. **Unified MCP Server Registry**: Single source of truth for all MCP servers
2. **Multi-Client Export**: Generate configs for Claude Code, OpenCode, Claude Desktop
3. **Wrapper Generation**: Auto-generate stdio wrappers for HTTP servers (Claude Desktop)
4. **Docker Management**: Start/stop Docker-based MCP containers
5. **Secrets Management**: Centralized, secure storage for API keys and tokens
6. **Project Scoping**: Enable/disable MCPs per project with easy activation

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                    Avalonia MCP Manager                         │
├─────────────────────────────────────────────────────────────────┤
│  UI Layer (Avalonia)                                            │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌────────────┐│
│  │ MCP Registry│ │  Projects   │ │   Docker    │ │  Settings  ││
│  │    View     │ │    View     │ │   Status    │ │    View    ││
│  └─────────────┘ └─────────────┘ └─────────────┘ └────────────┘│
├─────────────────────────────────────────────────────────────────┤
│  Services Layer                                                 │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌────────────┐│
│  │  MCP Server │ │   Config    │ │   Docker    │ │  Secrets   ││
│  │   Service   │ │  Generator  │ │   Service   │ │   Store    ││
│  └─────────────┘ └─────────────┘ └─────────────┘ └────────────┘│
├─────────────────────────────────────────────────────────────────┤
│  Data Layer                                                     │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │  SQLite / JSON Database (mcp-registry.db / mcp-registry.json)│
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

---

## Data Model

### MCP Server Definition

```csharp
public class McpServer
{
    public Guid Id { get; set; }
    public string Name { get; set; }                    // e.g., "home-assistant"
    public string DisplayName { get; set; }             // e.g., "Home Assistant"
    public string Description { get; set; }

    // Transport configuration
    public McpTransportType TransportType { get; set; } // Stdio, Http, Sse, StreamableHttp

    // For Stdio servers
    public string? Command { get; set; }                // e.g., "uvx", "docker"
    public List<string> Args { get; set; }              // e.g., ["--with", "packaging", "ha-mcp@latest"]

    // For HTTP/SSE/Streamable servers
    public string? Url { get; set; }                    // e.g., "http://127.0.0.1:8765/mcp"

    // Docker-specific
    public bool IsDocker { get; set; }
    public string? DockerImage { get; set; }            // e.g., "mcp/filesystem:latest"
    public string? DockerContainerName { get; set; }
    public List<DockerPortMapping> PortMappings { get; set; }
    public List<DockerVolumeMapping> VolumeMappings { get; set; }

    // Environment variables (references to secrets)
    public Dictionary<string, EnvVarValue> EnvironmentVariables { get; set; }

    // Client compatibility
    public bool SupportsClaudeCode { get; set; }
    public bool SupportsClaudeDesktop { get; set; }
    public bool SupportsOpenCode { get; set; }

    // Auto-wrapper for Claude Desktop (if HTTP server)
    public bool RequiresStdioWrapper { get; set; }
}

public enum McpTransportType
{
    Stdio,
    Http,
    Sse,
    StreamableHttp
}

public class EnvVarValue
{
    public bool IsSecret { get; set; }
    public string? PlainValue { get; set; }             // For non-sensitive values
    public string? SecretKey { get; set; }              // Reference to secrets store
}
```

### Project Definition

```csharp
public class McpProject
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }                    // e.g., "/Users/frank/Repos/my-project"
    public List<Guid> EnabledMcpServers { get; set; }   // Which MCPs are active for this project
    public Dictionary<Guid, Dictionary<string, string>> ServerOverrides { get; set; } // Per-server env overrides
}
```

### Secrets Store

```csharp
public class SecretEntry
{
    public string Key { get; set; }                     // e.g., "HOMEASSISTANT_TOKEN"
    public string EncryptedValue { get; set; }          // Encrypted using OS keychain or DPAPI
    public string? Description { get; set; }
}
```

---

## Core Features

### 1. MCP Server Registry

**UI: Main Registry View**
- List all registered MCP servers with status indicators
- Add/Edit/Delete MCP server definitions
- Import from existing `.mcp.json` or `opencode.json` files
- Test connection/health check for HTTP-based servers

**Actions:**
- `Add Server` - Form to define new MCP server
- `Import from File` - Parse existing config files
- `Duplicate` - Copy an existing server config
- `Test Connection` - Verify server is reachable

### 2. Project Management

**UI: Projects View**
- List all registered projects
- Toggle MCP servers on/off per project
- Quick export buttons for each client format

**Actions:**
- `Add Project` - Register a project folder
- `Sync to Project` - Write config files to project
- `Open in Finder/Explorer` - Navigate to project folder

### 3. Config Generation

**Claude Code (.mcp.json)**
```json
{
  "mcpServers": {
    "home-assistant": {
      "command": "uvx",
      "args": ["--with", "packaging", "ha-mcp@latest"],
      "env": {
        "HOMEASSISTANT_URL": "https://example.com",
        "HOMEASSISTANT_TOKEN": "actual-token-value"
      }
    }
  }
}
```

**OpenCode (.opencode/opencode.json)**
```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "home-assistant": {
      "type": "local",
      "enabled": true,
      "command": ["uvx", "--with", "packaging", "ha-mcp@latest"],
      "environment": {
        "HOMEASSISTANT_URL": "https://example.com",
        "HOMEASSISTANT_TOKEN": "actual-token-value"
      }
    }
  }
}
```

**Claude Desktop (claude_desktop_config.json)**
- For stdio servers: direct passthrough
- For HTTP servers: generate stdio wrapper

### 4. HTTP-to-Stdio Wrapper (for Claude Desktop)

Claude Desktop only supports stdio transport. For HTTP-based MCP servers, we need to generate wrapper scripts.

**Wrapper Script Generation:**

```python
#!/usr/bin/env python3
# Auto-generated by MCP Manager
# Wrapper for: autodesk_fusion (HTTP -> Stdio)

import sys
import json
import httpx

MCP_URL = "http://127.0.0.1:8765/mcp"

def main():
    for line in sys.stdin:
        request = json.loads(line)
        response = httpx.post(MCP_URL, json=request)
        print(json.dumps(response.json()), flush=True)

if __name__ == "__main__":
    main()
```

**Or using mcp-proxy (recommended):**
```json
{
  "mcpServers": {
    "autodesk_fusion": {
      "command": "mcp-proxy",
      "args": ["http://127.0.0.1:8765/mcp"]
    }
  }
}
```

### 5. Docker Management

**UI: Docker Status Panel**
- Show running MCP containers
- Start/Stop/Restart controls
- Log viewer for each container
- Auto-start on app launch (optional)

**Implementation:**
- Use Docker.DotNet library for container management
- Monitor container health
- Expose mapped ports in UI

### 6. Secrets Management

**Secure Storage Options:**
- macOS: Keychain Access
- Windows: DPAPI / Windows Credential Manager
- Linux: libsecret / GNOME Keyring
- Fallback: Encrypted JSON file with master password

**UI: Secrets Manager**
- Add/Edit/Delete secrets
- Show which MCP servers use each secret
- Import from .env files
- Export to .env files (for backup)

---

## Project Structure

```
mcp-manager/
├── src/
│   ├── McpManager/                          # Main Avalonia app
│   │   ├── App.axaml
│   │   ├── App.axaml.cs
│   │   ├── Program.cs
│   │   ├── ViewModels/
│   │   │   ├── MainWindowViewModel.cs
│   │   │   ├── McpRegistryViewModel.cs
│   │   │   ├── ProjectsViewModel.cs
│   │   │   ├── DockerStatusViewModel.cs
│   │   │   ├── SecretsViewModel.cs
│   │   │   └── ServerEditorViewModel.cs
│   │   ├── Views/
│   │   │   ├── MainWindow.axaml
│   │   │   ├── McpRegistryView.axaml
│   │   │   ├── ProjectsView.axaml
│   │   │   ├── DockerStatusView.axaml
│   │   │   ├── SecretsView.axaml
│   │   │   └── ServerEditorDialog.axaml
│   │   ├── Models/
│   │   │   ├── McpServer.cs
│   │   │   ├── McpProject.cs
│   │   │   ├── SecretEntry.cs
│   │   │   └── Enums.cs
│   │   ├── Services/
│   │   │   ├── IMcpServerService.cs
│   │   │   ├── McpServerService.cs
│   │   │   ├── IConfigGeneratorService.cs
│   │   │   ├── ConfigGeneratorService.cs
│   │   │   ├── IDockerService.cs
│   │   │   ├── DockerService.cs
│   │   │   ├── ISecretsService.cs
│   │   │   ├── SecretsService.cs
│   │   │   └── IProjectService.cs
│   │   └── Converters/
│   │       └── BoolToColorConverter.cs
│   │
│   ├── McpManager.Core/                     # Shared library (for CLI, etc.)
│   │   ├── Models/
│   │   ├── Services/
│   │   └── ConfigGenerators/
│   │       ├── ClaudeCodeConfigGenerator.cs
│   │       ├── ClaudeDesktopConfigGenerator.cs
│   │       └── OpenCodeConfigGenerator.cs
│   │
│   └── McpManager.Tests/
│       └── ...
│
├── wrappers/                                # Generated wrapper scripts
│   └── .gitkeep
│
├── McpManager.sln
└── README.md
```

---

## Implementation Phases

### Phase 1: Core Foundation
- [ ] Set up Avalonia project with MVVM structure
- [ ] Implement data models (McpServer, McpProject)
- [ ] Create JSON-based persistence layer
- [ ] Build MCP Registry CRUD operations
- [ ] Basic UI for viewing/adding servers

### Phase 2: Config Generation
- [ ] Implement Claude Code config generator
- [ ] Implement OpenCode config generator
- [ ] Implement Claude Desktop config generator
- [ ] Add stdio wrapper generation for HTTP servers
- [ ] Export/sync configs to projects

### Phase 3: Project Management
- [ ] Project registration and management
- [ ] Per-project MCP server selection
- [ ] Batch export to project folders
- [ ] Watch project folders for external changes

### Phase 4: Docker Integration
- [ ] Docker container status monitoring
- [ ] Start/Stop/Restart containers
- [ ] Log viewing
- [ ] Auto-start configuration

### Phase 5: Secrets Management
- [ ] Platform-specific secure storage
- [ ] Secrets CRUD UI
- [ ] Integration with MCP server configs
- [ ] Import/Export functionality

### Phase 6: Polish & Advanced Features
- [ ] Import from existing configs
- [ ] Health checking for HTTP servers
- [ ] System tray integration
- [ ] Auto-update MCP servers (version checking)
- [ ] Backup/restore functionality

---

## Technology Stack

| Component | Technology |
|-----------|------------|
| UI Framework | Avalonia UI 11.x |
| MVVM Framework | CommunityToolkit.Mvvm |
| Data Storage | LiteDB or JSON files |
| Docker Integration | Docker.DotNet |
| HTTP Client | System.Net.Http / HttpClient |
| Secrets (macOS) | Security.SecureStorage or Keychain |
| Secrets (Windows) | System.Security.Cryptography.ProtectedData |
| JSON Handling | System.Text.Json |
| Dependency Injection | Microsoft.Extensions.DependencyInjection |

---

## Config File Formats Reference

### Claude Code / Claude Desktop (.mcp.json)

**Stdio Server:**
```json
{
  "mcpServers": {
    "server-name": {
      "command": "command-to-run",
      "args": ["arg1", "arg2"],
      "env": {
        "VAR_NAME": "value"
      }
    }
  }
}
```

**HTTP Server:**
```json
{
  "mcpServers": {
    "server-name": {
      "type": "http",
      "url": "http://127.0.0.1:8765/mcp"
    }
  }
}
```

### OpenCode (.opencode/opencode.json)

**Local (Stdio):**
```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "server-name": {
      "type": "local",
      "enabled": true,
      "command": ["command", "arg1", "arg2"],
      "environment": {
        "VAR_NAME": "value"
      }
    }
  }
}
```

**Remote (HTTP):**
```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "server-name": {
      "type": "remote",
      "enabled": true,
      "url": "http://127.0.0.1:8765/mcp"
    }
  }
}
```

### Claude Desktop (for HTTP servers via wrapper)

```json
{
  "mcpServers": {
    "server-name-wrapped": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://127.0.0.1:8765/mcp"]
    }
  }
}
```

---

## Design Decisions

1. **Storage**: JSON files for persistence
2. **Secrets**: Plaintext for now (encryption can be added later)
3. **Claude Desktop wrapper**: Use `mcp-remote` npm package
4. **Target management**: TreeView of target folders allowing hierarchical MCP activation
5. **Docker support**: Both individual containers AND docker-compose files
6. **Scope**: TreeView handles global vs project-specific - MCPs enabled at folder tree levels

---

## Target Folder TreeView Concept

```
📁 Global (~/Library/Application Support/Claude/claude_desktop_config.json)
│   ├── ✅ home-assistant
│   └── ✅ filesystem
│
📁 Projects
│   ├── 📁 ~/Repos/my-webapp
│   │   ├── 📄 .mcp.json (Claude Code)
│   │   │   ├── ✅ home-assistant (inherited)
│   │   │   └── ✅ postgres-mcp
│   │   └── 📄 .opencode/opencode.json
│   │       └── ✅ home-assistant (inherited)
│   │
│   └── 📁 ~/Repos/fusion-project
│       └── 📄 .mcp.json (Claude Code)
│           ├── ✅ autodesk-fusion
│           └── ⬜ home-assistant (disabled)
```

This allows:
- Global MCPs that apply everywhere
- Per-project overrides
- Per-client configs within projects
- Visual inheritance indicators

---

## Next Steps

1. ✅ Plan approved
2. Begin Phase 1 implementation
