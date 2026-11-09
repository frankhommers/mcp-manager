using McpManager.Core.Models;
using McpManager.Core.Services;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class SaveFlowTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-save-flow-tests-").FullName;
  private readonly McpServer _alpha = new()
  {
    Name = "alpha",
    Command = "alpha-command",
    EnvironmentVariables = { ["ZETA"] = "last", ["ALPHA"] = "first", ["MIDDLE"] = "between" },
  };
  private readonly McpServer _beta = new()
  {
    Name = "beta",
    Command = "beta-command",
    EnvironmentVariables = { ["TOKEN"] = "secret" },
  };

  private string WorkspacePath => Path.Combine(_directory, "workspace.json");

  private string ProjectA => Path.Combine(_directory, "project-a");

  private string ProjectB => Path.Combine(_directory, "project-b");

  [Fact]
  public async Task Freshly_loaded_workspace_is_clean_and_toggle_round_trip_stays_clean()
  {
    MainWindowViewModel vm = await CreateAsync();
    vm.CheckTargetsCommand.Execute(null);
    Assert.False(vm.IsDirty);

    ServerSelectionViewModel cell = Place(vm, "A").ServerSelections.Single(s => s.ServerId == _alpha.Id);
    cell.IsEnabled = false;
    vm.CheckTargetsCommand.Execute(null);
    Assert.True(vm.IsDirty);
    cell.IsEnabled = true;
    vm.CheckTargetsCommand.Execute(null);
    Assert.False(vm.IsDirty);
  }

  [Fact]
  public async Task Editing_marks_dirty_and_resets_also_deploy()
  {
    MainWindowViewModel vm = await CreateAsync();
    vm.Servers.Single(s => s.Id == _alpha.Id).Command = "changed-command";
    vm.CheckTargetsCommand.Execute(null);
    Assert.True(vm.IsDirty);
    Assert.Equal(1, vm.ChangedFileCount);
    Assert.Equal("Also deploy (1 file)", vm.AlsoDeployText);
    vm.AlsoDeploy = true;

    vm.CancelCommand.Execute(null);
    Assert.False(vm.IsDirty);
    vm.Servers.Single(s => s.Id == _alpha.Id).Command = "edited-again";
    vm.CheckTargetsCommand.Execute(null);

    Assert.True(vm.IsDirty);
    Assert.False(vm.AlsoDeploy);
  }

  [Fact]
  public async Task Change_that_affects_no_place_cannot_deploy()
  {
    MainWindowViewModel vm = await CreateAsync();
    vm.Servers.Single(s => s.Id == _alpha.Id).Description = "only a note";
    vm.CheckTargetsCommand.Execute(null);
    Assert.True(vm.IsDirty);
    Assert.Equal(0, vm.ChangedFileCount);
    Assert.Equal("Also deploy (no files change)", vm.AlsoDeployText);
    Assert.False(vm.CanAlsoDeploy);
  }

  [Fact]
  public async Task Save_without_deploy_writes_workspace_only()
  {
    MainWindowViewModel vm = await CreateAsync();
    vm.Servers.Single(s => s.Id == _alpha.Id).Command = "changed-command";
    vm.CheckTargetsCommand.Execute(null);

    await vm.SaveCommand.ExecuteAsync(null);

    Assert.False(vm.IsDirty);
    McpRegistry saved = await new RegistryService(WorkspacePath).LoadAsync();
    Assert.Equal("changed-command", saved.Servers.Single(s => s.Id == _alpha.Id).Command);
    Assert.False(File.Exists(Path.Combine(ProjectA, ".mcp.json")));
    Assert.False(File.Exists(Path.Combine(ProjectB, ".mcp.json")));
    Assert.Equal(["project-a", "project-b", "workspace.json"],
      Directory.GetFileSystemEntries(_directory).Select(p => Path.GetRelativePath(_directory, p)).Order().ToArray());
  }

  [Fact]
  public async Task Save_with_deploy_writes_affected_place_files()
  {
    MainWindowViewModel vm = await CreateAsync();
    vm.Servers.Single(s => s.Id == _alpha.Id).Command = "changed-command";
    vm.CheckTargetsCommand.Execute(null);
    vm.AlsoDeploy = true;

    await vm.SaveCommand.ExecuteAsync(null);

    Assert.False(vm.IsDirty);
    Assert.Contains("changed-command", await File.ReadAllTextAsync(Path.Combine(ProjectA, ".mcp.json")));
    Assert.False(File.Exists(Path.Combine(ProjectB, ".mcp.json")));
    McpRegistry saved = await new RegistryService(WorkspacePath).LoadAsync();
    Assert.NotNull(saved.TargetFolders.Single(t => t.Name == "A").LastAppliedAt);
    Assert.Null(saved.TargetFolders.Single(t => t.Name == "B").LastAppliedAt);
    Assert.Equal(0, vm.BehindCount);
  }

  [Fact]
  public async Task Cancel_restores_saved_state()
  {
    MainWindowViewModel vm = await CreateAsync();
    vm.SelectedServer = vm.Servers.Single(s => s.Id == _alpha.Id);
    vm.SelectedServer.Command = "changed-command";
    Place(vm, "B").ServerSelections.Single(s => s.ServerId == _alpha.Id).IsEnabled = true;
    vm.CheckTargetsCommand.Execute(null);
    Assert.True(vm.IsDirty);

    vm.CancelCommand.Execute(null);

    Assert.False(vm.IsDirty);
    Assert.Equal("alpha-command", vm.Servers.Single(s => s.Id == _alpha.Id).Command);
    Assert.False(Place(vm, "B").ServerSelections.Single(s => s.ServerId == _alpha.Id).IsEnabled);
    Assert.Equal(_alpha.Id, vm.SelectedServer?.Id);
    Assert.Same(vm.Servers.Single(s => s.Id == _alpha.Id), vm.SelectedServer);
    Assert.Contains(vm.Matrix.Rows, r => r.Server == vm.SelectedServer);
  }

  [Fact]
  public async Task Invalid_stdio_server_blocks_save()
  {
    MainWindowViewModel vm = await CreateAsync();
    vm.Servers.Single(s => s.Id == _alpha.Id).Command = "";

    await vm.SaveCommand.ExecuteAsync(null);

    Assert.Equal("Server 'alpha' needs a command.", vm.StatusMessage);
    Assert.True(vm.IsDirty);
    McpRegistry saved = await new RegistryService(WorkspacePath).LoadAsync();
    Assert.Equal("alpha-command", saved.Servers.Single(s => s.Id == _alpha.Id).Command);
  }

  [Fact]
  public async Task Stay_keeps_selection()
  {
    MainWindowViewModel vm = await CreateAsync();
    Task pending = Task.CompletedTask;
    vm.ScheduleNavigationGuard = guard => pending = guard();
    vm.PromptUnsavedAsync = () => Task.FromResult(UnsavedChoice.Stay);
    McpServerViewModel alpha = vm.Servers.Single(s => s.Id == _alpha.Id);
    vm.SelectedServer = alpha;
    await pending;
    alpha.Command = "changed-command";

    vm.SelectedServer = vm.Servers.Single(s => s.Id == _beta.Id);
    await pending;

    Assert.Same(alpha, vm.SelectedServer);
    Assert.Equal("changed-command", alpha.Command);
    Assert.True(vm.IsDirty);
  }

  [Fact]
  public async Task Confirm_leave_saves_or_discards_on_request()
  {
    MainWindowViewModel vm = await CreateAsync();
    Assert.True(await vm.ConfirmLeaveAsync());

    vm.Servers.Single(s => s.Id == _alpha.Id).Command = "saved-command";
    vm.PromptUnsavedAsync = () => Task.FromResult(UnsavedChoice.Save);
    Assert.True(await vm.ConfirmLeaveAsync());
    Assert.Equal("saved-command",
      (await new RegistryService(WorkspacePath).LoadAsync()).Servers.Single(s => s.Id == _alpha.Id).Command);

    vm.Servers.Single(s => s.Id == _alpha.Id).Command = "discarded-command";
    vm.PromptUnsavedAsync = () => Task.FromResult(UnsavedChoice.Discard);
    Assert.True(await vm.ConfirmLeaveAsync());
    Assert.Equal("saved-command", vm.Servers.Single(s => s.Id == _alpha.Id).Command);
    Assert.False(vm.IsDirty);

    vm.Servers.Single(s => s.Id == _alpha.Id).Command = "";
    vm.PromptUnsavedAsync = () => Task.FromResult(UnsavedChoice.Save);
    Assert.False(await vm.ConfirmLeaveAsync());
    Assert.Equal("Server 'alpha' needs a command.", vm.StatusMessage);
  }

  [Fact]
  public async Task Never_deployed_place_not_counted_in_behind()
  {
    MainWindowViewModel vm = await CreateAsync();
    vm.CheckTargetsCommand.Execute(null);
    Assert.Equal(0, vm.BehindCount);
    Assert.False(vm.ShowDeployBar);

    await vm.DeployPlaceCommand.ExecuteAsync(Place(vm, "A"));
    Assert.True(File.Exists(Path.Combine(ProjectA, ".mcp.json")));
    vm.Servers.Single(s => s.Id == _alpha.Id).Command = "changed-command";
    await vm.SaveCommand.ExecuteAsync(null);

    Assert.Equal(1, vm.BehindCount);
    Assert.True(vm.ShowDeployBar);
    Assert.Equal("1 place not deployed", vm.DeployBarText);
    Assert.DoesNotContain("changed-command", await File.ReadAllTextAsync(Path.Combine(ProjectA, ".mcp.json")));

    await vm.DeployNowCommand.ExecuteAsync(null);

    Assert.Equal(0, vm.BehindCount);
    Assert.Contains("changed-command", await File.ReadAllTextAsync(Path.Combine(ProjectA, ".mcp.json")));
    Assert.False(File.Exists(Path.Combine(ProjectB, ".mcp.json")));
  }

  [Fact]
  public async Task Deploy_now_refuses_while_dirty()
  {
    MainWindowViewModel vm = await CreateAsync();
    await vm.DeployPlaceCommand.ExecuteAsync(Place(vm, "A"));
    string deployed = await File.ReadAllTextAsync(Path.Combine(ProjectA, ".mcp.json"));
    vm.Servers.Single(s => s.Id == _alpha.Id).Command = "unsaved-command";

    await vm.DeployPlaceCommand.ExecuteAsync(Place(vm, "A"));

    Assert.Equal("Save or cancel your changes before deploying.", vm.StatusMessage);
    Assert.Equal(deployed, await File.ReadAllTextAsync(Path.Combine(ProjectA, ".mcp.json")));
  }

  private static TargetFolderViewModel Place(MainWindowViewModel vm, string name) =>
    vm.TargetFolders.Single(t => t.Name == name);

  private async Task<MainWindowViewModel> CreateAsync()
  {
    Directory.CreateDirectory(ProjectA);
    Directory.CreateDirectory(ProjectB);
    RegistryService registryService = new(WorkspacePath);
    await registryService.SaveAsync(new McpRegistry
    {
      Servers = [_alpha, _beta],
      TargetFolders =
      [
        new TargetFolder { Name = "A", Path = ProjectA, EnabledServers = [_beta.Id, _alpha.Id] },
        new TargetFolder { Name = "B", Path = ProjectB, EnabledServers = [_beta.Id] },
      ],
      Settings = new GlobalSettings { CodexConfigPath = Path.Combine(_directory, "global.toml") },
    });
    MainWindowViewModel vm = new(registryService, new ConfigExportService(), new ConfigImportService(),
      new HttpMcpTester(), new StdioMcpTester(), new TransportDetectionService(), new ShellStub());
    await vm.InitializeAsync();
    return vm;
  }

  private sealed class ShellStub : IShellEnvironmentService
  {
    public string? ResolvedPath => null;

    public Task ResolveAsync() => Task.CompletedTask;
  }

  public void Dispose() => Directory.Delete(_directory, recursive: true);
}
