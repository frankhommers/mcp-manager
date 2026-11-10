using McpManager.Core.Models;
using McpManager.Core.Services;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class SidebarOrderingTests : IDisposable
{
  private readonly string _tmp = Directory.CreateTempSubdirectory("mcp-sidebar-").FullName;

  private string WorkspacePath => Path.Combine(_tmp, "workspace.json");

  [Fact]
  public void Sidebar_sorts_by_display_name_and_tracks_renames_without_reordering_source()
  {
    MainWindowViewModel vm = Create();
    McpServerViewModel zulu = new(new McpServer { Name = "first-added", DisplayName = "Zulu" });
    McpServerViewModel alpha = new(new McpServer { Name = "second-added", DisplayName = "alpha" });
    McpServerViewModel fallback = new(new McpServer { Name = "Bravo" });
    vm.Servers.Add(zulu);
    vm.Servers.Add(alpha);
    vm.Servers.Add(fallback);
    Assert.Equal([alpha, fallback, zulu], vm.SidebarServers);
    Assert.Equal([zulu, alpha, fallback], vm.Servers);

    vm.SelectedServer = zulu;
    zulu.DisplayName = "Aardvark";
    Assert.Equal([zulu, alpha, fallback], vm.SidebarServers);
    Assert.Same(zulu, vm.SelectedServer);
    fallback.Name = "Aardwolf";
    Assert.Equal([zulu, fallback, alpha], vm.SidebarServers);

    vm.Servers.Remove(alpha);
    Assert.DoesNotContain(alpha, vm.SidebarServers);
    vm.Servers.Clear();
    Assert.Empty(vm.SidebarServers);
    alpha.DisplayName = "removed";
    Assert.Empty(vm.SidebarServers);
  }

  [Fact]
  public void Places_list_hides_export_destinations_and_sorts_global_then_project_places()
  {
    MainWindowViewModel vm = Create();
    TargetFolderViewModel projectZulu = new(new TargetFolder { Name = "Zulu" }, []);
    TargetFolderViewModel globalZulu = new(new TargetFolder { Name = "Zulu", IsGlobal = true }, []);
    TargetFolderViewModel projectAlpha = new(new TargetFolder { Name = "alpha" }, []);
    TargetFolderViewModel globalAlpha = new(new TargetFolder { Name = "alpha", IsGlobal = true }, []);
    TargetFolderViewModel clipboard = new(new TargetFolder { Name = "Clipboard", IsClipboard = true }, []);
    TargetFolderViewModel quick = new(new TargetFolder { Name = "Quick Export", IsQuickExport = true }, []);
    vm.TargetFolders = [projectZulu, globalZulu, projectAlpha, globalAlpha, quick, clipboard];

    Assert.Equal([globalAlpha, globalZulu, projectAlpha, projectZulu], vm.SidebarTargets);
    projectZulu.Name = "Aardvark";
    Assert.Equal([globalAlpha, globalZulu, projectZulu, projectAlpha], vm.SidebarTargets);
    vm.TargetFolders.Remove(projectAlpha);
    Assert.DoesNotContain(projectAlpha, vm.SidebarTargets);
  }

  [Fact]
  public async Task Ui_state_is_read_only_once_the_registry_is_loaded()
  {
    TargetFolder alpha = new() { Name = "alpha", Path = Path.Combine(_tmp, "alpha") };
    TargetFolder zulu = new() { Name = "zulu", Path = Path.Combine(_tmp, "zulu") };
    RegistryService registryService = new(WorkspacePath);
    await registryService.SaveAsync(new McpRegistry
    {
      TargetFolders = [alpha, zulu],
      Settings = new GlobalSettings { CodexConfigPath = Path.Combine(_tmp, "global.toml") },
    });
    MainWindowViewModel vm = Create(registryService);
    vm.Servers.Add(new McpServerViewModel(new McpServer { Name = "early" }));
    UiStateStore.NextTo(WorkspacePath).Save(new UiState { LastProjectId = zulu.Id });

    await vm.InitializeAsync();

    Assert.Equal(zulu.Id, vm.Matrix.SelectedChoice?.Column?.Place.Id);
  }

  private MainWindowViewModel Create(RegistryService? registryService = null) =>
    new(registryService ?? new RegistryService(WorkspacePath), new ConfigExportService(), new ConfigImportService(),
      new HttpMcpTester(), new StdioMcpTester(), new TransportDetectionService(), new ShellStub());

  private sealed class ShellStub : IShellEnvironmentService
  {
    public string? ResolvedPath => null;

    public Task ResolveAsync() => Task.CompletedTask;
  }

  public void Dispose() => Directory.Delete(_tmp, recursive: true);
}
