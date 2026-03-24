# Typed Targets & New Client Formats Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add Cursor, Windsurf, and VS Code as supported MCP client targets, and make all typed targets (including Claude Desktop and Codex CLI) addable via a dialog instead of hardcoded.

**Architecture:** Extend `TargetClientFlags` enum with three new values. Create config generators for each new client (Cursor reuses Claude Code format, Windsurf reuses Claude Desktop format, VS Code has its own format). Wire generators into `ConfigExportService`. Replace hardcoded global targets in `CreateDefaultRegistry()` with an empty registry. Add a "New Target" dialog with radio buttons for choosing the target type.

**Tech Stack:** C# / .NET 10, Avalonia UI, CommunityToolkit.Mvvm, System.Text.Json, Tomlyn

---

### Task 1: Extend TargetClientFlags enum

**Files:**
- Modify: `src/McpManager.Core/Models/McpRegistry.cs:72-80`

Add three new flags:

```csharp
[Flags]
public enum TargetClientFlags
{
  None = 0,
  ClaudeCode = 1,
  ClaudeDesktop = 2,
  OpenCode = 4,
  Codex = 8,
  Cursor = 16,
  Windsurf = 32,
  VsCode = 64,
  All = ClaudeCode | ClaudeDesktop | OpenCode | Codex | Cursor | Windsurf | VsCode,
}
```

**Commit:** `feat: add Cursor, Windsurf, VsCode to TargetClientFlags`

---

### Task 2: Create CursorConfigGenerator

**Files:**
- Create: `src/McpManager.Core/ConfigGenerators/CursorConfigGenerator.cs`

Cursor uses the same JSON format as Claude Code (`mcpServers.{name}.command/args`), but writes to `.cursor/mcp.json`.

```csharp
public class CursorConfigGenerator : IConfigGenerator
{
  public string ClientName => "Cursor";
  public string ConfigFileName => "mcp.json";
  public string? ConfigSubFolder => ".cursor";
  // GenerateConfig: delegate to same logic as ClaudeCodeConfigGenerator
}
```

Since the format is identical to Claude Code, extract the shared generation logic into a base class or helper to avoid duplication.

**Commit:** `feat: add CursorConfigGenerator`

---

### Task 3: Create WindsurfConfigGenerator

**Files:**
- Create: `src/McpManager.Core/ConfigGenerators/WindsurfConfigGenerator.cs`

Windsurf uses the same JSON format as Claude Desktop (`mcpServers.{name}.command/args` with bridge wrapping for HTTP), but writes to `mcp_config.json` (no subfolder — the target path itself points to `~/.codeium/windsurf/`).

```csharp
public class WindsurfConfigGenerator : IConfigGenerator
{
  public string ClientName => "Windsurf";
  public string ConfigFileName => "mcp_config.json";
  public string? ConfigSubFolder => null;
  // GenerateConfig: delegate to same logic as ClaudeDesktopConfigGenerator
}
```

Extract shared bridge-wrapping logic from `ClaudeDesktopConfigGenerator` to avoid duplication.

**Commit:** `feat: add WindsurfConfigGenerator`

---

### Task 4: Create VsCodeConfigGenerator

**Files:**
- Create: `src/McpManager.Core/ConfigGenerators/VsCodeConfigGenerator.cs`

VS Code uses a different format: `mcp.servers.{name}` with a required `type` field.

```json
{
  "mcp": {
    "servers": {
      "chronoid": {
        "type": "stdio",
        "command": "/path/to/binary",
        "args": []
      }
    }
  }
}
```

Key differences from Claude Code format:
- Root key is `mcp.servers` not `mcpServers`
- Each server has a `type` field: `"stdio"` for stdio, or URL-based for HTTP
- Env vars use `"env"` key (same as Claude Code)

```csharp
public class VsCodeConfigGenerator : IConfigGenerator
{
  public string ClientName => "VS Code";
  public string ConfigFileName => "mcp.json";
  public string? ConfigSubFolder => ".vscode";
}
```

**Commit:** `feat: add VsCodeConfigGenerator`

---

### Task 5: Wire new generators into ConfigExportService

