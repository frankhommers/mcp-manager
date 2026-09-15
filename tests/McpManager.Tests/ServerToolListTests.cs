using McpManager.Core.Models;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class ServerToolListTests
{
  [Fact]
  public void Tool_search_and_bulk_actions_preserve_hidden_choices_and_update_the_row_summary()
  {
    McpServer server = CreateServer();
    TargetFolder model = new() { EnabledServers = [server.Id] };
    TargetFolderViewModel target = new(model, [server]);
    ServerSelectionViewModel selection = Assert.Single(target.ServerSelections);
    List<string?> changed = [];
    selection.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

    Assert.Equal(["alpha", "beta", "Zulu"], selection.VisibleTools.Select(t => t.ToolName));
    Assert.Equal("1 of 3 tools selected", selection.ToolSelectionSummary);
    selection.ToolSearchText = " ALP ";
    Assert.Equal("alpha", Assert.Single(selection.VisibleTools).ToolName);
    selection.SelectVisibleToolsCommand.Execute(null);
    Assert.Equal("2 of 3 tools selected", selection.ToolSelectionSummary);
    selection.ClearVisibleToolsCommand.Execute(null);
    Assert.True(selection.ToolOverrides.Single(t => t.ToolName == "beta").IsAllowed);
    Assert.Equal("1 of 3 tools selected", selection.ToolSelectionSummary);
    Assert.Contains(nameof(ServerSelectionViewModel.ToolSelectionSummary), changed);

    selection.ToolSearchText = "missing";
    Assert.True(selection.HasNoToolMatches);
    Assert.False(selection.SelectVisibleToolsCommand.CanExecute(null));
    Assert.False(selection.ClearVisibleToolsCommand.CanExecute(null));
    target.UpdateModel();
    Assert.Equal(["beta"], model.ServerToolOverrides[server.Id]);
    selection.ClearToolSearchCommand.Execute(null);
    Assert.Equal(3, selection.VisibleTools.Count);
  }

  [Fact]
  public void Expansion_search_and_tool_choices_survive_server_filtering_and_disable_enable()
  {
    McpServer server = CreateServer();
    TargetFolder model = new() { EnabledServers = [server.Id] };
    TargetFolderViewModel target = new(model, [server]);
    ServerSelectionViewModel selection = Assert.Single(target.ServerSelections);
    selection.AreToolsExpanded = true;
    selection.ToolSearchText = "beta";
    target.ServerSearchText = "hidden";
    Assert.Empty(target.VisibleServerSelections);
    target.ClearServerSearchCommand.Execute(null);

    Assert.Same(selection, Assert.Single(target.VisibleServerSelections));
    Assert.True(selection.AreToolsExpanded);
    Assert.Equal("beta", selection.ToolSearchText);
    selection.AreToolsExpanded = false;
    selection.IsEnabled = false;
    Assert.False(selection.ClearVisibleToolsCommand.CanExecute(null));
    selection.ClearVisibleToolsCommand.Execute(null);
    Assert.True(Assert.Single(selection.VisibleTools).IsAllowed);
    target.UpdateModel();
    Assert.Contains(server.Id, model.DisabledServers);
    Assert.Equal(["beta"], model.ServerToolOverrides[server.Id]);

    selection.IsEnabled = true;
    selection.AreToolsExpanded = true;
    Assert.True(selection.ClearVisibleToolsCommand.CanExecute(null));
    Assert.True(Assert.Single(selection.VisibleTools).IsAllowed);
    Assert.Equal("1 of 3 tools selected", selection.ToolSelectionSummary);
  }

  [Fact]
  public void Rediscovery_updates_counts_and_order_without_resetting_existing_choices_or_expansion()
  {
    McpServer server = CreateServer();
    TargetFolderViewModel target = new(new TargetFolder { EnabledServers = [server.Id] }, [server]);
    ServerSelectionViewModel selection = Assert.Single(target.ServerSelections);
    selection.AreToolsExpanded = true;
    ToolOverrideViewModel alpha = selection.ToolOverrides.Single(t => t.ToolName == "alpha");
    alpha.IsAllowed = true;
    server.KnownTools = ["gamma", "beta", "alpha"];
    target.RefreshServers([server]);

    Assert.Equal(["alpha", "beta", "gamma"], selection.VisibleTools.Select(t => t.ToolName));
    Assert.Equal("2 of 3 tools selected", selection.ToolSelectionSummary);
    Assert.Same(alpha, selection.VisibleTools[0]);
    Assert.True(selection.AreToolsExpanded);
    Assert.False(selection.VisibleTools[2].IsAllowed);
    selection.ToolOverrides.Clear();
    Assert.False(selection.HasToolOverrides);
    Assert.Empty(selection.VisibleTools);
    Assert.Equal("No tools discovered", selection.ToolSelectionSummary);
  }

  [Fact]
  public void Inline_tool_changes_remain_specific_to_the_target()
  {
    McpServer server = CreateServer();
    TargetFolder firstModel = new() { EnabledServers = [server.Id] };
    TargetFolder secondModel = new() { EnabledServers = [server.Id] };
    TargetFolderViewModel first = new(firstModel, [server]);
    TargetFolderViewModel second = new(secondModel, [server]);
    ServerSelectionViewModel selection = Assert.Single(first.ServerSelections);
    selection.SelectVisibleToolsCommand.Execute(null);
    first.UpdateModel();
    second.UpdateModel();

    Assert.Equal(3, firstModel.ServerToolOverrides[server.Id].Count);
    Assert.Equal(["beta"], secondModel.ServerToolOverrides[server.Id]);
    Assert.Equal(["beta"], server.AlwaysAllow);
  }

  private static McpServer CreateServer() => new()
  {
    Name = "example",
    DisplayName = "Example",
    KnownTools = ["Zulu", "alpha", "beta"],
    AlwaysAllow = ["beta"],
  };
}
