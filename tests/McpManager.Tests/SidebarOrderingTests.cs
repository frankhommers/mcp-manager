using McpManager.Core.Models;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class SidebarOrderingTests
{
  [Fact]
  public void Sidebar_sorts_by_display_name_and_tracks_renames_without_reordering_source()
  {
    MainWindowViewModel vm = new();
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
  public void Targets_pin_clipboard_and_quick_export_then_sort_global_and_project_targets()
  {
    MainWindowViewModel vm = new();
    TargetFolderViewModel projectZulu = new(new TargetFolder { Name = "Zulu" }, []);
    TargetFolderViewModel globalZulu = new(new TargetFolder { Name = "Zulu", IsGlobal = true }, []);
    TargetFolderViewModel projectAlpha = new(new TargetFolder { Name = "alpha" }, []);
    TargetFolderViewModel globalAlpha = new(new TargetFolder { Name = "alpha", IsGlobal = true }, []);
    TargetFolderViewModel clipboard = new(new TargetFolder { Name = "Clipboard", IsClipboard = true }, []);
    TargetFolderViewModel quick = new(new TargetFolder { Name = "Quick Export", IsQuickExport = true }, []);
    vm.TargetFolders = [projectZulu, globalZulu, projectAlpha, globalAlpha, quick, clipboard];

    Assert.Equal([clipboard, quick, globalAlpha, globalZulu, projectAlpha, projectZulu], vm.SidebarTargets);
    projectZulu.Name = "Aardvark";
    Assert.Equal([clipboard, quick, globalAlpha, globalZulu, projectZulu, projectAlpha], vm.SidebarTargets);
    vm.TargetFolders.Remove(quick);
    Assert.DoesNotContain(quick, vm.SidebarTargets);
  }
}
