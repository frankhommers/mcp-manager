# Claude Code Global Target Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a global target for Claude Code that writes MCP server definitions to `~/.claude.json`, enabling cross-project MCP server configuration.

**Architecture:** New `ClaudeCodeGlobalConfigGenerator` extends `ClaudeCodeConfigGenerator` and adds merge logic to preserve existing `~/.claude.json` content. Wired into UI via NewTargetDialog, converters, and ConfigExportService following the same patterns as OpenCode and Codex global targets.

**Tech Stack:** .NET 10, Avalonia UI, System.Text.Json, xUnit

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `src/McpManager.Core/Models/McpRegistry.cs` | Add `ClaudeCodeGlobal` flag |
| Create | `src/McpManager.Core/ConfigGenerators/ClaudeCodeGlobalConfigGenerator.cs` | Merge-aware generator for `~/.claude.json` |
| Modify | `src/McpManager.Core/Services/RegistryService.cs` | Add default path helper |
| Modify | `src/McpManager.Core/Services/ConfigExportService.cs` | Wire up generator for export + preview |
| Modify | `src/McpManager/Views/NewTargetDialog.axaml` | Add RadioButton |
| Modify | `src/McpManager/Views/NewTargetDialog.axaml.cs` | Add disable logic + creation case |
| Modify | `src/McpManager/ViewModels/TargetFolderViewModel.cs` | Add `IsClaudeCodeGlobal` + `ConfigFilePath` |
| Modify | `src/McpManager/ViewModels/TargetIconConverter.cs` | Add icon + color cases |
| Modify | `src/McpManager/ViewModels/TargetBadgeConverter.cs` | Add badge text |
| Create | `tests/McpManager.Tests/ClaudeCodeGlobalConfigGeneratorTests.cs` | Tests for merge behavior |

---

### Task 1: Add `ClaudeCodeGlobal` to `TargetClientFlags`

**Files:**
- Modify: `src/McpManager.Core/Models/McpRegistry.cs:71-83`

- [ ] **Step 1: Add enum value and update All**

In `src/McpManager.Core/Models/McpRegistry.cs`, change:

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
  ClaudeCodeGlobal = 128,
  All = ClaudeCode | ClaudeDesktop | OpenCode | Codex | Cursor | Windsurf | VsCode | ClaudeCodeGlobal,
}
```

- [ ] **Step 2: Build to verify no compile errors**

Run: `dotnet build src/McpManager.Core/McpManager.Core.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/McpManager.Core/Models/McpRegistry.cs
git commit -m "feat: add ClaudeCodeGlobal to TargetClientFlags enum"
```

---

### Task 2: Write tests for `ClaudeCodeGlobalConfigGenerator`

**Files:**
- Create: `tests/McpManager.Tests/ClaudeCodeGlobalConfigGeneratorTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/McpManager.Tests/ClaudeCodeGlobalConfigGeneratorTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.ConfigGenerators;
using McpManager.Core.Models;

namespace McpManager.Tests;

public sealed class ClaudeCodeGlobalConfigGeneratorTests
{
  [Fact]
  public void GenerateConfig_MergesIntoExistingFile()
  {
    string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);
    string existingPath = Path.Combine(tempDir, ".claude.json");

