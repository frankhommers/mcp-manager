# Claude Code Global Target

Add a global target for Claude Code that writes MCP server definitions to `~/.claude.json`, enabling cross-project MCP server configuration.

## Background

Claude Code stores global (user-scope) MCP servers in `~/.claude.json` under a top-level `mcpServers` key. This file also contains other data (preferences, OAuth sessions, per-project state), so the generator must merge — only replacing the `mcpServers` section and preserving everything else.

The format of the `mcpServers` section is identical to project-level `.mcp.json`:

```json
{
  "numStartups": 42,
  "mcpServers": {
    "my-server": {
      "command": "npx",
      "args": ["-y", "@some/server"],
      "env": { "KEY": "value" }
    },
    "remote-server": {
      "type": "http",
      "url": "https://example.com/mcp"
    }
  },
  "projects": { }
}
```

File path: `~/.claude.json` on all platforms (`%USERPROFILE%\.claude.json` on Windows).

## Changes

### 1. Model — `TargetClientFlags`

File: `src/McpManager.Core/Models/McpRegistry.cs`

- Add `ClaudeCodeGlobal = 128`
- Update `All` to include `ClaudeCodeGlobal`

### 2. Config Generator — `ClaudeCodeGlobalConfigGenerator`

New file: `src/McpManager.Core/ConfigGenerators/ClaudeCodeGlobalConfigGenerator.cs`

- Extends `ClaudeCodeConfigGenerator` to reuse the server JSON generation logic
- Overrides `ClientName` → `"Claude Code (Global)"`
- Overrides `ConfigFileName` → `.claude.json`
- Adds `ExistingConfigPath` property (same pattern as `OpenCodeConfigGenerator`)
- Overrides `GenerateConfig` to:
  1. Call `base.GenerateConfig()` to get the `{"mcpServers": {...}}` JSON
  2. Parse the result to extract the `mcpServers` object
  3. Load existing `~/.claude.json` (or start with empty `{}`)
  4. Replace only the `mcpServers` key, preserve all other keys
  5. Serialize and return

### 3. Services

**`RegistryService`** — add `GetDefaultClaudeCodeGlobalConfigPath()`:
- All platforms: `~/.claude.json` (UserProfile + `.claude.json`)

**`ConfigExportService`**:
- Add `ClaudeCodeGlobalConfigGenerator` field
- Add export block for `TargetClientFlags.ClaudeCodeGlobal` (with merge, like OpenCode/Codex)
- Add preview block for same

### 4. UI — NewTargetDialog

**`NewTargetDialog.axaml`**:
- Add RadioButton `RbClaudeCode` with `Content="Claude Code"` and `Tag="ClaudeCode"` before `RbClaudeDesktop`

**`NewTargetDialog.axaml.cs`**:
- Add disable check for `TargetClientFlags.ClaudeCodeGlobal`
- Add `RbClaudeCode` to the buttons array
- Add switch case `"ClaudeCode"` creating a global `TargetFolder` with:
  - `Name = "Claude Code"`
  - `Path` from `RegistryService.GetDefaultClaudeCodeGlobalConfigPath()` parent directory
  - `EnabledClients = TargetClientFlags.ClaudeCodeGlobal`
  - `IsGlobal = true`

### 5. UI — TargetFolderViewModel

- Add `IsClaudeCodeGlobal` property: `IsGlobal && _model.EnabledClients.HasFlag(TargetClientFlags.ClaudeCodeGlobal)`
- Add `ConfigFilePath` case for `ClaudeCodeGlobal` → `.claude.json`

### 6. UI — Converters

**`TargetIconConverter`**:
- Add case for `IsClaudeCodeGlobal` → `"ClaudeLogo"` icon
- Add color case → `#D97757` (Claude orange, same as Claude Desktop fallback)

**`TargetBadgeConverter`**:
- Add case for `IsClaudeCodeGlobal` → `"CLAUDE CODE"`

### 7. Testing

- Verify merge behavior: existing `~/.claude.json` keys (numStartups, projects, etc.) are preserved
- Verify empty/missing file creates valid output
- Verify stdio and HTTP server types generate correctly
