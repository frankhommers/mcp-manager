using McpManager.Core.ConfigGenerators;
using McpManager.Core.Models;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class TargetFolderViewModelTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-target-viewmodel-").FullName;

  [Fact]
  public async Task Existing_ids_match_renamed_servers_and_manual_deselection_survives_reload_and_save()
  {
    McpServer server = CreateServer("old-name");
    await WriteClaudeConfigAsync([server]);
    server.Name = "new-name";
    TargetFolder model = CreateTarget();
    TargetFolderViewModel vm = new(model, [server]);
    await vm.RefreshExistingServersAsync();
    ServerSelectionViewModel selection = Assert.Single(vm.ServerSelections);
    Assert.True(selection.IsEnabled);

    selection.IsEnabled = false;
    await vm.RefreshExistingServersAsync();
    Assert.False(selection.IsEnabled);
    vm.UpdateModel();
    Assert.Empty(model.EnabledServers);
    Assert.Contains(server.Id, model.DisabledServers);

    TargetFolderViewModel reloaded = new(model, [server]);
    await reloaded.RefreshExistingServersAsync();
    Assert.False(Assert.Single(reloaded.ServerSelections).IsEnabled);
  }

  [Fact]
  public async Task Changing_formats_recomputes_union_and_preserves_manual_additions()
  {
    McpServer claude = CreateServer("claude");
    McpServer codex = CreateServer("codex");
    McpServer manual = CreateServer("manual");
    await WriteClaudeConfigAsync([claude]);
    Directory.CreateDirectory(Path.Combine(_directory, ".codex"));
    await File.WriteAllTextAsync(Path.Combine(_directory, ".codex", "config.toml"),
      new CodexConfigGenerator().GenerateConfig([codex]));
    TargetFolder model = CreateTarget();
    TargetFolderViewModel vm = new(model, [claude, codex, manual]);
    vm.ServerSelections.Single(s => s.ServerId == manual.Id).IsEnabled = true;
    await vm.RefreshExistingServersAsync();
    Assert.True(EnabledIds(vm).SetEquals([claude.Id, manual.Id]));

    vm.EnableCodex = true;
    await vm.RefreshExistingServersAsync();
    Assert.True(EnabledIds(vm).SetEquals([claude.Id, codex.Id, manual.Id]));
    vm.EnableClaudeCode = false;
    await vm.RefreshExistingServersAsync();
    Assert.True(EnabledIds(vm).SetEquals([codex.Id, manual.Id]));
    vm.UpdateModel();
    Assert.Equal(TargetClientFlags.Codex, model.EnabledClients);
    Assert.Equal(Path.Combine(_directory, ".codex", "config.toml"), vm.ConfigFilePath);
    Assert.True(new TargetFolderViewModel(model, [claude, codex, manual]).EnableCodex);
  }

  [Fact]
  public async Task Changing_quick_export_folder_drops_old_auto_selections_without_stale_refresh()
  {
    McpServer first = CreateServer("first");
    McpServer second = CreateServer("second");
    await WriteClaudeConfigAsync([first]);
    string other = Directory.CreateDirectory(Path.Combine(_directory, "other")).FullName;
    await File.WriteAllTextAsync(Path.Combine(other, ".mcp.json"),
      new ClaudeCodeConfigGenerator().GenerateConfig([second]));
    TargetFolder model = CreateTarget();
    model.IsQuickExport = true;
    TargetFolderViewModel vm = new(model, [first, second]);
    await vm.RefreshExistingServersAsync();
    Assert.Equal(first.Id, Assert.Single(EnabledIds(vm)));
    Task oldRefresh = vm.RefreshExistingServersAsync(debounce: true);
    vm.Path = other;
    await vm.RefreshExistingServersAsync();
    await oldRefresh;
    Assert.Equal(second.Id, Assert.Single(EnabledIds(vm)));
  }

  [Fact]
  public async Task Unreadable_config_does_not_clear_preselected_servers()
  {
    McpServer server = CreateServer("existing");
    await WriteClaudeConfigAsync([server]);
    TargetFolderViewModel vm = new(CreateTarget(), [server]);
    await vm.RefreshExistingServersAsync();
    await File.WriteAllTextAsync(Path.Combine(_directory, ".mcp.json"), "{");
    await vm.RefreshExistingServersAsync();
    Assert.True(Assert.Single(vm.ServerSelections).IsEnabled);
    Assert.Contains("Could not read", vm.ExistingServersStatus);
  }

  [Fact]
  public async Task Library_refresh_preserves_new_server_defaults_and_detects_imported_ids()
  {
    McpServer existing = CreateServer("existing");
    await WriteClaudeConfigAsync([existing]);
    TargetFolder model = CreateTarget();
    TargetFolderViewModel vm = new(model, []);
    await vm.RefreshExistingServersAsync();
    Assert.Contains("missing from the library", vm.ExistingServersStatus);
    McpServer added = CreateServer("added");
    model.EnabledServers.Add(added.Id);
    vm.RefreshServers([existing, added]);
    Assert.True(EnabledIds(vm).SetEquals([existing.Id, added.Id]));
    await vm.RefreshExistingServersAsync();
    Assert.True(EnabledIds(vm).SetEquals([existing.Id, added.Id]));
    Assert.DoesNotContain("missing from the library", vm.ExistingServersStatus);
  }

  [Fact]
  public async Task Unmarked_same_name_is_not_adopted_or_auto_selected()
  {
    McpServer server = CreateServer("existing");
    await File.WriteAllTextAsync(Path.Combine(_directory, ".mcp.json"),
      "{\"mcpServers\":{\"existing\":{\"command\":\"manual-command\"}}}");
    TargetFolderViewModel vm = new(CreateTarget(), [server]);
    await vm.RefreshExistingServersAsync();
    Assert.False(Assert.Single(vm.ServerSelections).IsEnabled);
    Assert.Contains("Not owned by MCP Manager: 1 existing server(s)", vm.ExistingServersStatus);
  }

  [Fact]
  public async Task Read_status_tracks_the_latest_check_and_reports_missing_files()
  {
    TargetFolderViewModel vm = new(CreateTarget(), []);
    Assert.True(vm.CanReadConfig);
    Assert.Contains("not been checked", vm.ConfigReadStatus);

    Task olderRead = vm.RefreshExistingServersAsync(debounce: true);
    Assert.True(vm.IsReadingConfig);
    Assert.False(vm.CanReadConfig);
    Assert.Contains("Reading", vm.ConfigReadStatus);
    await vm.RefreshExistingServersAsync();
    string currentStatus = vm.ConfigReadStatus;
    Assert.False(vm.IsReadingConfig);
    Assert.True(vm.CanReadConfig);
    Assert.StartsWith("Last checked:", currentStatus);
    Assert.Contains("No configuration files found", vm.ExistingServersStatus);
    await olderRead;
    Assert.Equal(currentStatus, vm.ConfigReadStatus);
    Assert.False(vm.IsReadingConfig);

    vm.IsClipboard = true;
    Assert.False(vm.CanReadConfig);
  }

  [Fact]
  public async Task Config_check_uses_managed_server_ids_but_keeps_saved_inclusions_and_tool_choices()
  {
    McpServer existing = CreateServer("existing");
    existing.KnownTools = ["read", "write"];
    existing.AlwaysAllow = ["write"];
    McpServer saved = CreateServer("saved");
    TargetFolder model = CreateTarget();
    model.EnabledServers.Add(saved.Id);
    model.ServerToolOverrides[existing.Id] = ["read"];
    string config = new CodexConfigGenerator().GenerateConfig([existing]);
    model.EnabledClients = TargetClientFlags.Codex;
    string configDirectory = Directory.CreateDirectory(Path.Combine(_directory, ".codex")).FullName;
    string configPath = Path.Combine(configDirectory, "config.toml");
    await File.WriteAllTextAsync(configPath, config);
    TargetFolderViewModel vm = new(model, [existing, saved]);

    await vm.RefreshExistingServersAsync();

    Assert.True(EnabledIds(vm).SetEquals([existing.Id, saved.Id]));
    ServerSelectionViewModel selection = vm.ServerSelections.Single(s => s.ServerId == existing.Id);
    Assert.True(selection.ToolOverrides.Single(t => t.ToolName == "read").IsAllowed);
    Assert.False(selection.ToolOverrides.Single(t => t.ToolName == "write").IsAllowed);
    Assert.Equal(config, await File.ReadAllTextAsync(configPath));
  }

  private TargetFolder CreateTarget() => new()
  {
    Path = _directory,
    EnabledClients = TargetClientFlags.ClaudeCode,
  };

  private Task WriteClaudeConfigAsync(List<McpServer> servers) =>
    File.WriteAllTextAsync(Path.Combine(_directory, ".mcp.json"),
      new ClaudeCodeConfigGenerator().GenerateConfig(servers));

  private static McpServer CreateServer(string name) => new()
  {
    Name = name,
    Command = "test-mcp-server",
    TransportType = McpTransportType.Stdio,
  };

  private static HashSet<Guid> EnabledIds(TargetFolderViewModel vm) =>
    vm.ServerSelections.Where(s => s.IsEnabled).Select(s => s.ServerId).ToHashSet();

  public void Dispose() => Directory.Delete(_directory, recursive: true);
}
