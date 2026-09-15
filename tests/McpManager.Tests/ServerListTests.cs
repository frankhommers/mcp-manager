using McpManager.Core.ConfigGenerators;
using McpManager.Core.Models;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class ServerListTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-server-list-").FullName;

  [Fact]
  public void Sorting_and_toggling_keep_the_same_selection_objects_and_tool_overrides()
  {
    McpServer zulu = CreateServer("zulu", "Zulu");
    McpServer alpha = CreateServer("alpha", "alpha");
    zulu.KnownTools = ["test-tool"];
    TargetFolder target = CreateTarget();
    target.ServerToolOverrides[zulu.Id] = [];
    TargetFolderViewModel vm = new(target, [zulu, alpha]);
    ServerSelectionViewModel zuluSelection = vm.ServerSelections.Single(s => s.ServerId == zulu.Id);
    ToolOverrideViewModel tool = Assert.Single(zuluSelection.ToolOverrides);

    Assert.Equal([alpha.Id, zulu.Id], vm.VisibleServerSelections.Select(s => s.ServerId));
    zuluSelection.IsEnabled = true;
    Assert.Equal([alpha.Id, zulu.Id], vm.VisibleServerSelections.Select(s => s.ServerId));
    Assert.Equal("1 of 2 selected · A–Z", vm.ServerSelectionSummary);
    Assert.Same(zuluSelection, vm.VisibleServerSelections[1]);
    Assert.Same(tool, Assert.Single(vm.VisibleServerSelections[1].ToolOverrides));
    Assert.False(tool.IsAllowed);

    zulu.DisplayName = "Aardvark";
    vm.RefreshServers([zulu, alpha]);
    Assert.Same(zuluSelection, vm.VisibleServerSelections[0]);
    Assert.True(zuluSelection.IsEnabled);
    Assert.False(Assert.Single(zuluSelection.ToolOverrides).IsAllowed);
  }

  [Fact]
  public async Task Search_and_bulk_selection_only_affect_visible_servers_and_survive_refresh_and_save()
  {
    McpServer alpha = CreateServer("alpha", "Alpha");
    McpServer tmux = CreateServer("terminal-tools", "Tmux");
    McpServer unifi = CreateServer("unifi", "Unifi");
    unifi.Group = "Network";
    TargetFolder target = CreateTarget();
    target.EnabledServers = [alpha.Id];
    TargetFolderViewModel vm = new(target, [tmux, unifi, alpha]);
    vm.ServerSearchText = "  NETWORK  ";

    Assert.Equal(unifi.Id, Assert.Single(vm.VisibleServerSelections).ServerId);
    vm.SelectVisibleServersCommand.Execute(null);
    Assert.Equal("2 of 3 selected · 1 shown", vm.ServerSelectionSummary);
    vm.DeselectVisibleServersCommand.Execute(null);
    Assert.True(vm.ServerSelections.Single(s => s.ServerId == alpha.Id).IsEnabled);
    Assert.False(vm.ServerSelections.Single(s => s.ServerId == unifi.Id).IsEnabled);
    vm.SelectVisibleServersCommand.Execute(null);

    vm.ServerSearchText = "TERMINAL";
    Assert.Equal(tmux.Id, Assert.Single(vm.VisibleServerSelections).ServerId);
    vm.DeselectVisibleServersCommand.Execute(null);
    vm.ServerSearchText = "no matches";
    Assert.True(vm.HasNoServerMatches);
    Assert.False(vm.SelectVisibleServersCommand.CanExecute(null));
    Assert.False(vm.DeselectVisibleServersCommand.CanExecute(null));
    Assert.Equal("2 of 3 selected · 0 shown", vm.ServerSelectionSummary);

    await vm.RefreshExistingServersAsync();
    vm.UpdateModel();
    Assert.True(target.EnabledServers.SetEquals([alpha.Id, unifi.Id]));
    vm.ClearServerSearchCommand.Execute(null);
    Assert.Equal([alpha.Id, tmux.Id, unifi.Id], vm.VisibleServerSelections.Select(s => s.ServerId));
    Assert.Equal("2 of 3 selected · A–Z", vm.ServerSelectionSummary);
    Assert.False(vm.HasNoServerMatches);
  }

  [Fact]
  public async Task Existing_config_updates_the_total_count_even_when_selected_server_is_hidden_by_search()
  {
    McpServer alpha = CreateServer("alpha", "Alpha");
    McpServer tmux = CreateServer("tmux", "Tmux");
    await File.WriteAllTextAsync(Path.Combine(_directory, ".mcp.json"),
      new ClaudeCodeConfigGenerator().GenerateConfig([tmux]));
    TargetFolderViewModel vm = new(CreateTarget(), [tmux, alpha]);
    vm.ServerSearchText = "alpha";
    List<string?> changed = [];
    vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

    await vm.RefreshExistingServersAsync();

    Assert.Equal("1 of 2 selected · 1 shown", vm.ServerSelectionSummary);
    Assert.Contains(nameof(TargetFolderViewModel.ServerSelectionSummary), changed);
    Assert.False(Assert.Single(vm.VisibleServerSelections).IsEnabled);
    Assert.Equal([alpha.Id, tmux.Id], vm.SortedServerSelections.Select(s => s.ServerId));
  }

  private TargetFolder CreateTarget() => new()
  {
    Path = _directory,
    EnabledClients = TargetClientFlags.ClaudeCode,
  };

  private static McpServer CreateServer(string name, string displayName) => new()
  {
    Name = name,
    DisplayName = displayName,
    Command = "test-mcp-server",
    TransportType = McpTransportType.Stdio,
  };

  public void Dispose() => Directory.Delete(_directory, recursive: true);
}
