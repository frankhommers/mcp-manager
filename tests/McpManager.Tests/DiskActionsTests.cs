using McpManager.Core.Models;
using McpManager.Core.Services;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class DiskActionsTests : IDisposable
{
  private readonly string _tmp = Directory.CreateTempSubdirectory("mcp-disk-actions-").FullName;
  private readonly McpServer _alpha = new() { Name = "alpha", Command = "node" };
  private readonly McpServer _gamma = new() { Name = "gamma", Command = "gamma-command" };

  private string Root => Path.Combine(_tmp, "root");

  private string Home => Path.Combine(_tmp, "home");

  private string App => Path.Combine(Root, "app");

  [Fact]
  public async Task Startup_refresh_lists_found_folder_in_choices()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "node" } } }""");
    MainWindowViewModel vm = await CreateAsync();

    await vm.RefreshDiskCommand.ExecuteAsync(null);

    Assert.Contains(vm.Matrix.Choices, c => c.Kind == ProjectChoiceKind.Found && c.Path == App);
  }

  [Fact]
  public async Task Editing_candidate_promotes_it_and_marks_dirty()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "node" } } }""");
    MainWindowViewModel vm = await CreateAsync();
    vm.Matrix.SelectedChoice = vm.Matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found);

    vm.Matrix.Rows.Single(r => r.Server.Id == _gamma.Id).Cells[^1].Selection.IsEnabled = true;
    await vm.PendingDiskWork;

    TargetFolderViewModel place = vm.TargetFolders.Single(t => t.Path == App);
    Assert.Contains(_gamma.Id, place.Model.EnabledServers);
    Assert.Contains(_alpha.Id, place.Model.EnabledServers);
    Assert.Equal(ProjectChoiceKind.Project, vm.Matrix.SelectedChoice?.Kind);
    Assert.Equal(place.Id, vm.Matrix.SelectedChoice?.Column?.Place.Id);
    Assert.True(vm.IsDirty);
    Assert.Equal("Added project app. Save to keep it.", vm.StatusMessage);
  }

  [Fact]
  public async Task Add_unknown_to_library_adds_server_and_row_disappears_after_rematch()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "node" }, "beta": { "command": "beta-command" } } }""");
    MainWindowViewModel vm = await CreateAsync();
    vm.Matrix.SelectedChoice = vm.Matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found);
    UnknownServerRowViewModel row = Assert.Single(vm.Matrix.UnknownRows);
    Assert.Equal("beta", row.Name);

    vm.AddUnknownToLibraryCommand.Execute(row);
    await vm.PendingDiskWork;

    Assert.Contains(vm.Servers, s => s.Name == "beta");
    Assert.Empty(vm.Matrix.UnknownRows);
    Assert.Equal(ProjectChoiceKind.Found, vm.Matrix.SelectedChoice?.Kind);
    Assert.True(vm.IsDirty);
    Assert.Equal("Added beta to your library. Save to keep it.", vm.StatusMessage);
  }

  [Fact]
  public async Task Resolving_a_difference_with_found_version_updates_library_and_promotes_folder()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "deno" } } }""");
    MainWindowViewModel vm = await CreateAsync();
    vm.Matrix.SelectedChoice = vm.Matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found);
    MatrixCellViewModel cell = vm.Matrix.Rows.Single(r => r.Server.Id == _alpha.Id).Cells[^1];
    Assert.True(cell.HasDifference);
    DiffViewModel? shown = null;
    vm.PromptResolveDifferenceAsync = diff =>
    {
      shown = diff;
      return Task.FromResult<DiffChoice?>(DiffChoice.UseFoundVersion);
    };

    await vm.ResolveDifferenceCommand.ExecuteAsync(cell);
    await vm.PendingDiskWork;

    Assert.Equal("alpha · app", shown?.Current?.ServerName);
    Assert.Equal("deno", vm.Servers.Single(s => s.Id == _alpha.Id).Command);
    TargetFolderViewModel place = vm.TargetFolders.Single(t => t.Path == App);
    Assert.Contains(_alpha.Id, place.Model.EnabledServers);
    Assert.Equal(place.Id, vm.Matrix.SelectedChoice?.Column?.Place.Id);
    Assert.False(vm.Matrix.Rows.Single(r => r.Server.Id == _alpha.Id).Cells[^1].HasDifference);
    Assert.Equal("Updated alpha. Save to keep it.", vm.StatusMessage);
  }

  [Fact]
  public async Task Rematch_during_a_refresh_keeps_the_loaded_folders()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "node" }, "beta": { "command": "beta-command" } } }""");
    GatedPlanner planner = new();
    MainWindowViewModel vm = await CreateAsync(planner);
    vm.Matrix.SelectedChoice = vm.Matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found);
    string other = Path.Combine(Root, "other");
    Directory.CreateDirectory(other);
    File.WriteAllText(Path.Combine(other, ".mcp.json"), """{ "mcpServers": { "alpha": { "command": "node" } } }""");
    planner.Gate.Reset();

    Task refresh = vm.RefreshDiskCommand.ExecuteAsync(null);
    vm.AddUnknownToLibraryCommand.Execute(Assert.Single(vm.Matrix.UnknownRows));
    Assert.False(refresh.IsCompleted);
    planner.Gate.Set();
    await refresh;
    await vm.PendingDiskWork;

    Assert.Contains(vm.Matrix.Choices, c => c.Kind == ProjectChoiceKind.Found && c.Path == other);
    Assert.Equal(App, vm.Matrix.SelectedChoice?.Path);
    Assert.Empty(vm.Matrix.UnknownRows);
    Assert.Contains(vm.Servers, s => s.Name == "beta");
  }

  [Fact]
  public async Task Failed_resolve_leaves_the_registry_unchanged()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "deno" } } }""");
    Directory.CreateDirectory(Path.Combine(App, ".cursor"));
    File.WriteAllText(
      Path.Combine(App, ".cursor", "mcp.json"), """{ "mcpServers": { "alpha": { "command": "node" } } }""");
    MainWindowViewModel vm = await CreateAsync();
    vm.Matrix.SelectedChoice = vm.Matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found);
    int places = vm.TargetFolders.Count;
    vm.PromptResolveDifferenceAsync = _ => Task.FromResult<DiffChoice?>(DiffChoice.UseFoundVersion);

    await vm.ResolveDifferenceCommand.ExecuteAsync(
      vm.Matrix.Rows.Single(r => r.Server.Id == _alpha.Id).Cells[^1]);

    Assert.Contains("would be changed", vm.StatusMessage);
    Assert.Equal("node", vm.Servers.Single(s => s.Id == _alpha.Id).Model.Command);
    vm.CheckTargetsCommand.Execute(null);
    Assert.False(vm.IsDirty);
    Assert.Equal(places, vm.TargetFolders.Count);
    Assert.Equal(2, vm.Servers.Count);
  }

  private void WriteApp(string json)
  {
    Directory.CreateDirectory(App);
    File.WriteAllText(Path.Combine(App, ".mcp.json"), json);
  }

  private async Task<MainWindowViewModel> CreateAsync(ScanImportPlanner? diskPlanner = null)
  {
    Directory.CreateDirectory(Home);
    RegistryService registryService = new(Path.Combine(_tmp, "workspace.json"));
    await registryService.SaveAsync(new McpRegistry
    {
      Servers = [_alpha, _gamma],
      Settings = new GlobalSettings { ScanRoots = [Root], CodexConfigPath = Path.Combine(_tmp, "global.toml") },
    });
    ConfigExportService exportService = new();
    MainWindowViewModel vm = new(registryService, exportService, new ConfigImportService(),
      new HttpMcpTester(), new StdioMcpTester(), new TransportDetectionService(), new ShellStub(),
      diskService: new DiskSnapshotService(
        new ConfigScanService(exportService, _ => [], homeDirectory: Home),
        diskPlanner ?? new ScanImportPlanner(new ConfigImportService(), exportService)));
    await vm.InitializeAsync();
    await vm.PendingDiskWork;
    return vm;
  }

  private sealed class GatedPlanner() : ScanImportPlanner(new ConfigImportService(), new ConfigExportService())
  {
    public ManualResetEventSlim Gate { get; } = new(true);

    public override IReadOnlyList<ScanServerMatch> Match(IEnumerable<ScannedFolder> folders, McpRegistry registry,
      ICollection<string>? skipped = null)
    {
      Assert.True(Gate.Wait(TimeSpan.FromSeconds(10)));
      return base.Match(folders, registry, skipped);
    }
  }

  private sealed class ShellStub : IShellEnvironmentService
  {
    public string? ResolvedPath => null;

    public Task ResolveAsync() => Task.CompletedTask;
  }

  public void Dispose() => Directory.Delete(_tmp, recursive: true);
}
