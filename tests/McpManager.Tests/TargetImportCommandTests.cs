using McpManager.Core.Models;
using McpManager.Core.Services;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class TargetImportCommandTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-target-import-command-").FullName;

  [Fact]
  public async Task Import_adds_one_server_selects_only_the_chosen_target_and_keeps_files_until_export()
  {
    string claudePath = Path.Combine(_directory, ".mcp.json");
    const string claudeConfig = "{\"mcpServers\":{\"tmux\":{\"command\":\"from-claude\"}}}";
    await File.WriteAllTextAsync(claudePath, claudeConfig);
    string codexDirectory = Directory.CreateDirectory(Path.Combine(_directory, ".codex")).FullName;
    string codexPath = Path.Combine(codexDirectory, "config.toml");
    const string codexConfig = "[mcp_servers.tmux]\ncommand = 'from-codex'\n";
    await File.WriteAllTextAsync(codexPath, codexConfig);
    (MainWindowViewModel vm, RegistryService registryService, TargetFolderViewModel target) = await CreateViewModelAsync();
    target.EnableCodex = true;
    await target.RefreshExistingServersAsync();
    Assert.Equal(2, target.FoundServers.Count);
    FoundTargetServerViewModel found = target.FoundServers.Single(s => s.Source.Client == TargetClientFlags.Codex);
    target.ServerSearchText = "no matches";

    await vm.ImportFoundServerCommand.ExecuteAsync(found);

    McpServerViewModel imported = Assert.Single(vm.Servers);
    Assert.Equal("tmux", imported.Name);
    Assert.Equal("from-codex", imported.Command);
    Assert.True(Assert.Single(target.ServerSelections).IsEnabled);
    Assert.Empty(target.FoundServers);
    Assert.Empty(target.ServerSearchText);
    Assert.All(vm.TargetFolders.Where(t => t != target), t => Assert.False(Assert.Single(t.ServerSelections).IsEnabled));
    Assert.Equal(claudeConfig, await File.ReadAllTextAsync(claudePath));
    Assert.Equal(codexConfig, await File.ReadAllTextAsync(codexPath));
    Assert.Empty((await registryService.LoadAsync()).Servers);

    await vm.ImportFoundServerCommand.ExecuteAsync(found);
    Assert.Single(vm.Servers);
    await vm.SaveCommand.ExecuteAsync(null);
    McpRegistry saved = await registryService.LoadAsync();
    Assert.Equal(imported.Id, Assert.Single(saved.Servers).Id);
    Assert.Contains(imported.Id, saved.TargetFolders.Single(t => t.Id == target.Id).EnabledServers);
    Assert.Equal(claudeConfig, await File.ReadAllTextAsync(claudePath));
    Assert.Equal(codexConfig, await File.ReadAllTextAsync(codexPath));
  }

  [Fact]
  public async Task Changed_source_is_refreshed_before_importing_and_requires_the_current_row()
  {
    string path = Path.Combine(_directory, ".mcp.json");
    const string original = "{\"mcpServers\":{\"tmux\":{\"command\":\"old-command\"}}}";
    await File.WriteAllTextAsync(path, original);
    (MainWindowViewModel vm, _, TargetFolderViewModel target) = await CreateViewModelAsync();
    FoundTargetServerViewModel oldRow = Assert.Single(target.FoundServers);
    string changed = original.Replace("old-command", "new-command");
    await File.WriteAllTextAsync(path, changed);

    await vm.ImportFoundServerCommand.ExecuteAsync(oldRow);

    Assert.Empty(vm.Servers);
    Assert.Contains("changed", vm.StatusMessage);
    await vm.ImportFoundServerCommand.ExecuteAsync(Assert.Single(target.FoundServers));
    Assert.Equal("new-command", Assert.Single(vm.Servers).Command);
    Assert.Equal(changed, await File.ReadAllTextAsync(path));
  }

  [Fact]
  public async Task Invalid_server_import_keeps_the_library_and_source_untouched()
  {
    string path = Path.Combine(_directory, ".mcp.json");
    const string content = "{\"mcpServers\":{\"broken\":{\"type\":\"http\"}}}";
    await File.WriteAllTextAsync(path, content);
    (MainWindowViewModel vm, _, TargetFolderViewModel target) = await CreateViewModelAsync();

    await vm.ImportFoundServerCommand.ExecuteAsync(Assert.Single(target.FoundServers));

    Assert.Empty(vm.Servers);
    Assert.Contains("Import error", vm.StatusMessage);
    Assert.False(vm.IsLoading);
    Assert.Single(target.FoundServers);
    Assert.Equal(content, await File.ReadAllTextAsync(path));
  }

  [Fact]
  public async Task Re_read_config_command_picks_up_external_changes_without_writing_files()
  {
    (MainWindowViewModel vm, _, TargetFolderViewModel target) = await CreateViewModelAsync();
    Assert.Empty(target.FoundServers);
    const string content = "{\"mcpServers\":{\"new-server\":{\"command\":\"test-server\"}}}";
    string path = Path.Combine(_directory, ".mcp.json");
    await File.WriteAllTextAsync(path, content);
    Assert.Empty(target.FoundServers);

    await vm.RefreshTargetConfigCommand.ExecuteAsync(null);

    Assert.Equal("new-server", Assert.Single(target.FoundServers).Name);
    Assert.StartsWith("Last checked:", target.ConfigReadStatus);
    Assert.False(target.IsReadingConfig);
    Assert.Equal(content, await File.ReadAllTextAsync(path));
    Assert.Empty(vm.Servers);
  }

  private async Task<(MainWindowViewModel, RegistryService, TargetFolderViewModel)> CreateViewModelAsync()
  {
    RegistryService registryService = new(Path.Combine(_directory, "registry.json"));
    TargetFolder target = new() { Name = "Test project", Path = _directory };
    McpRegistry registry = new()
    {
      TargetFolders = [target],
      Settings = new GlobalSettings { CodexConfigPath = Path.Combine(_directory, "global.toml") },
    };
    await registryService.SaveAsync(registry);
    MainWindowViewModel vm = new(registryService, new ConfigExportService(), new ConfigImportService(),
      new HttpMcpTester(), new StdioMcpTester(), new TransportDetectionService(), new TestShellEnvironmentService());
    await vm.InitializeAsync();
    TargetFolderViewModel targetVm = vm.TargetFolders.Single(t => t.Id == target.Id);
    vm.SelectedTarget = targetVm;
    await targetVm.RefreshExistingServersAsync(registry.Settings);
    return (vm, registryService, targetVm);
  }

  public void Dispose() => Directory.Delete(_directory, recursive: true);

  private sealed class TestShellEnvironmentService : IShellEnvironmentService
  {
    public string? ResolvedPath => null;
    public Task ResolveAsync() => Task.CompletedTask;
  }
}
