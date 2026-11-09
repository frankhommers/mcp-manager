using McpManager.Core.Models;
using McpManager.Core.Services;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class ScanViewModelTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-scan-vm-").FullName;

  private string Root => Path.Combine(_directory, "repos");

  private string WorkspacePath => Path.Combine(_directory, "workspace.json");

  [Fact]
  public async Task Test_folders_unselected_by_default()
  {
    Write("app/.mcp.json", ClaudeJson("tool", "run"));
    Write("TestData/.mcp.json", ClaudeJson("tool", "run"));
    McpRegistry registry = Registry();
    ScanViewModel scan = CreateScan(registry);

    await scan.ScanCommand.ExecuteAsync(null);

    ScannedFolderViewModel app = Folder(scan, "app");
    Assert.True(app.IsNew);
    Assert.True(app.IsSelected);
    Assert.Equal("", app.Note);
    Assert.Equal(["CC"], app.ClientChips);
    Assert.Equal("1 unknown", app.Summary);
    ScannedFolderViewModel test = Folder(scan, "TestData");
    Assert.True(test.IsNew);
    Assert.False(test.IsSelected);
    Assert.Equal("Looks like test data", test.Note);
  }

  [Fact]
  public async Task Summary_counts_matches_and_reports_problems()
  {
    Write("app/.mcp.json",
      """{"mcpServers":{"same":{"command":"run"},"changed":{"command":"new"},"other":{"command":"x"}}}""");
    Write("app/opencode.jsonc", "{ not json");
    McpRegistry registry = Registry(new McpServer { Name = "same", Command = "run" },
      new McpServer { Name = "changed", Command = "old" });
    ScanViewModel scan = CreateScan(registry);

    await scan.ScanCommand.ExecuteAsync(null);

    Assert.Equal("1 recognized · 1 differs · 1 unknown · Unreadable: opencode.jsonc", Folder(scan, "app").Summary);
  }

  [Fact]
  public async Task Known_place_is_already_added_unless_it_gained_a_harness()
  {
    Write("known/.mcp.json", ClaudeJson("tool", "run"));
    Write("grown/.mcp.json", ClaudeJson("tool", "run"));
    Write("grown/.cursor/mcp.json", ClaudeJson("tool", "run"));
    McpRegistry registry = Registry();
    registry.TargetFolders.Add(new TargetFolder { Name = "known", Path = Path.Combine(Root, "KNOWN") });
    registry.TargetFolders.Add(new TargetFolder { Name = "grown", Path = Path.Combine(Root, "grown") });
    ScanViewModel scan = CreateScan(registry);

    await scan.ScanCommand.ExecuteAsync(null);

    ScannedFolderViewModel known = Folder(scan, "known");
    Assert.False(known.IsNew);
    Assert.False(known.IsSelected);
    Assert.Equal("Already added", known.Note);
    ScannedFolderViewModel grown = Folder(scan, "grown");
    Assert.False(grown.IsNew);
    Assert.True(grown.IsSelected);
    Assert.Equal("New harness found", grown.Note);
  }

  [Fact]
  public async Task Add_without_differences_creates_places_and_marks_dirty()
  {
    Write("app/.mcp.json", ClaudeJson("tool", "run"));
    string config = await File.ReadAllTextAsync(Path.Combine(Root, "app", ".mcp.json"));
    MainWindowViewModel vm = await CreateMainAsync();
    string workspace = await File.ReadAllTextAsync(WorkspacePath);
    vm.OpenScanCommand.Execute(null);
    Assert.Equal(NavigationPage.Scan, vm.CurrentPage);
    await vm.Scan.ScanCommand.ExecutionTask!;

    vm.Scan.AddSelectedCommand.Execute(null);

    Assert.Null(vm.Scan.Walkthrough);
    TargetFolderViewModel place = vm.TargetFolders.Single(t => t.Name == "app");
    McpServerViewModel server = vm.Servers.Single(s => s.Name == "tool");
    Assert.Contains(place.ServerSelections, s => s.ServerId == server.Id && s.IsEnabled);
    Assert.True(vm.IsDirty);
    Assert.Equal(NavigationPage.Overview, vm.CurrentPage);
    Assert.Equal("Added 1 place. Save to keep them.", vm.StatusMessage);
    Assert.Equal(workspace, await File.ReadAllTextAsync(WorkspacePath));
    Assert.Equal(config, await File.ReadAllTextAsync(Path.Combine(Root, "app", ".mcp.json")));
  }

  [Fact]
  public async Task Cancelled_walkthrough_changes_nothing()
  {
    Write("app/.mcp.json", ClaudeJson("tool", "new-command"));
    McpRegistry registry = Registry(new McpServer { Name = "tool", Command = "old-command" });
    string before = RegistryService.Serialize(registry);
    int applied = 0;
    ScanViewModel scan = CreateScan(registry, _ => applied++);
    await scan.ScanCommand.ExecuteAsync(null);

    scan.AddSelectedCommand.Execute(null);

    DiffViewModel walkthrough = Assert.IsType<DiffViewModel>(scan.Walkthrough);
    Assert.Equal("Library", walkthrough.LeftLabel);
    Assert.Equal("Found in project", walkthrough.RightLabel);
    Assert.Equal("tool · app/.mcp.json", walkthrough.Current!.ServerName);
    Assert.Equal(["Keep library", "Put this version in the library", "Import as separate server"],
      walkthrough.Options.Select(o => o.Label));
    walkthrough.SelectedChoice = DiffChoice.UseFoundVersion;

    scan.CancelWalkthroughCommand.Execute(null);

    Assert.Null(scan.Walkthrough);
    Assert.Equal(0, applied);
    Assert.Equal(before, RegistryService.Serialize(registry));
  }

  [Fact]
  public async Task Completed_walkthrough_applies_choices()
  {
    Write("app/.mcp.json", ClaudeJson("tool", "new-command"));
    McpRegistry registry = Registry(new McpServer { Name = "tool", Command = "old-command" });
    int applied = 0;
    ScanViewModel scan = CreateScan(registry, count => applied = count);
    await scan.ScanCommand.ExecuteAsync(null);
    scan.AddSelectedCommand.Execute(null);
    Assert.False(scan.AddPlacesCommand.CanExecute(null));

    scan.Walkthrough!.SelectedChoice = DiffChoice.UseFoundVersion;
    scan.AddPlacesCommand.Execute(null);

    Assert.Null(scan.Walkthrough);
    Assert.Equal(1, applied);
    McpServer server = Assert.Single(registry.Servers);
    Assert.Equal("new-command", server.Command);
    Assert.Contains(server.Id, registry.TargetFolders.Single(t => t.Name == "app").EnabledServers);
  }

  [Fact]
  public async Task Conflicting_choices_keep_walkthrough_open_with_message()
  {
    Write("a/.mcp.json", ClaudeJson("tool", "command-a"));
    Write("b/.mcp.json", ClaudeJson("tool", "command-b"));
    McpRegistry registry = Registry(new McpServer { Name = "tool", Command = "old-command" });
    string before = RegistryService.Serialize(registry);
    ScanViewModel scan = CreateScan(registry);
    await scan.ScanCommand.ExecuteAsync(null);
    scan.AddSelectedCommand.Execute(null);
    DiffViewModel walkthrough = scan.Walkthrough!;
    walkthrough.SelectedChoice = DiffChoice.UseFoundVersion;
    walkthrough.NextCommand.Execute(null);
    walkthrough.SelectedChoice = DiffChoice.UseFoundVersion;

    scan.AddPlacesCommand.Execute(null);

    Assert.Same(walkthrough, scan.Walkthrough);
    Assert.Contains("'tool'", scan.WalkthroughError);
    Assert.Equal([DiffChoice.UseFoundVersion, DiffChoice.UseFoundVersion], walkthrough.Choices);
    Assert.Equal(before, RegistryService.Serialize(registry));
  }

  private static ScannedFolderViewModel Folder(ScanViewModel scan, string name) =>
    scan.Folders.Single(f => Path.GetFileName(f.Path) == name);

  private McpRegistry Registry(params McpServer[] servers) => new()
  {
    Servers = [.. servers],
    Settings = new GlobalSettings { ScanRoots = [Root], CodexConfigPath = Path.Combine(_directory, "global.toml") },
  };

  private static ScanViewModel CreateScan(McpRegistry registry, Action<int>? applied = null) => new(
    new ConfigScanService(new ConfigExportService(), _ => []),
    new ScanImportPlanner(new ConfigImportService()),
    () => registry,
    applied ?? (_ => { }));

  private async Task<MainWindowViewModel> CreateMainAsync()
  {
    RegistryService registryService = new(WorkspacePath);
    await registryService.SaveAsync(Registry());
    MainWindowViewModel vm = new(registryService, new ConfigExportService(), new ConfigImportService(),
      new HttpMcpTester(), new StdioMcpTester(), new TransportDetectionService(), new ShellStub(),
      new ConfigScanService(new ConfigExportService(), _ => []));
    await vm.InitializeAsync();
    return vm;
  }

  private void Write(string relative, string content)
  {
    string path = Path.Combine(Root, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
  }

  private static string ClaudeJson(string name, string command) =>
    $"{{\"mcpServers\":{{\"{name}\":{{\"command\":\"{command}\"}}}}}}";

  private sealed class ShellStub : IShellEnvironmentService
  {
    public string? ResolvedPath => null;

    public Task ResolveAsync() => Task.CompletedTask;
  }

  public void Dispose() => Directory.Delete(_directory, recursive: true);
}
