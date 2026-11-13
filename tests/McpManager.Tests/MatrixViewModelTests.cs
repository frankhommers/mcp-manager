using McpManager.Core.Models;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class MatrixViewModelTests
{
  private static TargetFolderViewModel Place(
    List<McpServer> servers, string name, TargetClientFlags clients, bool global = false, string path = "")
    => new(new TargetFolder { Name = name, Path = path, IsGlobal = global, EnabledClients = clients }, servers);

  private static McpServerViewModel Server(McpServer model) => new(model);

  private static ScannedFolder Folder(string path, TargetClientFlags clients, bool global = false) =>
    new(path, global, clients, [], [], false);

  private static ScanServerMatch Match(
    string folder, string name, ScanMatchKind kind, McpServer? library, bool enabled = true,
    TargetClientFlags client = TargetClientFlags.ClaudeCode) =>
    new(
      folder, new ExistingTargetServer(name, null, enabled, client, folder + "/.mcp.json", "{}"),
      new McpServer { Name = name }, library, kind, null);

  private static List<TargetFolderViewModel> ProjectPlaces(MatrixViewModel matrix) =>
    matrix.Choices.Where(c => c.Kind == ProjectChoiceKind.Project).Select(c => c.Column!.Place).ToList();

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

    Assert.Equal([projectAlpha, project], ProjectPlaces(matrix));
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
    matrix.SelectedChoice = matrix.Choices.Single(p => p.Column?.Place == one);
    Assert.Equal([global, one], matrix.Columns.Select(c => c.Place).ToList());
    Assert.Equal([one.Id], reported);
    foreach (MatrixRowViewModel row in matrix.Rows)
    {
      Assert.Equal(2, row.Cells.Count);
      Assert.Equal(row.Server.Id, row.Cells[1].Selection.ServerId);
    }

    matrix.Rows[0].Cells[1].Selection.IsEnabled = true;
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
    Assert.Same(one, matrix.SelectedChoice?.Column?.Place);
    List<Guid?> reported = [];
    matrix.SelectedProjectChanged += (_, id) => reported.Add(id);

    one.Name = "Zzz";

    Assert.Equal([two, one], ProjectPlaces(matrix));
    Assert.Same(one, matrix.SelectedChoice?.Column?.Place);
    Assert.Empty(reported);
  }

  [Fact]
  public void Without_projects_only_globals_show()
  {
    TargetFolderViewModel global = Place([], "G", TargetClientFlags.Codex, true, "/g");
    MatrixViewModel matrix = new([], [global]);

    Assert.Null(matrix.SelectedChoice);
    Assert.Equal(["Other folder…"], matrix.Choices.Select(c => c.Header).ToList());
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
    Assert.Equal(["CC", "OC"], matrix.Choices.Single(c => c.Column?.Place == both).Chips);
    Assert.Equal(["CC", "CD", "OC", "CX", "CU", "VS", "WS"], matrix.Choices.Single(c => c.Column?.Place == many).Chips);
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
    Assert.False(missing.Rows[0].Cells[0].Selection.IsEnabled);
    Assert.Equal(Guid.Empty, missing.Rows[0].Cells[0].Selection.ServerId);
  }

  [Fact]
  public void Cell_becomes_real_selection_when_place_gains_it()
  {
    McpServer model = new() { Name = "late" };
    TargetFolderViewModel place = Place([], "P", TargetClientFlags.ClaudeCode, path: "/p");
    McpServerViewModel server = Server(model);
    MatrixViewModel matrix = new([server], [place]);
    Assert.Equal(Guid.Empty, matrix.Rows[0].Cells[0].Selection.ServerId);

    place.RefreshServers([model]);

    ServerSelectionViewModel cell = matrix.Rows[0].Cells[0].Selection;
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
    Assert.Equal([second, first], ProjectPlaces(matrix));
    Assert.Equal("Zzz", matrix.Choices[1].Header);
    Assert.Equal([first], matrix.Columns.Select(c => c.Place).ToList());

    first.SelectedClient = TargetClientFlags.OpenCode;
    Assert.Equal(["OC"], matrix.Columns[0].Chips);
  }

  [Fact]
  public void Choices_list_projects_header_found_and_other()
  {
    TargetFolderViewModel bbb = Place([], "Bbb", TargetClientFlags.ClaudeCode, path: "/r/bbb");
    TargetFolderViewModel aaa = Place([], "Aaa", TargetClientFlags.ClaudeCode, path: "/r/aaa");
    DiskSnapshot disk = new(
      [
        Folder("/r/zed", TargetClientFlags.ClaudeCode), Folder("/r/alpha", TargetClientFlags.ClaudeCode),
        Folder("/r/tests/x", TargetClientFlags.ClaudeCode), Folder("/r/aaa/", TargetClientFlags.ClaudeCode),
        Folder("/r/empty", TargetClientFlags.None), Folder("/home/.codex", TargetClientFlags.Codex, true),
      ],
      [], []);

    MatrixViewModel matrix = new([], [bbb, aaa], disk: disk, scanRoots: ["/r"]);

    Assert.Equal(
      ["Aaa", "Bbb", "Found on disk", "alpha", "zed", "Other folder…"],
      matrix.Choices.Select(c => c.Header).ToList());
    Assert.Equal(
      [
        ProjectChoiceKind.Project, ProjectChoiceKind.Project, ProjectChoiceKind.Header, ProjectChoiceKind.Found,
        ProjectChoiceKind.Found, ProjectChoiceKind.OtherFolder,
      ],
      matrix.Choices.Select(c => c.Kind).ToList());
    Assert.Equal([true, true, false, true, true, true], matrix.Choices.Select(c => c.IsSelectable).ToList());
    Assert.Equal("/r/alpha", matrix.Choices[3].Path);
    Assert.Equal("/r/alpha", matrix.Choices[3].Folder?.Path);
    Assert.Equal(["CC"], matrix.Choices[3].Chips);
    Assert.Same(aaa, matrix.SelectedChoice?.Column?.Place);

    matrix.SelectedChoice = matrix.Choices[2];
    Assert.Same(aaa, matrix.SelectedChoice?.Column?.Place);
  }

  [Fact]
  public void Other_folder_restores_selection_and_raises_event()
  {
    TargetFolderViewModel global = Place([], "G", TargetClientFlags.OpenCode, true, "/g");
    TargetFolderViewModel one = Place([], "One", TargetClientFlags.ClaudeCode, path: "/one");
    TargetFolderViewModel two = Place([], "Two", TargetClientFlags.ClaudeCode, path: "/two");
    MatrixViewModel matrix = new([], [global, one, two], two.Id);
    List<Guid?> reported = [];
    int requested = 0;
    matrix.SelectedProjectChanged += (_, id) => reported.Add(id);
    matrix.OtherFolderRequested += (_, _) => requested++;

    matrix.SelectedChoice = matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.OtherFolder);

    Assert.Same(two, matrix.SelectedChoice?.Column?.Place);
    Assert.Equal(1, requested);
    Assert.Empty(reported);
    Assert.Equal([global, two], matrix.Columns.Select(c => c.Place).ToList());
  }

  [Fact]
  public void Found_folder_shows_candidate_column_with_known_ticks_and_raises_candidate_edited_once()
  {
    McpServer known = new() { Name = "known" };
    McpServer differs = new() { Name = "differs" };
    McpServer off = new() { Name = "off" };
    List<McpServer> servers = [known, differs, off];
    TargetFolderViewModel global = Place(servers, "G", TargetClientFlags.OpenCode, true, "/g");
    TargetFolderViewModel project = Place(servers, "P", TargetClientFlags.ClaudeCode, path: "/r/p");
    DiskSnapshot disk = new(
      [Folder("/r/found", TargetClientFlags.ClaudeCode | TargetClientFlags.Codex)],
      [
        Match("/r/found", "known", ScanMatchKind.Known, known),
        Match("/r/found", "differs", ScanMatchKind.Differs, differs),
        Match("/r/found", "off", ScanMatchKind.Known, off, false),
      ],
      []);
    MatrixViewModel matrix = new(servers.Select(s => Server(s)), [global, project], project.Id, disk, ["/r"]);
    List<Guid?> reported = [];
    List<CandidateEditedEventArgs> edits = [];
    matrix.SelectedProjectChanged += (_, id) => reported.Add(id);
    matrix.CandidateEdited += (_, e) => edits.Add(e);

    matrix.SelectedChoice = matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found);

    Assert.Equal([null], reported);
    Assert.Equal(2, matrix.Columns.Count);
    Assert.Same(global, matrix.Columns[0].Place);
    PlaceColumnViewModel candidate = matrix.Columns[1];
    Assert.True(candidate.IsCandidate);
    Assert.Equal("Found on disk", candidate.GroupLabel);
    Assert.Equal("found", candidate.Header);
    Assert.Equal("/r/found", candidate.Place.Path);
    Assert.Equal(["CC", "CX"], candidate.Chips);
    MatrixCellViewModel knownCell = matrix.Rows.Single(r => r.Server.Id == known.Id).Cells[1];
    MatrixCellViewModel differsCell = matrix.Rows.Single(r => r.Server.Id == differs.Id).Cells[1];
    MatrixCellViewModel offCell = matrix.Rows.Single(r => r.Server.Id == off.Id).Cells[1];
    Assert.True(knownCell.Selection.IsEnabled);
    Assert.False(knownCell.HasDifference);
    Assert.False(differsCell.Selection.IsEnabled);
    Assert.True(differsCell.HasDifference);
    Assert.Equal("differs", differsCell.Difference?.Found.Name);
    Assert.False(offCell.Selection.IsEnabled);
    Assert.False(matrix.Rows.Single(r => r.Server.Id == differs.Id).Cells[0].HasDifference);
    Assert.Empty(edits);

    knownCell.Selection.IsEnabled = false;
    offCell.Selection.IsEnabled = true;

    CandidateEditedEventArgs edit = Assert.Single(edits);
    Assert.Equal("/r/found", edit.Folder.Path);
    Assert.Equal(candidate.Place.ServerSelections, edit.Selections);

    matrix.SelectedChoice = matrix.Choices.Single(c => c.Column?.Place == project);
    Assert.Equal([null, project.Id], reported);
    Assert.Equal([global, project], matrix.Columns.Select(c => c.Place).ToList());
  }

  [Fact]
  public void Remembered_candidate_path_is_selected_when_no_project_matches()
  {
    TargetFolderViewModel project = Place([], "P", TargetClientFlags.ClaudeCode, path: "/r/p");
    DiskSnapshot disk = new([Folder("/r/found", TargetClientFlags.ClaudeCode)], [], []);

    MatrixViewModel byPath = new([], [project], Guid.NewGuid(), disk, ["/r"], "/r/found/");
    MatrixViewModel byProject = new([], [project], project.Id, disk, ["/r"], "/r/found");

    Assert.Equal(ProjectChoiceKind.Found, byPath.SelectedChoice?.Kind);
    Assert.True(byPath.Columns.Single().IsCandidate);
    Assert.Same(project, byProject.SelectedChoice?.Column?.Place);
  }

  [Fact]
  public void Explicit_candidates_under_a_test_folder_are_offered_and_selected()
  {
    DiskSnapshot disk = new(
      [
        Folder("/r/tests/app", TargetClientFlags.ClaudeCode), Folder("/r/tests/pinned", TargetClientFlags.ClaudeCode),
        Folder("/r/tests/hidden", TargetClientFlags.ClaudeCode),
      ],
      [], []);

    MatrixViewModel matrix = new([], [], null, disk, ["/r"], "/r/tests/app/", ["/r/tests/pinned"]);

    Assert.Equal(ProjectChoiceKind.Found, matrix.SelectedChoice?.Kind);
    Assert.Equal("/r/tests/app", matrix.SelectedChoice?.Path);
    Assert.True(Assert.Single(matrix.Columns).IsCandidate);
    Assert.Equal(
      ["app", "pinned"],
      matrix.Choices.Where(c => c.Kind == ProjectChoiceKind.Found).Select(c => c.Header).ToList());
  }

  [Fact]
  public void Unknown_rows_only_for_visible_columns()
  {
    McpServer known = new() { Name = "known" };
    List<McpServer> servers = [known];
    TargetFolderViewModel codex = Place(servers, "Codex", TargetClientFlags.Codex, true, "/codex");
    TargetFolderViewModel openCode = Place(servers, "OC", TargetClientFlags.OpenCode, true, "/oc");
    TargetFolderViewModel project = Place(servers, "P", TargetClientFlags.ClaudeCode, path: "/r/p");
    DiskSnapshot disk = new(
      [
        Folder("/home/.codex", TargetClientFlags.Codex, true), Folder("/r/p", TargetClientFlags.ClaudeCode),
        Folder("/r/other", TargetClientFlags.ClaudeCode),
      ],
      [
        Match("/home/.codex", "jira", ScanMatchKind.Unknown, null, client: TargetClientFlags.Codex),
        Match("/home/.codex", "known", ScanMatchKind.Known, known, client: TargetClientFlags.Codex),
        Match("/r/other", "old", ScanMatchKind.Unknown, null),
        Match("/r/other", "jira", ScanMatchKind.Unknown, null),
      ],
      []);

    MatrixViewModel matrix = new([Server(known)], [codex, openCode, project], project.Id, disk, ["/r"]);

    Assert.Equal([codex, openCode, project], matrix.Columns.Select(c => c.Place).ToList());
    UnknownServerRowViewModel row = Assert.Single(matrix.UnknownRows);
    Assert.Equal("jira", row.Name);
    Assert.Equal([true, false, false], row.FoundIn);
    Assert.True(matrix.HasUnknownRows);

    matrix.ServerFilter = "zzz";
    Assert.Empty(matrix.UnknownRows);
    Assert.False(matrix.HasUnknownRows);
    matrix.ServerFilter = "JI";
    Assert.Equal(["jira"], matrix.UnknownRows.Select(r => r.Name).ToList());
    matrix.ServerFilter = string.Empty;

    matrix.SelectedChoice = matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found);
    Assert.Equal(["jira", "old"], matrix.UnknownRows.Select(r => r.Name).ToList());
    Assert.Equal([true, false, true], matrix.UnknownRows[0].FoundIn);
    Assert.Equal([false, false, true], matrix.UnknownRows[1].FoundIn);
  }

  [Fact]
  public void Stale_snapshot_is_ignored()
  {
    McpServer live = new() { Name = "live" };
    McpServer gone = new() { Name = "gone" };
    List<McpServer> servers = [live];
    TargetFolderViewModel codex = Place(servers, "Codex", TargetClientFlags.Codex, true, "/codex");
    TargetFolderViewModel project = Place(servers, "P", TargetClientFlags.ClaudeCode, path: "/r/p");
    DiskSnapshot disk = new(
      [Folder("/home/.codex", TargetClientFlags.Codex, true), Folder("/r/deleted", TargetClientFlags.ClaudeCode)],
      [
        Match("/home/.codex", "gone", ScanMatchKind.Differs, gone, client: TargetClientFlags.Codex),
        Match("/r/deleted", "gone", ScanMatchKind.Known, gone),
      ],
      []);

    MatrixViewModel matrix = new([Server(live)], [codex, project], project.Id, disk, ["/r"]);

    Assert.All(matrix.Rows.SelectMany(r => r.Cells), c => Assert.False(c.HasDifference));
    Assert.Equal(["gone"], matrix.UnknownRows.Select(r => r.Name).ToList());

    ProjectChoiceViewModel deleted = matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found);
    Assert.Equal("/r/deleted", deleted.Path);
    matrix.SelectedChoice = deleted;

    Assert.True(matrix.Columns[1].IsCandidate);
    Assert.All(matrix.Columns[1].Place.ServerSelections, s => Assert.False(s.IsEnabled));
    Assert.All(matrix.Rows.SelectMany(r => r.Cells), c => Assert.False(c.HasDifference));
    Assert.Equal([true, true], matrix.UnknownRows.Single().FoundIn);
  }

  [Fact]
  public void Difference_that_deploy_replaces_is_marked_as_updated_on_deploy()
  {
    McpServer adopted = new() { Name = "adopted" };
    McpServer managed = new() { Name = "managed" };
    McpServer foreign = new() { Name = "foreign" };
    List<McpServer> servers = [adopted, managed, foreign];
    TargetFolderViewModel project = Place(servers, "P", TargetClientFlags.ClaudeCode, path: "/r/p");
    project.Model.AdoptedServerNames.Add("adopted");
    ScanServerMatch managedMatch = Match("/r/p", "managed", ScanMatchKind.Differs, managed);
    DiskSnapshot disk = new(
      [Folder("/r/p", TargetClientFlags.ClaudeCode)],
      [
        Match("/r/p", "adopted", ScanMatchKind.Differs, adopted),
        managedMatch with { Found = managedMatch.Found with { ManagedId = managed.Id } },
        Match("/r/p", "foreign", ScanMatchKind.Differs, foreign),
      ],
      []);

    MatrixViewModel matrix = new([.. servers.Select(Server)], [project], project.Id, disk, ["/r"]);

    Dictionary<string, MatrixCellViewModel> cells = matrix.Rows.ToDictionary(r => r.Server.Name, r => r.Cells.Single());
    Assert.True(cells["adopted"].IsUpdatedOnDeploy);
    Assert.True(cells["managed"].IsUpdatedOnDeploy);
    Assert.False(cells["foreign"].IsUpdatedOnDeploy);
    Assert.False(cells["adopted"].NeedsChoice);
    Assert.True(cells["foreign"].NeedsChoice);
  }

  [Fact]
  public void Difference_in_a_found_folder_always_needs_a_choice()
  {
    McpServer alpha = new() { Name = "alpha" };
    ScanServerMatch match = Match("/r/found", "alpha", ScanMatchKind.Differs, alpha);
    DiskSnapshot disk = new(
      [Folder("/r/found", TargetClientFlags.ClaudeCode)],
      [match with { Found = match.Found with { ManagedId = alpha.Id } }],
      []);
    MatrixViewModel matrix = new([Server(alpha)], [], null, disk, ["/r"]);

    matrix.SelectedChoice = matrix.Choices.Single(c => c.Kind == ProjectChoiceKind.Found);

    MatrixCellViewModel cell = matrix.Rows.Single().Cells.Single();
    Assert.False(cell.IsUpdatedOnDeploy);
    Assert.True(cell.NeedsChoice);
  }

  [Fact]
  public void Existing_project_ignores_entries_of_harnesses_it_does_not_use()
  {
    McpServer alpha = new() { Name = "alpha" };
    List<McpServer> servers = [alpha];
    TargetFolderViewModel project = Place(servers, "P", TargetClientFlags.ClaudeCode, path: "/r/p");
    DiskSnapshot disk = new(
      [Folder("/r/p", TargetClientFlags.ClaudeCode | TargetClientFlags.Cursor)],
      [
        Match("/r/p", "alpha", ScanMatchKind.Differs, alpha, client: TargetClientFlags.Cursor),
        Match("/r/p", "cursor-only", ScanMatchKind.Unknown, null, client: TargetClientFlags.Cursor),
        Match("/r/p", "claude-only", ScanMatchKind.Unknown, null),
      ],
      []);

    MatrixViewModel matrix = new([Server(alpha)], [project], project.Id, disk, ["/r"]);

    Assert.False(matrix.Rows.Single().Cells.Single().HasDifference);
    Assert.Equal(["claude-only"], matrix.UnknownRows.Select(r => r.Name).ToList());
  }
}