**Files:**
- Modify: `src/McpManager.Core/Services/ConfigExportService.cs`

Add three new generator fields and handle the new flags in `ExportAsync()` and `PreviewConfigs()`:

```csharp
private readonly CursorConfigGenerator _cursorGen = new();
private readonly WindsurfConfigGenerator _windsurfGen = new();
private readonly VsCodeConfigGenerator _vsCodeGen = new();
```

Add export/preview cases for `TargetClientFlags.Cursor`, `.Windsurf`, `.VsCode`.

Windsurf needs bridge commands like Claude Desktop. Cursor does not (it supports native HTTP).

**Commit:** `feat: wire Cursor, Windsurf, VS Code generators into export service`

---

### Task 6: Add default paths helper

**Files:**
- Modify: `src/McpManager.Core/Services/RegistryService.cs`

Add static methods for default config paths:

```csharp
public static string GetDefaultCursorConfigPath()  // ~/.cursor/
public static string GetDefaultWindsurfConfigPath() // ~/.codeium/windsurf/
public static string GetDefaultVsCodeConfigPath()   // platform-dependent
```

Also: make existing `GetDefaultClaudeDesktopConfigPath()` and `GetDefaultCodexConfigPath()` public static so the UI can use them.

**Commit:** `feat: add default config path helpers for all client types`

---

### Task 7: Remove hardcoded global targets from CreateDefaultRegistry

**Files:**
- Modify: `src/McpManager.Core/Services/RegistryService.cs:58-91`

`CreateDefaultRegistry()` should return an empty registry with no pre-created targets:

```csharp
private static McpRegistry CreateDefaultRegistry()
{
  return new McpRegistry();
}
```

Existing users already have their targets persisted in `mcp-registry.json`, so they won't be affected.

**Commit:** `refactor: remove hardcoded global targets from default registry`

---

### Task 8: Add "New Target" dialog

**Files:**
- Create: `src/McpManager/Views/NewTargetDialog.axaml`
- Create: `src/McpManager/Views/NewTargetDialog.axaml.cs`
- Create: `src/McpManager/ViewModels/NewTargetDialogViewModel.cs`

Dialog with radio buttons:
- Claude Desktop (global, auto-fills path)
- Codex CLI (global, auto-fills path)
- Cursor (global, auto-fills path)
- Windsurf (global, auto-fills path)
- VS Code (global, auto-fills path)
- Clipboard
- Folder (custom name + path)

Each typed option shows the default path. Result: a `TargetFolder` model with the chosen type pre-configured.

Typed targets that already exist in the registry should be disabled/greyed out in the dialog.

**Commit:** `feat: add New Target dialog with client type selection`

---

### Task 9: Update ViewModel to use dialog

**Files:**
- Modify: `src/McpManager/ViewModels/MainWindowViewModel.cs`

Change `AddTargetFolder()` to open the new dialog instead of directly creating a folder target. Wire the dialog result into the existing target creation flow.

Also update `EnsureQuickExportTarget()` — it should still auto-create the Quick Export target if missing.

**Commit:** `feat: use New Target dialog for adding targets`

---

### Task 10: Update sidebar display for new target types

**Files:**
- Modify: `src/McpManager/Views/MainWindow.axaml` (sidebar target list)
- Modify: `src/McpManager/ViewModels/TargetFolderViewModel.cs`

Add display properties for new types:
- `IsCursor`, `IsWindsurf`, `IsVsCode` computed properties
- Sidebar should show appropriate icons/labels (e.g., "CURSOR", "WINDSURF", "VSCODE" subtitles like existing "DESKTOP", "CLIPBOARD" etc.)

**Commit:** `feat: show client type labels in sidebar for new targets`

---

### Task 11: Build, test, verify

Run full build and tests:

```bash
cd mcp-manager
dotnet build
dotnet test
```

Manually verify in the UI that:
1. New Target dialog shows all client types
2. Adding a Cursor/Windsurf/VS Code target works
3. Exporting to each new target generates correct config format
4. Existing Claude Desktop / Codex CLI targets still work
5. Empty default registry works for fresh installs

**Commit:** Final fixes if needed
