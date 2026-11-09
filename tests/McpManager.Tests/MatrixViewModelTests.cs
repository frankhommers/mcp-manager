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
  public void Columns_exclude_clipboard_and_quick_export_global_first()
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

    Assert.Equal(
      [globalClaude, globalOpenCode, projectAlpha, project], matrix.Columns.Select(c => c.Place).ToList());
    Assert.Equal(["Claude Desktop", "OpenCode", "alpha", "Zeta"], matrix.Columns.Select(c => c.Header).ToList());
    Assert.Equal([true, true, false, false], matrix.Columns.Select(c => c.IsGlobal).ToList());
  }

  [Fact]
  public void Cell_toggles_place_selection()
  {
    List<McpServer> servers = [new McpServer { Name = "b" }, new McpServer { Name = "a", DisplayName = "Zed" }];
    TargetFolderViewModel place = Place(servers, "P", TargetClientFlags.ClaudeCode, path: "/p");
    int changes = 0;
    place.ConfigurationChanged += (_, _) => changes++;
    MatrixViewModel matrix = new(servers.Select(s => Server(s)), [place]);

    Assert.Equal(["b", "Zed"], matrix.Rows.Select(r => r.Label).ToList());
    MatrixRowViewModel row = matrix.Rows[1];
    row.Cells[0].IsEnabled = true;

    Assert.True(place.ServerSelections.Single(s => s.ServerId == servers[1].Id).IsEnabled);
    Assert.False(place.ServerSelections.Single(s => s.ServerId == servers[0].Id).IsEnabled);
    Assert.True(changes > 0);
  }

  [Fact]
  public void Place_filter_hides_columns_and_cells_stay_aligned()
  {
    List<McpServer> servers = [new McpServer { Name = "a" }, new McpServer { Name = "needle" }];
    TargetFolderViewModel one = Place(servers, "One", TargetClientFlags.ClaudeCode, path: "/work/one");
    TargetFolderViewModel two = Place(servers, "Two", TargetClientFlags.Codex, path: "/work/two");
    TargetFolderViewModel three = Place(servers, "Three", TargetClientFlags.Cursor, path: "/other/three");
    MatrixViewModel matrix = new(servers.Select(s => Server(s)), [one, two, three]);

    matrix.PlaceFilter = "WORK";
    Assert.Equal([one, two], matrix.Columns.Select(c => c.Place).ToList());
    foreach (MatrixRowViewModel row in matrix.Rows)
    {
      Assert.Equal(2, row.Cells.Count);
      Assert.Equal(row.Server.Id, row.Cells[0].ServerId);
    }

    matrix.PlaceFilter = "three";
    Assert.Single(matrix.Columns);
    matrix.Rows[0].Cells[0].IsEnabled = true;
    Assert.True(three.ServerSelections.Single(s => s.ServerId == matrix.Rows[0].Server.Id).IsEnabled);

    matrix.ServerFilter = "NEED";
    Assert.Equal(["needle"], matrix.Rows.Select(r => r.Label).ToList());
    matrix.PlaceFilter = "";
    Assert.Equal(3, matrix.Rows[0].Cells.Count);
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
    Assert.Equal(["CC", "OC"], matrix.Columns.Single(c => c.Place == both).Chips);
    Assert.Equal(["CC", "CD", "OC", "CX", "CU", "VS", "WS"], matrix.Columns.Single(c => c.Place == many).Chips);
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
    Assert.Equal([second, first], matrix.Columns.Select(c => c.Place).ToList());
    Assert.Equal("Zzz", matrix.Columns[1].Header);

    first.SelectedClient = TargetClientFlags.OpenCode;
    Assert.Equal(["OC"], matrix.Columns[1].Chips);
  }
}
