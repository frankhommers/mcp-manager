using McpManager.Core.Models;
using McpManager.Core.Services;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class ProfileManagementTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-profile-tests-").FullName;

  [Fact]
  public void Profile_choices_remain_inherited_after_save_reload_and_reset()
  {
    McpServer server = new() { Name = "test", Command = "first", KnownTools = ["read", "write"], AlwaysAllow = ["read"] };
    List<McpServer> servers = [server];
    McpProfile profile = new() { Name = "Development", EnabledServers = [server.Id] };
    ProfileViewModel editor = new(profile, servers);
    TargetFolder model = new() { ProfileId = profile.Id };
    TargetFolderViewModel target = new(model, servers) { SelectedProfile = editor };
    target.UpdateModel();
    Assert.Empty(model.EnabledServers);
    Assert.Empty(model.ServerToolOverrides);
    Assert.True(target.ServerSelections[0].IsEnabled);
    editor.ServerSelections[0].IsEnabled = false;
    target.ApplyInheritance();
    Assert.False(target.ServerSelections[0].IsEnabled);
    target.ServerSelections[0].IsEnabled = true;
    target.ServerSelections[0].ToolOverrides.Single(t => t.ToolName == "write").IsAllowed = true;
    target.UpdateModel();
    TargetFolderViewModel reloaded = new(model, servers) { SelectedProfile = editor };
    Assert.True(reloaded.ServerSelections[0].IsEnabled);
    Assert.True(reloaded.ServerSelections[0].ToolOverrides.Single(t => t.ToolName == "write").IsAllowed);
    reloaded.ResetOverridesCommand.Execute(null);
    reloaded.UpdateModel();
    Assert.False(reloaded.ServerSelections[0].IsEnabled);
    Assert.Empty(model.ServerToolOverrides);
    Assert.Empty(model.EnabledServers);
  }

  [Fact]
  public void Target_tool_override_can_be_empty_and_does_not_change_profile_or_other_target()
  {
    McpServer server = new() { Name = "test", KnownTools = ["read", "write"] };
    McpProfile profile = new() { EnabledServers = [server.Id], ServerToolOverrides = { [server.Id] = ["read"] } };
    ProfileViewModel editor = new(profile, [server]);
    TargetFolder target = new() { ProfileId = profile.Id, ServerToolOverrides = { [server.Id] = [] } };
    TargetFolderViewModel vm = new(target, [server]) { SelectedProfile = editor };
    Assert.All(vm.ServerSelections[0].ToolOverrides, t => Assert.False(t.IsAllowed));
    Assert.Empty(ProfileResolver.Resolve(target, [profile]).ServerToolOverrides[server.Id]);
    Assert.Equal(["read"], profile.ServerToolOverrides[server.Id]);
    vm.ServerSelections[0].ResetToInheritedCommand.Execute(null);
    Assert.True(vm.ServerSelections[0].ToolOverrides.Single(t => t.ToolName == "read").IsAllowed);
  }

  [Fact]
  public async Task One_server_change_updates_two_clients_and_external_changes_block_the_batch()
  {
    McpServer server = new() { Name = "shared", Command = "first-command" };
    McpProfile profile = new() { EnabledServers = [server.Id] };
    TargetFolder first = new() { Name = "Claude project", Path = _directory, ProfileId = profile.Id };
    TargetFolder second = new() { Name = "Codex project", Path = _directory, ProfileId = profile.Id, EnabledClients = TargetClientFlags.Codex };
    (MainWindowViewModel vm, RegistryService registry) = await CreateAsync([server], [profile], [first, second]);
    await vm.ExportAllTargetsCommand.ExecuteAsync(null);
    Assert.DoesNotContain("failed", vm.StatusMessage);
    string claude = Path.Combine(_directory, ".mcp.json");
    string codex = Path.Combine(_directory, ".codex", "config.toml");
    Assert.Contains("first-command", await File.ReadAllTextAsync(claude));
    Assert.Contains("first-command", await File.ReadAllTextAsync(codex));
    vm.Servers[0].Command = "second-command";
    vm.CheckTargetsCommand.Execute(null);
    Assert.All(vm.ManagedTargets.Where(t => t.Model.ProfileId == profile.Id), t => Assert.True(t.NeedsUpdate));
    await vm.ExportAllTargetsCommand.ExecuteAsync(null);
    Assert.Contains("second-command", await File.ReadAllTextAsync(claude));
    Assert.Contains("second-command", await File.ReadAllTextAsync(codex));
    Assert.Contains("first-command", await File.ReadAllTextAsync(claude + ".mcp-manager.bak"));
    Assert.All((await registry.LoadAsync()).TargetFolders.Where(t => t.ProfileId == profile.Id), t => Assert.Empty(t.EnabledServers));
    string edited = (await File.ReadAllTextAsync(claude)).Replace("second-command", "manual-command");
    await File.WriteAllTextAsync(claude, edited);
    vm.Servers[0].Command = "third-command";
    await vm.ExportAllTargetsCommand.ExecuteAsync(null);
    Assert.Contains("External changes", vm.StatusMessage);
    Assert.Equal(edited, await File.ReadAllTextAsync(claude));
    Assert.Contains("second-command", await File.ReadAllTextAsync(codex));
    vm.SelectedTarget = vm.TargetFolders.Single(t => t.Id == first.Id);
    await vm.ReapplyTargetCommand.ExecuteAsync(null);
    Assert.Contains("third-command", await File.ReadAllTextAsync(claude));
    Assert.Equal(edited, await File.ReadAllTextAsync(claude + ".mcp-manager.bak"));
  }

  [Fact]
  public async Task Adding_a_server_to_profile_reaches_linked_targets_but_explicit_exclusion_wins()
  {
    McpServer server = new() { Name = "shared", Command = "test" };
    McpProfile profile = new();
    TargetFolder first = new() { Path = _directory, ProfileId = profile.Id };
    TargetFolder second = new() { Path = Path.Combine(_directory, "other"), ProfileId = profile.Id, DisabledServers = [server.Id] };
    (MainWindowViewModel vm, _) = await CreateAsync([server], [profile], [first, second]);
    vm.Profiles[0].ServerSelections[0].IsEnabled = true;
    vm.CheckTargetsCommand.Execute(null);
    Assert.True(vm.TargetFolders.Single(t => t.Id == first.Id).ServerSelections[0].IsEnabled);
    Assert.False(vm.TargetFolders.Single(t => t.Id == second.Id).ServerSelections[0].IsEnabled);
    await vm.ExportAllTargetsCommand.ExecuteAsync(null);
    Assert.Contains("shared", await File.ReadAllTextAsync(Path.Combine(_directory, ".mcp.json")));
    Assert.DoesNotContain("shared", await File.ReadAllTextAsync(Path.Combine(_directory, "other", ".mcp.json")));
  }

  [Fact]
  public async Task Duplicate_destinations_fail_before_writing_anything()
  {
    ConfigExportService exporter = new();
    await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.PrepareExportAsync(
      [new() { Path = _directory }, new() { Path = _directory }], [], null, _ => Task.FromResult<ExportConflictResolution?>(null)));
    Assert.Empty(Directory.GetFiles(_directory));
  }

  [Fact]
  public async Task Invalid_destination_stops_transaction_before_existing_files_change()
  {
    string existing = Path.Combine(_directory, "first.json");
    string blocker = Path.Combine(_directory, "file-not-directory");
    await File.WriteAllTextAsync(existing, "original");
    await File.WriteAllTextAsync(blocker, "blocker");
    await Assert.ThrowsAnyAsync<IOException>(() => new ConfigExportService().WriteConfigsAsync(
      new Dictionary<string, string> { [existing] = "updated", [Path.Combine(blocker, "second.json")] = "new" }));
    Assert.Equal("original", await File.ReadAllTextAsync(existing));
    Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
  }

  [Fact]
  public async Task Old_or_corrupt_registry_is_rejected_without_resetting_or_migrating_it()
  {
    string path = Path.Combine(_directory, "workspace.json");
    RegistryService service = new(path);
    const string old = "{\"servers\":[],\"targetFolders\":[]}";
    await File.WriteAllTextAsync(path, old);
    await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync());
    Assert.Equal(old, await File.ReadAllTextAsync(path));
    await File.WriteAllTextAsync(path, "{");
    await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => service.LoadAsync());
    Assert.Equal("{", await File.ReadAllTextAsync(path));
  }

  [Fact]
  public async Task Failed_second_write_restores_first_file_and_keeps_backup()
  {
    string first = Path.Combine(_directory, "first.json");
    string second = Path.Combine(_directory, "second.json");
    await File.WriteAllTextAsync(first, "first original");
    await File.WriteAllTextAsync(second, "second original");
    Directory.CreateDirectory(second + ".mcp-manager.bak");
    Exception? failure = await Record.ExceptionAsync(() => new ConfigExportService().WriteConfigsAsync(
      new Dictionary<string, string> { [first] = "changed", [second] = "changed" }));
    Assert.True(failure is IOException or UnauthorizedAccessException);
    Assert.Equal("first original", await File.ReadAllTextAsync(first));
    Assert.Equal("second original", await File.ReadAllTextAsync(second));
    Assert.Equal("first original", await File.ReadAllTextAsync(first + ".mcp-manager.bak"));
  }

  [Fact]
  public async Task Bulk_assignment_keeps_overrides_and_profile_deletion_is_blocked_while_linked()
  {
    McpServer server = new() { Name = "shared", Command = "test" };
    McpProfile profile = new() { EnabledServers = [server.Id] };
    TargetFolder target = new() { Path = _directory, DisabledServers = [server.Id] };
    (MainWindowViewModel vm, _) = await CreateAsync([server], [profile], [target]);
    TargetFolderViewModel selected = vm.TargetFolders.Single(t => t.Id == target.Id);
    selected.IsSelectedForBulk = true;
    vm.BulkProfile = vm.Profiles[0];
    vm.AssignProfileToSelectedCommand.Execute(null);
    Assert.Equal(profile.Id, selected.Model.ProfileId);
    Assert.False(selected.ServerSelections[0].IsEnabled);
    vm.SelectedProfile = vm.Profiles[0];
    vm.DeleteProfileCommand.Execute(null);
    Assert.Single(vm.Profiles);
    Assert.Contains("Detach", vm.StatusMessage);
  }

  [Fact]
  public async Task First_update_requires_importing_unknown_managed_entries()
  {
    McpServer existing = new() { Name = "existing", Command = "keep-me" };
    string path = Path.Combine(_directory, ".mcp.json");
    string config = new McpManager.Core.ConfigGenerators.ClaudeCodeConfigGenerator().GenerateConfig([existing]);
    await File.WriteAllTextAsync(path, config);
    (MainWindowViewModel vm, _) = await CreateAsync([], [], [new() { Path = _directory }]);
    await vm.ExportAllTargetsCommand.ExecuteAsync(null);
    Assert.Contains("Import the existing managed servers", vm.StatusMessage);
    Assert.Equal(config, await File.ReadAllTextAsync(path));
  }

  [Fact]
  public async Task First_update_keeps_known_managed_entries_already_in_the_config()
  {
    McpServer existing = new() { Name = "existing", Command = "keep-me" };
    string path = Path.Combine(_directory, ".mcp.json");
    await File.WriteAllTextAsync(path, new McpManager.Core.ConfigGenerators.ClaudeCodeConfigGenerator().GenerateConfig([existing]));
    TargetFolder target = new() { Name = "Legacy", Path = _directory };
    (MainWindowViewModel vm, _) = await CreateAsync([existing], [], [target]);
    await vm.ExportAllTargetsCommand.ExecuteAsync(null);
    Assert.Contains("keep-me", await File.ReadAllTextAsync(path));
    Assert.Contains(existing.Id, vm.TargetFolders.Single(t => t.Id == target.Id).Model.EnabledServers);
  }

  [Fact]
  public async Task Original_backup_survives_repeated_updates()
  {
    string path = Path.Combine(_directory, "config.json");
    await File.WriteAllTextAsync(path, "original");
    ConfigExportService exporter = new();
    await exporter.WriteConfigsAsync(new Dictionary<string, string> { [path] = "first" });
    await exporter.WriteConfigsAsync(new Dictionary<string, string> { [path] = "second" });
    Assert.Equal("original", await File.ReadAllTextAsync(path + ".mcp-manager.orig.bak"));
    Assert.Equal("first", await File.ReadAllTextAsync(path + ".mcp-manager.bak"));
  }

  [Fact]
  public async Task Updating_a_symlinked_config_writes_through_the_link()
  {
    if (OperatingSystem.IsWindows()) return;
    string real = Path.Combine(_directory, "real.json");
    string link = Path.Combine(_directory, "link.json");
    await File.WriteAllTextAsync(real, "original");
    File.CreateSymbolicLink(link, real);
    await new ConfigExportService().WriteConfigsAsync(new Dictionary<string, string> { [link] = "updated" });
    Assert.NotNull(new FileInfo(link).LinkTarget);
    Assert.Equal("updated", await File.ReadAllTextAsync(real));
  }

  [Fact]
  public async Task No_op_update_preserves_previous_backup()
  {
    string path = Path.Combine(_directory, "config.json");
    await File.WriteAllTextAsync(path, "original");
    ConfigExportService exporter = new();
    Dictionary<string, string> configs = new() { [path] = "updated" };
    await exporter.WriteConfigsAsync(configs);
    await exporter.WriteConfigsAsync(configs);
    Assert.Equal("updated", await File.ReadAllTextAsync(path));
    Assert.Equal("original", await File.ReadAllTextAsync(path + ".mcp-manager.bak"));
  }

  [Fact]
  public void Codex_empty_tool_override_is_exported_as_an_explicit_empty_list()
  {
    McpServer server = new() { Name = "test", Command = "test", AlwaysAllow = ["read"] };
    string config = new McpManager.Core.ConfigGenerators.CodexConfigGenerator()
      .GenerateConfig([server], toolOverrides: new() { [server.Id] = [] });
    Assert.Contains("enabled_tools = []", config);
    Assert.DoesNotContain("read", config);
  }

  private async Task<(MainWindowViewModel, RegistryService)> CreateAsync(
    List<McpServer> servers, List<McpProfile> profiles, List<TargetFolder> targets)
  {
    RegistryService service = new(Path.Combine(_directory, "workspace.json"));
    await service.SaveAsync(new McpRegistry
    {
      Servers = servers, Profiles = profiles, TargetFolders = targets,
      Settings = new() { CodexConfigPath = Path.Combine(_directory, "global.toml") },
    });
    MainWindowViewModel vm = new(service, new ConfigExportService(), new ConfigImportService(), new HttpMcpTester(),
      new StdioMcpTester(), new TransportDetectionService(), new ShellStub());
    await vm.InitializeAsync();
    return (vm, service);
  }

  private sealed class ShellStub : IShellEnvironmentService
  {
    public string? ResolvedPath => null;
    public Task ResolveAsync() => Task.CompletedTask;
  }

  public void Dispose() => Directory.Delete(_directory, true);
}