    try
    {
      // Write existing file with other data
      File.WriteAllText(existingPath, JsonSerializer.Serialize(new JsonObject
      {
        ["numStartups"] = 42,
        ["autoUpdates"] = true,
        ["projects"] = new JsonObject(),
      }, new JsonSerializerOptions { WriteIndented = true }));

      ClaudeCodeGlobalConfigGenerator gen = new()
      {
        ExistingConfigPath = existingPath,
      };

      List<McpServer> servers =
      [
        new McpServer
        {
          Id = Guid.NewGuid(),
          Name = "test-server",
          TransportType = McpTransportType.Stdio,
          Command = "npx",
          Args = ["-y", "@test/server"],
          EnvironmentVariables = new Dictionary<string, string> { ["KEY"] = "val" },
        },
      ];

      string result = gen.GenerateConfig(servers);
      JsonObject root = JsonNode.Parse(result)!.AsObject();

      // Existing keys preserved
      Assert.Equal(42, root["numStartups"]!.GetValue<int>());
      Assert.True(root["autoUpdates"]!.GetValue<bool>());
      Assert.NotNull(root["projects"]);

      // mcpServers section generated
      JsonObject mcpServers = root["mcpServers"]!.AsObject();
      Assert.True(mcpServers.ContainsKey("test-server"));
      Assert.Equal("npx", mcpServers["test-server"]!["command"]!.GetValue<string>());
      Assert.Equal("val", mcpServers["test-server"]!["env"]!["KEY"]!.GetValue<string>());
    }
    finally
    {
      Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void GenerateConfig_NoExistingFile_CreatesValidOutput()
  {
    string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);
    string nonExistentPath = Path.Combine(tempDir, ".claude.json");

    try
    {
      ClaudeCodeGlobalConfigGenerator gen = new()
      {
        ExistingConfigPath = nonExistentPath,
      };

      List<McpServer> servers =
      [
        new McpServer
        {
          Id = Guid.NewGuid(),
          Name = "my-server",
          TransportType = McpTransportType.Http,
          Url = "https://example.com/mcp",
        },
      ];

      string result = gen.GenerateConfig(servers);
      JsonObject root = JsonNode.Parse(result)!.AsObject();

      JsonObject mcpServers = root["mcpServers"]!.AsObject();
      Assert.True(mcpServers.ContainsKey("my-server"));
      Assert.Equal("http", mcpServers["my-server"]!["type"]!.GetValue<string>());
      Assert.Equal("https://example.com/mcp", mcpServers["my-server"]!["url"]!.GetValue<string>());
    }
    finally
    {
      Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void GenerateConfig_ReplacesExistingMcpServers()
  {
    string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);
    string existingPath = Path.Combine(tempDir, ".claude.json");

    try
    {
      // Write file with old mcpServers
      File.WriteAllText(existingPath, JsonSerializer.Serialize(new JsonObject
      {
        ["mcpServers"] = new JsonObject
        {
          ["old-server"] = new JsonObject { ["command"] = "old" },
        },
      }, new JsonSerializerOptions { WriteIndented = true }));

      ClaudeCodeGlobalConfigGenerator gen = new()
      {
        ExistingConfigPath = existingPath,
      };

      List<McpServer> servers =
      [
        new McpServer
        {
          Id = Guid.NewGuid(),
          Name = "new-server",
          TransportType = McpTransportType.Stdio,
          Command = "new-cmd",
        },
      ];

      string result = gen.GenerateConfig(servers);
      JsonObject root = JsonNode.Parse(result)!.AsObject();
      JsonObject mcpServers = root["mcpServers"]!.AsObject();

      Assert.False(mcpServers.ContainsKey("old-server"));
      Assert.True(mcpServers.ContainsKey("new-server"));
    }
    finally
    {
      Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void HasCorrectMetadata()
  {
    ClaudeCodeGlobalConfigGenerator gen = new();
    Assert.Equal("Claude Code (Global)", gen.ClientName);
    Assert.Equal(".claude.json", gen.ConfigFileName);
    Assert.Null(gen.ConfigSubFolder);
  }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/McpManager.Tests/ --filter "ClaudeCodeGlobalConfigGeneratorTests" -v n`
Expected: Build error — `ClaudeCodeGlobalConfigGenerator` does not exist yet.

- [ ] **Step 3: Commit**

```bash
git add tests/McpManager.Tests/ClaudeCodeGlobalConfigGeneratorTests.cs
git commit -m "test: add failing tests for ClaudeCodeGlobalConfigGenerator"
```

---

### Task 3: Implement `ClaudeCodeGlobalConfigGenerator`

**Files:**
- Create: `src/McpManager.Core/ConfigGenerators/ClaudeCodeGlobalConfigGenerator.cs`

- [ ] **Step 1: Create the generator**

Create `src/McpManager.Core/ConfigGenerators/ClaudeCodeGlobalConfigGenerator.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;

namespace McpManager.Core.ConfigGenerators;

/// <summary>
/// Generates the mcpServers section in ~/.claude.json for Claude Code global config.
/// Merges into existing file, preserving all non-mcpServers data.
/// </summary>
public class ClaudeCodeGlobalConfigGenerator : ClaudeCodeConfigGenerator
{
  public override string ClientName => "Claude Code (Global)";
  public override string ConfigFileName => ".claude.json";

  /// <summary>
  /// Path to the existing ~/.claude.json file to merge with.
  /// </summary>
  public string? ExistingConfigPath { get; set; }

  public override string GenerateConfig(
    IEnumerable<McpServer> servers,
    Dictionary<Guid, Dictionary<string, string>>? envOverrides = null,
    Dictionary<Guid, List<string>>? toolOverrides = null,
    string? bridgeArgs = null)
  {
    // Generate the mcpServers JSON using parent logic
    string baseJson = base.GenerateConfig(servers, envOverrides, toolOverrides, bridgeArgs);
    JsonObject generated = JsonNode.Parse(baseJson)!.AsObject();
    JsonNode? mcpServers = generated["mcpServers"]?.DeepClone();

    // Load existing file or start fresh
    JsonObject root = LoadExistingConfig();

    // Replace only mcpServers key
    root["mcpServers"] = mcpServers;

    return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
  }

  private JsonObject LoadExistingConfig()
  {
    if (string.IsNullOrEmpty(ExistingConfigPath) || !File.Exists(ExistingConfigPath))
    {
      return new JsonObject();
    }

    try
    {
      string content = File.ReadAllText(ExistingConfigPath);
      JsonNode? node = JsonNode.Parse(content);
      return node as JsonObject ?? new JsonObject();
    }
    catch
    {
      return new JsonObject();
    }
  }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test tests/McpManager.Tests/ --filter "ClaudeCodeGlobalConfigGeneratorTests" -v n`
Expected: All 4 tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/McpManager.Core/ConfigGenerators/ClaudeCodeGlobalConfigGenerator.cs
git commit -m "feat: implement ClaudeCodeGlobalConfigGenerator with merge logic"
```

---

### Task 4: Add default path helper to `RegistryService`

**Files:**
- Modify: `src/McpManager.Core/Services/RegistryService.cs`

- [ ] **Step 1: Add `GetDefaultClaudeCodeGlobalConfigPath` method**

Add at the end of `RegistryService` class, after `GetDefaultOpenCodeConfigPath()`:

```csharp
  public static string GetDefaultClaudeCodeGlobalConfigPath()
  {
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".claude.json");
  }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/McpManager.Core/McpManager.Core.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/McpManager.Core/Services/RegistryService.cs
git commit -m "feat: add GetDefaultClaudeCodeGlobalConfigPath to RegistryService"
```

---

### Task 5: Wire up `ConfigExportService`

**Files:**
- Modify: `src/McpManager.Core/Services/ConfigExportService.cs`

- [ ] **Step 1: Add generator field**

Add after the `_vsCodeGen` field (line 14):

```csharp
  private readonly ClaudeCodeGlobalConfigGenerator _claudeCodeGlobalGen = new();
```

- [ ] **Step 2: Add export block in `ExportAsync`**

Add after the `VsCode` block (after line 80), before the closing brace:

```csharp
    if (target.EnabledClients.HasFlag(TargetClientFlags.ClaudeCodeGlobal))
    {
      string claudeCodeGlobalPath = RegistryService.GetDefaultClaudeCodeGlobalConfigPath();
      _claudeCodeGlobalGen.ExistingConfigPath = claudeCodeGlobalPath;
      string basePath = Path.GetDirectoryName(claudeCodeGlobalPath) ?? claudeCodeGlobalPath;
      await ExportConfigAsync(basePath, _claudeCodeGlobalGen, servers, envOverrides, toolOverrides, bridgeArgs);
    }
```

- [ ] **Step 3: Add preview block in `PreviewConfigs`**

Add after the `VsCode` preview block (after line 168), before `return result;`:

```csharp
    if (target.EnabledClients.HasFlag(TargetClientFlags.ClaudeCodeGlobal))
    {
      string claudeCodeGlobalPath = RegistryService.GetDefaultClaudeCodeGlobalConfigPath();
      _claudeCodeGlobalGen.ExistingConfigPath = claudeCodeGlobalPath;
      result[claudeCodeGlobalPath] = _claudeCodeGlobalGen.GenerateConfig(serverList, envOverrides, toolOverrides, bridgeArgs);
    }
```

- [ ] **Step 4: Add missing using**

Add at the top of the file if not present:

```csharp
using McpManager.Core.ConfigGenerators;
```

(Check if it's already there — ConfigGenerators namespace may already be used implicitly since the generator types are referenced.)

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/McpManager.Core/McpManager.Core.csproj`
Expected: Build succeeded

- [ ] **Step 6: Run all tests**

Run: `dotnet test tests/McpManager.Tests/ -v n`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/McpManager.Core/Services/ConfigExportService.cs
git commit -m "feat: wire ClaudeCodeGlobal into ConfigExportService export and preview"
```

---

### Task 6: Add RadioButton to `NewTargetDialog`

**Files:**
- Modify: `src/McpManager/Views/NewTargetDialog.axaml`
- Modify: `src/McpManager/Views/NewTargetDialog.axaml.cs`

- [ ] **Step 1: Add RadioButton to AXAML**

In `NewTargetDialog.axaml`, inside the `StackPanel` named `RadioPanel`, add before the `RbClaudeDesktop` line (before line 16):

```xml
      <RadioButton x:Name="RbClaudeCode" GroupName="TargetType" Content="Claude Code"
                   Tag="ClaudeCodeGlobal" IsChecked="True" />
```

And change `RbClaudeDesktop` to remove `IsChecked="True"` (line 17 after insert):

```xml
      <RadioButton x:Name="RbClaudeDesktop" GroupName="TargetType" Content="Claude Desktop"
                   Tag="ClaudeDesktop" />
```

- [ ] **Step 2: Add disable logic and creation case in code-behind**

In `NewTargetDialog.axaml.cs`, add at the start of `DisableExistingGlobalTypes()` (before the ClaudeDesktop check):

```csharp
    if (_existingGlobalTypes.Contains(TargetClientFlags.ClaudeCodeGlobal))
    {
      RbClaudeCode.IsEnabled = false;
      RbClaudeCode.Opacity = 0.4;
    }
```

Update the `buttons` arrays in `DisableExistingGlobalTypes()` and `GetSelectedTag()` to include `RbClaudeCode` first:

```csharp
    RadioButton[] buttons = [RbClaudeCode, RbClaudeDesktop, RbCodex, RbCursor, RbWindsurf, RbVsCode, RbOpenCode, RbFolder];
```

Add the switch case in `AddButton_OnClick`, before the `"ClaudeDesktop"` case:

```csharp
      "ClaudeCodeGlobal" => new TargetFolder
      {
        Name = "Claude Code",
        Path = System.IO.Path.GetDirectoryName(RegistryService.GetDefaultClaudeCodeGlobalConfigPath()) ?? "",
        EnabledClients = TargetClientFlags.ClaudeCodeGlobal,
        IsGlobal = true,
      },
```

- [ ] **Step 3: Build the UI project to verify**

Run: `dotnet build src/McpManager/McpManager.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/McpManager/Views/NewTargetDialog.axaml src/McpManager/Views/NewTargetDialog.axaml.cs
git commit -m "feat: add Claude Code global option to NewTargetDialog"
```

---

### Task 7: Update `TargetFolderViewModel`

**Files:**
- Modify: `src/McpManager/ViewModels/TargetFolderViewModel.cs`

- [ ] **Step 1: Add `IsClaudeCodeGlobal` property**

Add after the `IsOpenCode` property (after line 51):

```csharp
  public bool IsClaudeCodeGlobal => IsGlobal && _model.EnabledClients.HasFlag(TargetClientFlags.ClaudeCodeGlobal);
```

- [ ] **Step 2: Add `ConfigFilePath` case**

In the `ConfigFilePath` getter, add a case for `ClaudeCodeGlobal` before the existing `ClaudeCode` check (before the `if (clients.HasFlag(TargetClientFlags.ClaudeCode))` line):

```csharp
      if (clients.HasFlag(TargetClientFlags.ClaudeCodeGlobal))
        return System.IO.Path.Combine(Path, ".claude.json");
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/McpManager/McpManager.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/McpManager/ViewModels/TargetFolderViewModel.cs
git commit -m "feat: add IsClaudeCodeGlobal property and ConfigFilePath case"
```

---

### Task 8: Update converters

**Files:**
- Modify: `src/McpManager/ViewModels/TargetIconConverter.cs`
- Modify: `src/McpManager/ViewModels/TargetBadgeConverter.cs`

- [ ] **Step 1: Add icon case to `TargetIconConverter`**

In the `Convert` method's switch expression, add before the `IsGlobal` fallback case (before line 24):

```csharp
      TargetFolderViewModel target when target.IsClaudeCodeGlobal => "ClaudeLogo",
```

- [ ] **Step 2: Add color case to `TargetIconColorConverter`**

In the `Convert` method, add inside the `IsGlobal` block, before the final `return` that returns `#D97757` (before line 81):

```csharp
      if (target.IsClaudeCodeGlobal)
      {
        return new SolidColorBrush(Color.Parse("#D97757"));
      }
```

- [ ] **Step 3: Add badge case to `TargetBadgeConverter`**

In the `Convert` method, add before the `IsGlobal` fallback that returns `"DESKTOP"` (before line 50):

```csharp
      if (target.IsClaudeCodeGlobal)
      {
        return "CLAUDE CODE";
      }
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/McpManager/McpManager.csproj`
Expected: Build succeeded

- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/McpManager.Tests/ -v n`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/McpManager/ViewModels/TargetIconConverter.cs src/McpManager/ViewModels/TargetBadgeConverter.cs
git commit -m "feat: add Claude Code Global icon, color, and badge to converters"
```

---

### Task 9: Final verification

- [ ] **Step 1: Full build**

Run: `dotnet build McpManager.sln`
Expected: Build succeeded, 0 warnings related to our changes.

- [ ] **Step 2: Full test suite**

Run: `dotnet test McpManager.sln -v n`
Expected: All tests pass.

- [ ] **Step 3: Ask user to test the UI**

Ask user to:
1. Launch the app
2. Click "Add Target"
3. Verify "Claude Code" appears as the first (pre-selected) option
4. Add the Claude Code global target
5. Enable some servers and sync
6. Verify `~/.claude.json` has the `mcpServers` section merged with existing content
