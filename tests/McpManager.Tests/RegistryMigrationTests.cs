using System.Text.Json.Nodes;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class RegistryMigrationTests : IDisposable
{
  private readonly string _directory = Path.Combine(Path.GetTempPath(), "mcpmanager-migration-" + Guid.NewGuid());

  public RegistryMigrationTests() => Directory.CreateDirectory(_directory);

  public void Dispose() => Directory.Delete(_directory, true);

  private string WorkspacePath => Path.Combine(_directory, "workspace.json");
  private string LegacyPath => Path.Combine(_directory, "mcp-registry.json");

  private const string LegacyJson = """
    {
      "servers": [
        { "id": "2bf75a0f-f5e9-416c-99db-4730d2470a43", "name": "obsidian", "command": "npx", "args": ["-y", "pkg"] }
      ],
      "targetFolders": [
        { "id": "11111111-1111-1111-1111-111111111111", "name": "Repo", "path": "/tmp/repo",
          "enabledServers": ["2bf75a0f-f5e9-416c-99db-4730d2470a43"] }
      ],
      "settings": {
        "bridgeHeaderArgumentTemplate": "-H '{key}: {value}'",
        "autoSyncOnChange": true
      }
    }
    """;

  [Fact]
  public async Task Migrates_legacy_registry_when_workspace_is_missing()
  {
    await File.WriteAllTextAsync(LegacyPath, LegacyJson);

    McpRegistry registry = await new RegistryService(WorkspacePath).LoadAsync();

    Assert.Equal(2, registry.FormatVersion);
    Assert.Equal("obsidian", Assert.Single(registry.Servers).Name);
    TargetFolder target = Assert.Single(registry.TargetFolders);
    Assert.Null(target.ProfileId);
    Assert.Contains(Guid.Parse("2bf75a0f-f5e9-416c-99db-4730d2470a43"), target.EnabledServers);
    Assert.Equal("-H '{key}: {value}'", registry.Settings.BridgeHeaderArgumentTemplate);

    JsonNode saved = JsonNode.Parse(await File.ReadAllTextAsync(WorkspacePath))!;
    Assert.Equal(2, saved["formatVersion"]!.GetValue<int>());
    Assert.Equal(LegacyJson, await File.ReadAllTextAsync(LegacyPath));
  }

  [Fact]
  public async Task Migration_upgrades_old_bridge_command_placeholders()
  {
    await File.WriteAllTextAsync(LegacyPath, """
      { "settings": {
          "bridgeCommandHttp": "mcp-proxy {headers} {url}",
          "bridgeCommandSse": "npx -y mcp-remote {url}",
          "bridgeCommandStreamableHttp": "npx -y mcp-remote {args} {url} {headerArgs}" } }
      """);

    GlobalSettings settings = (await new RegistryService(WorkspacePath).LoadAsync()).Settings;

    Assert.Equal("mcp-proxy {headerArgs} {args} {url}", settings.BridgeCommandHttp);
    Assert.Equal("npx -y mcp-remote {args} {url}", settings.BridgeCommandSse);
    Assert.Equal("npx -y mcp-remote {args} {url} {headerArgs}", settings.BridgeCommandStreamableHttp);
  }

  [Fact]
  public async Task Prefers_existing_workspace_over_legacy_registry()
  {
    await File.WriteAllTextAsync(LegacyPath, LegacyJson);
    await File.WriteAllTextAsync(WorkspacePath, """{ "formatVersion": 2, "servers": [] }""");

    McpRegistry registry = await new RegistryService(WorkspacePath).LoadAsync();

    Assert.Empty(registry.Servers);
  }

  [Fact]
  public async Task Returns_empty_registry_when_no_files_exist()
  {
    McpRegistry registry = await new RegistryService(WorkspacePath).LoadAsync();

    Assert.Empty(registry.Servers);
    Assert.False(File.Exists(WorkspacePath));
  }
}
