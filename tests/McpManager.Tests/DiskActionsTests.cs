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
  public async Task Unreadable_config_file_is_announced_in_the_status()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "node" } } }""");
    File.WriteAllText(Path.Combine(App, "opencode.jsonc"), "{ not json");
    MainWindowViewModel vm = await CreateAsync();

    await vm.RefreshDiskCommand.ExecuteAsync(null);

    Assert.StartsWith("1 problem on disk: ", vm.StatusMessage);
    Assert.Contains("opencode.jsonc", vm.StatusMessage);
  }

  [Fact]
  public async Task Reload_after_save_keeps_the_save_status_when_problems_are_unchanged()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "node" } } }""");
    File.WriteAllText(Path.Combine(App, "opencode.jsonc"), "{ not json");
    MainWindowViewModel vm = await CreateAsync();
    Assert.StartsWith("1 problem on disk: ", vm.StatusMessage);

    await vm.SaveCommand.ExecuteAsync(null);
    await vm.PendingDiskWork;

    Assert.Equal("Saved.", vm.StatusMessage);
  }

  [Fact]
  public async Task New_problems_never_replace_a_deploy_failure()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "node" } } }""");
    MainWindowViewModel vm = await CreateAsync();
    vm.StatusMessage = "Deploy failed: boom";
    File.WriteAllText(Path.Combine(App, "opencode.jsonc"), "{ not json");

    await vm.RefreshDiskCommand.ExecuteAsync(null);

    Assert.Equal("Deploy failed: boom", vm.StatusMessage);
  }

  [Fact]
  public async Task Problems_status_clears_when_the_problems_are_gone()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "node" } } }""");
    File.WriteAllText(Path.Combine(App, "opencode.jsonc"), "{ not json");
    MainWindowViewModel vm = await CreateAsync();
    Assert.StartsWith("1 problem on disk: ", vm.StatusMessage);
    File.Delete(Path.Combine(App, "opencode.jsonc"));

    await vm.RefreshDiskCommand.ExecuteAsync(null);

    Assert.Equal(string.Empty, vm.StatusMessage);
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
    Assert.Equal("Added project app. Save to keep it.", vm.StatusMessage);
  }

  [Fact]
  public async Task Cancel_after_resolving_shows_the_difference_again()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "deno" } } }""");
    MainWindowViewModel vm = await CreateAsync();
    vm.Matrix.SelectedChoice = vm.Matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found);
    vm.PromptResolveDifferenceAsync = _ => Task.FromResult<DiffChoice?>(DiffChoice.UseFoundVersion);
    await vm.ResolveDifferenceCommand.ExecuteAsync(vm.Matrix.Rows.Single(r => r.Server.Id == _alpha.Id).Cells[^1]);
    await vm.PendingDiskWork;

    vm.CancelCommand.Execute(null);
    await vm.PendingDiskWork;
    vm.Matrix.SelectedChoice = vm.Matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found);

    Assert.Equal("node", vm.Servers.Single(s => s.Id == _alpha.Id).Command);
    Assert.True(vm.Matrix.Rows.Single(r => r.Server.Id == _alpha.Id).Cells[^1].HasDifference);
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

  [Fact]
  public async Task Resolving_on_an_existing_project_keeps_its_harnesses()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "deno" } } }""");
    Directory.CreateDirectory(Path.Combine(App, ".cursor"));
    File.WriteAllText(
      Path.Combine(App, ".cursor", "mcp.json"), """{ "mcpServers": { "beta": { "command": "beta-command" } } }""");
    TargetFolder project = new() { Name = "app", Path = App, EnabledClients = TargetClientFlags.ClaudeCode };
    MainWindowViewModel vm = await CreateAsync(place: project);
    Assert.Equal(project.Id, vm.Matrix.SelectedChoice?.Column?.Place.Id);
    Assert.Empty(vm.Matrix.UnknownRows);
    vm.PromptResolveDifferenceAsync = _ => Task.FromResult<DiffChoice?>(DiffChoice.KeepLibrary);

    await vm.ResolveDifferenceCommand.ExecuteAsync(
      vm.Matrix.Rows.Single(r => r.Server.Id == _alpha.Id).Cells[^1]);
    await vm.PendingDiskWork;

    TargetFolderViewModel place = vm.TargetFolders.Single(t => t.Path == App);
    Assert.Equal(TargetClientFlags.ClaudeCode, place.Model.EnabledClients);
    Assert.Contains(_alpha.Id, place.Model.EnabledServers);
    Assert.Equal("Updated alpha. Save to keep it.", vm.StatusMessage);
  }

  [Fact]
  public async Task Keeping_the_library_marks_the_difference_as_updated_on_deploy()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "deno" } } }""");
    TargetFolder project = new() { Name = "app", Path = App, EnabledClients = TargetClientFlags.ClaudeCode };
    MainWindowViewModel vm = await CreateAsync(place: project);
    Assert.True(vm.Matrix.Rows.Single(r => r.Server.Id == _alpha.Id).Cells[^1].NeedsChoice);
    vm.PromptResolveDifferenceAsync = _ => Task.FromResult<DiffChoice?>(DiffChoice.KeepLibrary);

    await vm.ResolveDifferenceCommand.ExecuteAsync(
      vm.Matrix.Rows.Single(r => r.Server.Id == _alpha.Id).Cells[^1]);
    await vm.PendingDiskWork;

    MatrixCellViewModel cell = vm.Matrix.Rows.Single(r => r.Server.Id == _alpha.Id).Cells[^1];
    Assert.True(cell.IsUpdatedOnDeploy);
    Assert.False(cell.NeedsChoice);
  }

  [Fact]
  public async Task Difference_dialog_explains_what_each_choice_does()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "deno" } } }""");
    string otherPath = Path.Combine(_tmp, "elsewhere");
    Directory.CreateDirectory(otherPath);
    TargetFolder project = new() { Name = "app", Path = App, EnabledClients = TargetClientFlags.ClaudeCode };
    TargetFolder other = new()
    {
      Name = "elsewhere", Path = otherPath, EnabledClients = TargetClientFlags.ClaudeCode, EnabledServers = [_alpha.Id],
    };
    MainWindowViewModel vm = await CreateAsync(place: project, otherPlaces: [other]);
    vm.Matrix.SelectedChoice = vm.Matrix.Choices.Single(c => c.Column?.Place.Id == project.Id);
    DiffViewModel? shown = null;
    vm.PromptResolveDifferenceAsync = diff =>
    {
      shown = diff;
      return Task.FromResult<DiffChoice?>(null);
    };

    await vm.ResolveDifferenceCommand.ExecuteAsync(vm.Matrix.Rows.Single(r => r.Server.Id == _alpha.Id).Cells[^1]);

    Dictionary<DiffChoice, string> texts = shown!.Options.ToDictionary(o => o.Choice, o => o.Description);
    Assert.Equal("Your library stays the same. On deploy, app gets the library version of alpha.",
      texts[DiffChoice.KeepLibrary]);
    Assert.Equal("Changes alpha in your library, also for 1 other place that uses it. "
      + "It gets this version on deploy.", texts[DiffChoice.UseFoundVersion]);
    Assert.Equal("Adds alpha-app to your library for app. On deploy, alpha in its config is renamed to alpha-app.",
      texts[DiffChoice.ImportSeparately]);
  }

  [Fact]
  public async Task Using_the_found_version_in_a_server_nobody_else_uses_says_so()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "deno" } } }""");
    MainWindowViewModel vm = await CreateAsync();
    vm.Matrix.SelectedChoice = vm.Matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found);
    DiffViewModel? shown = null;
    vm.PromptResolveDifferenceAsync = diff =>
    {
      shown = diff;
      return Task.FromResult<DiffChoice?>(null);
    };

    await vm.ResolveDifferenceCommand.ExecuteAsync(vm.Matrix.Rows.Single(r => r.Server.Id == _alpha.Id).Cells[^1]);

    Assert.Equal("Changes alpha in your library to this version.",
      shown!.Options.Single(o => o.Choice == DiffChoice.UseFoundVersion).Description);
  }

  [Fact]
  public async Task Add_to_library_only_reads_folders_that_contain_the_server()
  {
    WriteApp("""{ "mcpServers": { "alpha": { "command": "node" }, "beta": { "command": "beta-command" } } }""");
    string other = Path.Combine(Root, "other");
    Directory.CreateDirectory(other);
    File.WriteAllText(Path.Combine(other, ".mcp.json"), """{ "mcpServers": { "alpha": { "command": "node" } } }""");
    RecordingImportService imports = new();
    MainWindowViewModel vm = await CreateAsync(importService: imports);
    vm.Matrix.SelectedChoice = vm.Matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found && c.Path == App);
    imports.Sources.Clear();

    vm.AddUnknownToLibraryCommand.Execute(Assert.Single(vm.Matrix.UnknownRows));
    await vm.PendingDiskWork;

    Assert.Contains(vm.Servers, s => s.Name == "beta");
    Assert.DoesNotContain(imports.Sources, s => s.FilePath.StartsWith(other, StringComparison.Ordinal));
  }

  [Fact]
  public async Task Opening_another_folder_matches_it_in_the_background()
  {
    string other = Path.Combine(_tmp, "elsewhere");
    Directory.CreateDirectory(other);
    File.WriteAllText(Path.Combine(other, ".mcp.json"), """{ "mcpServers": { "alpha": { "command": "node" } } }""");
    GatedPlanner planner = new();
    MainWindowViewModel vm = await CreateAsync(planner);
    planner.Gate.Reset();
    try
    {
      Task open = vm.OpenFolderAsync(other);

      Assert.Same(open, await Task.WhenAny(open, Task.Delay(TimeSpan.FromSeconds(5))));
      Assert.False(vm.PendingDiskWork.IsCompleted);
      Assert.Equal(other, vm.Matrix.SelectedChoice?.Path);
    }
    finally
    {
      planner.Gate.Set();
    }

    await vm.PendingDiskWork;
    Assert.Equal(other, vm.Matrix.SelectedChoice?.Path);
    Assert.Contains(_alpha.Id, vm.Matrix.Columns[^1].Place.Model.EnabledServers);
  }

  private void WriteApp(string json)
  {
    Directory.CreateDirectory(App);
    File.WriteAllText(Path.Combine(App, ".mcp.json"), json);
  }

  private async Task<MainWindowViewModel> CreateAsync(
    ScanImportPlanner? diskPlanner = null, TargetFolder? place = null, IConfigImportService? importService = null,
    IReadOnlyList<TargetFolder>? otherPlaces = null)
  {
    Directory.CreateDirectory(Home);
    RegistryService registryService = new(Path.Combine(_tmp, "workspace.json"));
    await registryService.SaveAsync(new McpRegistry
    {
      Servers = [_alpha, _gamma],
      TargetFolders = [.. place == null ? [] : new[] { place }, .. otherPlaces ?? []],
      Settings = new GlobalSettings { ScanRoots = [Root], CodexConfigPath = Path.Combine(_tmp, "global.toml") },
    });
    ConfigExportService diskExportService = new();
    MainWindowViewModel vm = new(registryService, new ConfigExportService(), importService ?? new ConfigImportService(),
      new HttpMcpTester(), new StdioMcpTester(), new TransportDetectionService(), new ShellStub(),
      diskService: new DiskSnapshotService(
        new ConfigScanService(diskExportService, _ => [], homeDirectory: Home),
        diskPlanner ?? new ScanImportPlanner(new ConfigImportService(), diskExportService)));
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

  private sealed class RecordingImportService : IConfigImportService
  {
    private readonly ConfigImportService _inner = new();

    public List<ExistingTargetServer> Sources { get; } = [];

    public McpServer ImportServer(ExistingTargetServer source)
    {
      Sources.Add(source);
      return _inner.ImportServer(source);
    }

    public Task<List<McpServer>> ImportFromClaudeCodeAsync(string filePath) =>
      _inner.ImportFromClaudeCodeAsync(filePath);

    public Task<List<McpServer>> ImportFromOpenCodeAsync(string filePath) => _inner.ImportFromOpenCodeAsync(filePath);

    public Task<List<McpServer>> ImportFromCodexAsync(string filePath) => _inner.ImportFromCodexAsync(filePath);

    public Task<List<McpServer>> ImportAutoDetectAsync(string filePath) => _inner.ImportAutoDetectAsync(filePath);
  }

  private sealed class ShellStub : IShellEnvironmentService
  {
    public string? ResolvedPath => null;

    public Task ResolveAsync() => Task.CompletedTask;
  }

  public void Dispose() => Directory.Delete(_tmp, recursive: true);
}
