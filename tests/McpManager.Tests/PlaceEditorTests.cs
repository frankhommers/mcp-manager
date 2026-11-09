using McpManager.Core.Models;
using McpManager.Core.Services;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class PlaceEditorTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-place-editor-tests-").FullName;
  private readonly McpServer _alpha = new() { Name = "alpha", Command = "alpha-command" };

  private string WorkspacePath => Path.Combine(_directory, "workspace.json");

  private string Project => Path.Combine(_directory, "project");

  private string ConfigFile => Path.Combine(Project, ".mcp.json");

  [Fact]
  public async Task Keep_file_accepts_disk_state_without_writing()
  {
    (MainWindowViewModel vm, string edited) = await CreateWithExternalChangeAsync();
    DiffViewModel? shown = null;
    vm.PromptExternalChangeAsync = diff =>
    {
      shown = diff;
      return Task.FromResult<DiffChoice?>(DiffChoice.KeepFile);
    };

    await vm.CompareExternalChangesCommand.ExecuteAsync(null);

    Assert.NotNull(shown);
    Assert.Equal("In file", shown.LeftLabel);
    Assert.Equal("MCP Manager will write", shown.RightLabel);
    Assert.Equal("alpha · .mcp.json", shown.Current?.ServerName);
    Assert.Equal(edited, await File.ReadAllTextAsync(ConfigFile));
    Assert.False(File.Exists(ConfigFile + ".mcp-manager.bak"));
    Assert.False(vm.IsDirty);
    Assert.False(Place(vm).HasExternalChanges);
    McpRegistry saved = await new RegistryService(WorkspacePath).LoadAsync();
    Assert.Equal(TargetSyncService.FileHash(ConfigFile),
      saved.TargetFolders.Single(t => t.Name == "A").AppliedFileHashes[Path.GetFullPath(ConfigFile)]);
  }

  [Fact]
  public async Task Overwrite_deploys_over_external_change()
  {
    (MainWindowViewModel vm, string edited) = await CreateWithExternalChangeAsync();
    vm.PromptExternalChangeAsync = _ => Task.FromResult<DiffChoice?>(DiffChoice.Overwrite);

    await vm.CompareExternalChangesCommand.ExecuteAsync(null);

    Assert.Contains("alpha-command", await File.ReadAllTextAsync(ConfigFile));
    Assert.Equal(edited, await File.ReadAllTextAsync(ConfigFile + ".mcp-manager.bak"));
    Assert.False(Place(vm).HasExternalChanges);
    Assert.False(vm.IsDirty);
  }

  [Fact]
  public async Task Closing_the_comparison_changes_nothing()
  {
    (MainWindowViewModel vm, string edited) = await CreateWithExternalChangeAsync();
    string workspace = await File.ReadAllTextAsync(WorkspacePath);
    vm.PromptExternalChangeAsync = _ => Task.FromResult<DiffChoice?>(null);

    await vm.CompareExternalChangesCommand.ExecuteAsync(null);

    Assert.Equal(edited, await File.ReadAllTextAsync(ConfigFile));
    Assert.Equal(workspace, await File.ReadAllTextAsync(WorkspacePath));
    Assert.True(Place(vm).HasExternalChanges);
  }

  [Fact]
  public async Task Keep_file_refuses_while_dirty()
  {
    (MainWindowViewModel vm, string edited) = await CreateWithExternalChangeAsync();
    string workspace = await File.ReadAllTextAsync(WorkspacePath);
    vm.Servers.Single().Command = "unsaved-command";

    await vm.KeepExternalFileCommand.ExecuteAsync(null);

    Assert.Equal("Save or cancel your changes before deploying.", vm.StatusMessage);
    Assert.Equal(edited, await File.ReadAllTextAsync(ConfigFile));
    Assert.Equal(workspace, await File.ReadAllTextAsync(WorkspacePath));
  }

  [Fact]
  public void Harness_checkboxes_keep_selected_client_and_config_files_in_sync()
  {
    Directory.CreateDirectory(Path.Combine(Project, ".opencode"));
    File.WriteAllText(Path.Combine(Project, ".opencode", "opencode.json"), "{}");
    TargetFolder model = new() { Name = "A", Path = Project, EnabledClients = TargetClientFlags.ClaudeCode };
    TargetFolderViewModel place = new(model, [_alpha]);
    MatrixViewModel matrix = new([], [place]);

    place.EnableOpenCode = true;

    Assert.Equal(TargetClientFlags.ClaudeCode | TargetClientFlags.OpenCode, place.SelectedClient);
    Assert.Equal(["CC", "OC"], matrix.Columns.Single().Chips);
    Assert.Equal(
      string.Join(Environment.NewLine, ConfigFile, Path.Combine(Project, ".opencode", "opencode.json")),
      place.ConfigFilePath);
    place.UpdateModel();
    Assert.Equal(TargetClientFlags.ClaudeCode | TargetClientFlags.OpenCode, model.EnabledClients);

    place.SelectedClient = TargetClientFlags.Codex;

    Assert.False(place.EnableClaudeCode);
    Assert.False(place.EnableOpenCode);
    Assert.True(place.EnableCodex);
    Assert.Equal(Path.Combine(Project, ".codex", "config.toml"), place.ConfigFilePath);
  }

  [Fact]
  public async Task Export_menu_opens_clipboard_and_quick_export_outside_places_list()
  {
    (MainWindowViewModel vm, _) = await CreateWithExternalChangeAsync();
    Assert.DoesNotContain(vm.SidebarTargets, t => !t.IsManagedTarget);

    vm.OpenClipboardExportCommand.Execute(null);
    Assert.True(vm.SelectedTarget?.IsClipboard);
    vm.OpenQuickExportCommand.Execute(null);
    Assert.True(vm.SelectedTarget?.IsQuickExport);
  }

  private static TargetFolderViewModel Place(MainWindowViewModel vm) => vm.TargetFolders.Single(t => t.Name == "A");

  private async Task<(MainWindowViewModel Vm, string Edited)> CreateWithExternalChangeAsync()
  {
    Directory.CreateDirectory(Project);
    RegistryService registryService = new(WorkspacePath);
    await registryService.SaveAsync(new McpRegistry
    {
      Servers = [_alpha],
      TargetFolders = [new TargetFolder { Name = "A", Path = Project, EnabledServers = [_alpha.Id] }],
      Settings = new GlobalSettings { CodexConfigPath = Path.Combine(_directory, "global.toml") },
    });
    MainWindowViewModel vm = new(registryService, new ConfigExportService(), new ConfigImportService(),
      new HttpMcpTester(), new StdioMcpTester(), new TransportDetectionService(), new ShellStub());
    await vm.InitializeAsync();
    await vm.DeployPlaceCommand.ExecuteAsync(Place(vm));
    string edited = (await File.ReadAllTextAsync(ConfigFile)).Replace("alpha-command", "manual-command");
    await File.WriteAllTextAsync(ConfigFile, edited);
    File.Delete(ConfigFile + ".mcp-manager.bak");
    vm.CheckTargetsCommand.Execute(null);
    Assert.True(Place(vm).HasExternalChanges);
    vm.SelectedTarget = Place(vm);
    return (vm, edited);
  }

  private sealed class ShellStub : IShellEnvironmentService
  {
    public string? ResolvedPath => null;

    public Task ResolveAsync() => Task.CompletedTask;
  }

  public void Dispose() => Directory.Delete(_directory, recursive: true);
}
