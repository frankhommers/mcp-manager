using McpManager.Core.Models;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class MatrixViewModelTests
{
  private static TargetFolderViewModel Place(
    List<McpServer> servers, string name, TargetClientFlags clients, bool global = false, string path = "")
    => new(new TargetFolder { Name = name, Path = path, IsGlobal = global, EnabledClients = clients }, servers);

  private static McpServerViewModel Server(McpServer model) => new(model);

  [Fact]
  public void Columns_are_globals_plus_selected_project()
  {
    List<McpServer> servers = [new McpServer { Name = "a" }];
    TargetFolderViewModel project = Place(servers, "Zeta", TargetClientFlags.ClaudeCode, path: "/p/zeta");
    TargetFolderViewModel projectAlpha = Place(servers, "alpha", TargetClientFlags.Codex, path: "/p/alpha");
    TargetFolderViewModel globalOpenCode = Place(servers, "g1", TargetClientFlags.OpenCode, true, "/g1");
    TargetFolderViewModel globalClaude = Place(servers, "g2", TargetClientFlags.ClaudeDesktop, true, "/g2");
    TargetFolderViewModel clipboard = new(new TargetFolder { Name = "Clipboard", IsClipboard = true }, servers);
    TargetFolderViewModel quick = new(new TargetFolder { Name = "Quick", IsQuickExport = true }, servers);

    MatrixViewModel matrix = new(
      servers.Select(s => Server(s)), [project, clipboard, projectAlpha, quick, globalOpenCode, globalClaude]);

    Assert.Equal([projectAlpha, project], matrix.Projects.Select(c => c.Place).ToList());
    Assert.Equal([globalClaude, globalOpenCode, projectAlpha], matrix.Columns.Select(c => c.Place).ToList());
    Assert.Equal(["Claude Desktop", "OpenCode", "alpha"], matrix.Columns.Select(c => c.Header).ToList());
    Assert.Equal(["Global", "Global", "Project"], matrix.Columns.Select(c => c.GroupLabel).ToList());
  }

  [Fact]
  public void Selecting_project_swaps_column_keeps_cells_aligned_and_reports_choice()
  {
    List<McpServer> servers = [new McpServer { Name = "a" }, new McpServer { Name = "needle" }];
    TargetFolderViewModel global = Place(servers, "G", TargetClientFlags.OpenCode, true, "/g");
    TargetFolderViewModel one = Place(servers, "One", TargetClientFlags.ClaudeCode, path: "/work/one");
    TargetFolderViewModel two = Place(servers, "Two", TargetClientFlags.Codex, path: "/work/two");
    MatrixViewModel matrix = new(servers.Select(s => Server(s)), [global, one, two], two.Id);
    List<Guid?> reported = [];
    matrix.SelectedProjectChanged += (_, id) => reported.Add(id);

    Assert.Equal([global, two], matrix.Columns.Select(c => c.Place).ToList());
    matrix.SelectedProject = matrix.Projects.Single(p => p.Place == one);
    Assert.Equal([global, one], matrix.Columns.Select(c => c.Place).ToList());
    Assert.Equal([one.Id], reported);
    foreach (MatrixRowViewModel row in matrix.Rows)
    {
      Assert.Equal(2, row.Cells.Count);
      Assert.Equal(row.Server.Id, row.Cells[1].ServerId);
    }

    matrix.Rows[0].Cells[1].IsEnabled = true;
    Assert.True(one.ServerSelections.Single(s => s.ServerId == matrix.Rows[0].Server.Id).IsEnabled);

    matrix.ServerFilter = "NEED";
    Assert.Equal(["needle"], matrix.Rows.Select(r => r.Label).ToList());
  }

  [Fact]
  public void Unknown_remembered_project_falls_back_to_first_and_rename_keeps_selection()
  {
    List<McpServer> servers = [];
    TargetFolderViewModel one = Place(servers, "Bbb", TargetClientFlags.ClaudeCode, path: "/b");
    TargetFolderViewModel two = Place(servers, "Ccc", TargetClientFlags.Codex, path: "/c");
    MatrixViewModel matrix = new([], [one, two], Guid.NewGuid());
    Assert.Same(one, matrix.SelectedProject?.Place);
    List<Guid?> reported = [];
    matrix.SelectedProjectChanged += (_, id) => reported.Add(id);

    one.Name = "Zzz";

    Assert.Equal([two, one], matrix.Projects.Select(p => p.Place).ToList());
    Assert.Same(one, matrix.SelectedProject?.Place);
    Assert.Empty(reported);
  }

  [Fact]
  public void Without_projects_only_globals_show()
  {
    TargetFolderViewModel global = Place([], "G", TargetClientFlags.Codex, true, "/g");
    MatrixViewModel matrix = new([], [global]);

    Assert.False(matrix.HasProjects);
    Assert.Null(matrix.SelectedProject);
    Assert.Equal([global], matrix.Columns.Select(c => c.Place).ToList());
  }

  [Fact]
  public void Chips_for_claude_code_and_opencode()
  {
    List<McpServer> servers = [];
    TargetFolderViewModel claude = Place(servers, "A", TargetClientFlags.ClaudeCodeGlobal, true, "/a");
    TargetFolderViewModel both = Place(
      servers, "B", TargetClientFlags.ClaudeCode | TargetClientFlags.OpenCode, path: "/b");
    TargetFolderViewModel many = Place(servers, "C", TargetClientFlags.All & ~TargetClientFlags.ClaudeCodeGlobal);
    MatrixViewModel matrix = new([], [claude, both, many]);

    Assert.Equal(["CC"], matrix.Columns.Single(c => c.Place == claude).Chips);
    Assert.Equal(["CC", "OC"], matrix.Projects.Single(c => c.Place == both).Chips);
    Assert.Equal(["CC", "CD", "OC", "CX", "CU", "VS", "WS"], matrix.Projects.Single(c => c.Place == many).Chips);
  }

  [Fact]
  public void Status_text_and_missing_selection_and_fallback_header()
  {
    List<McpServer> servers = [new McpServer { Name = "a" }];
    TargetFolderViewModel place = Place(servers, "", TargetClientFlags.ClaudeCode, path: "/x/last");
    MatrixViewModel matrix = new(servers.Select(s => Server(s)), [place]);

    Assert.Equal("last", matrix.Columns[0].Header);
    Assert.Equal("Not deployed yet", matrix.Columns[0].StatusText);
    matrix.SetStatus(place.Id, PlaceStatus.ExternallyChanged);
    Assert.Equal("Changed outside MCP Manager", matrix.Columns[0].StatusText);
    matrix.SetStatus(place.Id, PlaceStatus.Behind);
    Assert.Equal("Not deployed", matrix.Columns[0].StatusText);
    Assert.Equal($"last{Environment.NewLine}/x/last{Environment.NewLine}Not deployed", matrix.Columns[0].ToolTipText);

    McpServerViewModel extra = Server(new McpServer { Name = "extra" });
    MatrixViewModel missing = new([extra], [place]);
    Assert.False(missing.Rows[0].Cells[0].IsEnabled);
    Assert.Equal(Guid.Empty, missing.Rows[0].Cells[0].ServerId);
  }

  [Fact]
  public void Cell_becomes_real_selection_when_place_gains_it()
  {
    McpServer model = new() { Name = "late" };
    TargetFolderViewModel place = Place([], "P", TargetClientFlags.ClaudeCode, path: "/p");
    McpServerViewModel server = Server(model);
    MatrixViewModel matrix = new([server], [place]);
    Assert.Equal(Guid.Empty, matrix.Rows[0].Cells[0].ServerId);

    place.RefreshServers([model]);

    ServerSelectionViewModel cell = matrix.Rows[0].Cells[0];
    Assert.Equal(model.Id, cell.ServerId);
    Assert.Same(place.ServerSelections.Single(), cell);
  }

  [Fact]
  public void Renames_relabel_and_resort_rows_and_columns()
  {
    List<McpServer> servers = [new McpServer { Name = "alpha" }, new McpServer { Name = "bravo" }];
    List<McpServerViewModel> serverVms = servers.Select(s => Server(s)).ToList();
    TargetFolderViewModel first = Place(servers, "Aaa", TargetClientFlags.ClaudeCode, path: "/a");
    TargetFolderViewModel second = Place(servers, "Bbb", TargetClientFlags.Codex, path: "/b");
    MatrixViewModel matrix = new(serverVms, [first, second]);

    serverVms[0].DisplayName = "Zulu";
    Assert.Equal(["bravo", "Zulu"], matrix.Rows.Select(r => r.Label).ToList());

    first.Name = "Zzz";
    Assert.Equal([second, first], matrix.Projects.Select(c => c.Place).ToList());
    Assert.Equal("Zzz", matrix.Projects[1].Header);
    Assert.Equal([first], matrix.Columns.Select(c => c.Place).ToList());

    first.SelectedClient = TargetClientFlags.OpenCode;
    Assert.Equal(["OC"], matrix.Columns[0].Chips);
  }
}
