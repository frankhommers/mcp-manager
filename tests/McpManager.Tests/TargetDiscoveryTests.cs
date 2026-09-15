using System.Text.Json.Nodes;
using McpManager.Core.ConfigGenerators;
using McpManager.Core.Models;
using McpManager.Core.Services;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class TargetDiscoveryTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-target-discovery-").FullName;

  [Theory]
  [InlineData(TargetClientFlags.ClaudeCode)]
  [InlineData(TargetClientFlags.ClaudeDesktop)]
  [InlineData(TargetClientFlags.Cursor)]
  [InlineData(TargetClientFlags.Windsurf)]
  [InlineData(TargetClientFlags.VsCode)]
  [InlineData(TargetClientFlags.OpenCode)]
  [InlineData(TargetClientFlags.Codex)]
  public async Task Discovers_importable_servers_with_their_source_and_original_identity(TargetClientFlags client)
  {
    IConfigGenerator generator = client switch
    {
      TargetClientFlags.ClaudeDesktop => new ClaudeDesktopConfigGenerator(),
      TargetClientFlags.Cursor => new CursorConfigGenerator(),
      TargetClientFlags.Windsurf => new WindsurfConfigGenerator(),
      TargetClientFlags.VsCode => new VsCodeConfigGenerator(),
      TargetClientFlags.OpenCode => new OpenCodeConfigGenerator(),
      TargetClientFlags.Codex => new CodexConfigGenerator(),
      _ => new ClaudeCodeConfigGenerator(),
    };
    TargetFolder target = new()
    {
      Path = _directory,
      EnabledClients = client,
      IsGlobal = client is TargetClientFlags.Cursor or TargetClientFlags.Windsurf or TargetClientFlags.VsCode,
    };
    string path = new ConfigExportService().GetConfigFilePaths(target)[client];
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    McpServer server = new() { Name = "missing-server", Command = "test-command" };
    string content = generator.GenerateConfig([server]);
    await File.WriteAllTextAsync(path, content);
    TargetFolderViewModel vm = new(target, []);

    await vm.RefreshExistingServersAsync();

    FoundTargetServerViewModel found = Assert.Single(vm.FoundServers);
    Assert.Equal(path, found.FilePath);
    Assert.Equal(client, found.Source.Client);
    Assert.Equal(server.Id, found.Source.ManagedId);
    Assert.True(found.Source.IsEnabled);
    McpServer imported = new ConfigImportService().ImportServer(found.Source);
    Assert.Equal(server.Id, imported.Id);
    Assert.Equal(server.Command, imported.Command);
    Assert.Equal(content, await File.ReadAllTextAsync(path));
  }

  [Fact]
  public async Task Known_ids_and_names_are_excluded_and_disabled_missing_servers_are_labeled()
  {
    McpServer knownByName = new() { Name = "known-unowned" };
    McpServer knownById = new() { Name = "renamed-in-library" };
    McpServer missingOwned = new() { Name = "missing-owned", Command = "owned-command" };
    JsonNode root = JsonNode.Parse(new ClaudeCodeConfigGenerator().GenerateConfig([missingOwned]))!;
    root["mcpServers"]!["old-name"] = new JsonObject
    {
      ["command"] = "old-command",
      ["env"] = new JsonObject { [ManagedServerIdentity.EnvironmentKey] = knownById.Id.ToString() },
    };
    root["mcpServers"]!["known-unowned"] = new JsonObject { ["command"] = "known-command" };
    root["mcpServers"]!["new-unowned"] = new JsonObject { ["command"] = "new-command", ["disabled"] = true };
    string original = "// Existing configuration\n" + root.ToJsonString();
    string path = Path.Combine(_directory, ".mcp.json");
    await File.WriteAllTextAsync(path, original);
    TargetFolderViewModel vm = new(new TargetFolder { Path = _directory }, [knownByName, knownById]);

    await vm.RefreshExistingServersAsync();

    Assert.Equal(["missing-owned", "new-unowned"], vm.FoundServers.Select(s => s.Name));
    Assert.Equal(missingOwned.Id, vm.FoundServers[0].Source.ManagedId);
    Assert.False(vm.FoundServers[1].Source.IsEnabled);
    Assert.Contains("Not owned by MCP Manager", vm.FoundServers[1].Status);
    Assert.Contains("Disabled", vm.FoundServers[1].Status);
    Assert.False(vm.ServerSelections.Single(s => s.ServerId == knownByName.Id).IsEnabled);
    Assert.True(vm.ServerSelections.Single(s => s.ServerId == knownById.Id).IsEnabled);

    McpServer imported = new ConfigImportService().ImportServer(vm.FoundServers[1].Source);
    vm.RefreshServers([knownByName, knownById, imported]);
    vm.SelectImportedServer(imported.Id);
    Assert.Equal("missing-owned", Assert.Single(vm.FoundServers).Name);
    Assert.True(vm.ServerSelections.Single(s => s.ServerId == imported.Id).IsEnabled);
    Assert.Equal(original, await File.ReadAllTextAsync(path));
  }

  [Fact]
  public async Task Changing_destination_and_unreadable_files_remove_stale_import_candidates()
  {
    string path = Path.Combine(_directory, ".mcp.json");
    await File.WriteAllTextAsync(path, "{\"mcpServers\":{\"found\":{\"command\":\"test-command\"}}}");
    TargetFolderViewModel vm = new(new TargetFolder { Path = _directory }, []);
    await vm.RefreshExistingServersAsync();
    Assert.True(vm.HasFoundServers);

    vm.Path = Path.Combine(_directory, "other");
    await vm.RefreshExistingServersAsync();
    Assert.Empty(vm.FoundServers);
    vm.Path = _directory;
    await File.WriteAllTextAsync(path, "{");
    await vm.RefreshExistingServersAsync();
    Assert.Empty(vm.FoundServers);
    Assert.Contains("Could not read", vm.ExistingServersStatus);
  }

  public void Dispose() => Directory.Delete(_directory, recursive: true);
}
