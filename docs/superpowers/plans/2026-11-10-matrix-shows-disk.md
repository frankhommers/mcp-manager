# Matrix Shows Disk Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Scan page with a matrix that shows what is on disk: found folders in the project picker, unknown servers as rows, and differences as clickable dots.

**Architecture:** A Core `DiskSnapshotService` reads scan roots, global configs and existing places in the background and matches them against the library. A deserialized registry copy keeps that work off the live model. `MatrixViewModel` turns a `DiskSnapshot` into picker choices, a candidate column, cell difference markers and unknown-server rows. `MainWindowViewModel` owns the snapshot lifecycle and the actions: promote, + Library, resolve difference, other folder. Each action re-matches the affected folders against the live registry through `ScanImportPlanner`.

**Tech Stack:** .NET 10, C#, Avalonia 11.3 (compiled bindings), CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-10-matrix-shows-disk-design.md`

## Global Constraints

- Code style follows the repo `CLAUDE.md`:
  - Allman braces, 2-space indent;
  - no `var`;
  - one type per file;
  - `Async` suffix on Task-returning methods;
  - catch specific exceptions only (the existing `when (ex is not OperationCanceledException)` pattern is allowed
    where a whole folder must be skipped).
- Commits:
  - conventional prefix;
  - message explains why;
  - **never** a Co-Authored-By or any AI/tool attribution.
- No secrets in code, tests or logs. Test tokens are obvious fakes like `tok-a`.
- Never overwrite a differing server entry silently (user requirement). Differing entries stay unadopted until the
  user resolves them in the diff dialog.
- Exact UI copy:
  - picker header "Found on disk", picker item "Other folder…", candidate column group label "Found on disk";
  - section "Found in your configs, not in your library", button "+ Library";
  - status messages "Added project {name}. Save to keep it." and "Choose which harnesses {name} uses.";
  - diff labels "Library" / "Found on disk";
  - Preferences label "Folders to look for projects in".
- Run tests from the worktree root: `dotnet test` (whole suite) or `dotnet test --filter "FullyQualifiedName~<Class>"`.

## Review Focus

- A place path with a trailing separator (`/Users/x/_argaleo/melvin/`) must not also appear as a found folder.
  Covered by a T1 test.
- "+ Library" with the same name but a different token in another place must not adopt it there. It shows a
  difference dot instead. Covered by a T2 test.
- Discovery hitting an unreadable config or a folder whose match throws must still return the other folders.
  Covered by a T1 test.
- A snapshot that is older than the live library must be ignored without crashing. Examples: a server deleted after
  the snapshot, or a match whose `Library` id no longer exists. Covered by a T3 test.
- Promoting a candidate must adopt only Known in-library servers. Differs and unknown entries stay untouched.
  Covered by a T2 test.

---

### Task 1: Disk snapshot in Core

**Files:**
- Create: `src/McpManager.Core/Models/DiskSnapshot.cs`
- Create: `src/McpManager.Core/Services/DiskSnapshotService.cs`
- Create: `src/McpManager.Core/Services/ProjectFolderRules.cs`
- Modify: `src/McpManager.Core/Services/ConfigScanService.cs` (add two public read methods)
- Modify: `src/McpManager.Core/Services/ScanImportPlanner.cs` (`FindPlace` uses `ProjectFolderRules.PathsEqual`)
- Test: `tests/McpManager.Tests/DiskSnapshotServiceTests.cs`

**Interfaces:**
- Produces:
  - `public static class ProjectFolderRules`:
    - `public static bool PathsEqual(string left, string right)`: `Path.TrimEndingDirectorySeparator` on both, then
      `OrdinalIgnoreCase`.
    - `public static bool LooksLikeTest(string path, IEnumerable<string> roots)`: moved verbatim from
      `ScannedFolderViewModel.LooksLikeTest`, together with `IsTestSegment` and `IsBoundary`.
  - `public sealed record DiskSnapshot(IReadOnlyList<ScannedFolder> Folders, IReadOnlyList<ScanServerMatch> Matches)`
    with:
    - `public static DiskSnapshot Empty`;
    - `public ScannedFolder? FolderFor(TargetFolder place)`: a global place matches a global folder with equal
      `Clients`; a project matches a non-global folder by `PathsEqual`;
    - `public IReadOnlyList<ScanServerMatch> MatchesIn(ScannedFolder folder)`: by `FolderPath`, ordinal.
  - `ConfigScanService`:
    - `public Task<ScannedFolder> ReadFolderAsync(string path, GlobalSettings settings)`: detects harnesses; a folder
      without configs gives `Clients = None` and no servers;
    - `public Task<ScannedFolder> ReadPlaceAsync(TargetFolder place, GlobalSettings settings)`: wraps the existing
      private `ReadAsync(place, settings, place.IsGlobal)`.
  - `public class DiskSnapshotService(ConfigScanService scanService, ScanImportPlanner planner)` with:
    - `public async Task<DiskSnapshot> LoadAsync(McpRegistry registry, CancellationToken cancellationToken = default)`;
    - `public DiskSnapshot Rematch(DiskSnapshot snapshot, McpRegistry registry)`.

`LoadAsync` does the following:
1. Scans `RegistryService.GetDefaultScanRoots(registry.Settings)` with `ScanAsync`.
2. Adds `ReadPlaceAsync` results for every non-global, non-clipboard, non-quick-export place whose directory exists
   and that no scanned folder already covers (`PathsEqual`).
3. Calls `planner.Match` per folder. A folder whose match throws (anything but `OperationCanceledException`) keeps
   its folder entry and contributes no matches.

`Rematch` re-runs step 3 on `snapshot.Folders`.

- [ ] **Step 1: Write the failing tests** in `DiskSnapshotServiceTests` (temp dir fixture like `ConfigScanServiceTests`; scan service built as `new ConfigScanService(new ConfigExportService(), _ => [], homeDirectory: <tempHome>)`; registry `Settings.ScanRoots = [root]`):
  - `Load_includes_places_outside_scan_roots`: a place at `<tmp>/elsewhere` with a `.mcp.json` containing server `a`. `LoadAsync` → `Folders` contains that path; `Matches` has a match with `Found.Name == "a"`.
  - `Trailing_separator_place_is_the_same_folder`: a place `Path = <root>/melvin/` and `<root>/melvin/.mcp.json`. Exactly one folder for melvin. `FolderFor(place)` is not null.
  - `Folder_whose_match_throws_keeps_other_folders`: a subclass `ThrowingPlanner : ScanImportPlanner` overrides `Match` to throw `InvalidOperationException` when a folder path ends with `bad`. Both folders are present; matches exist only for the good folder.
  - `Rematch_reflects_new_library`: after adding the found server to `registry.Servers` (same name and definition), `Rematch` gives `Kind == Known`, where it was `Unknown` before.
  - `Read_folder_without_configs_has_no_clients`: `ReadFolderAsync(<empty dir>)` → `Clients == None`, `Servers` empty.
  - `LooksLikeTest` cases: move the existing data from `ScanViewModelTests.Test_rule_matches_whole_segments_below_the_scan_root` into a `[Theory]` against `ProjectFolderRules.LooksLikeTest`.
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~DiskSnapshotServiceTests"`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement** the interfaces above. `ScannedFolderViewModel` calls `ProjectFolderRules.LooksLikeTest` until Task 5 deletes it.
- [ ] **Step 4: Run** the same filter, then `dotnet test`. Expected: all PASS.
- [ ] **Step 5: Commit** `feat: disk snapshot of scan roots and existing places`

### Task 2: Promote a folder and add an unknown server to the library

**Files:**
- Modify: `src/McpManager.Core/Services/ScanImportPlanner.cs`
- Test: `tests/McpManager.Tests/ScanImportPlannerTests.cs` (reuse its `Found`, `Folder`, `Library` helpers)

**Interfaces:**
- Consumes: `ProjectFolderRules.PathsEqual` (T1).
- Produces:
  - `public TargetFolder PromoteFolder(McpRegistry registry, ScannedFolder folder)`:
    1. Re-matches `[folder]` against `registry`.
    2. Applies only the matches with `Kind == Known` whose `Library` is in `registry.Servers` (by `Id`), with no
       choices, through `Apply`.
    3. Returns `FindPlace(registry, folder)!`. An existing place is returned unchanged except for the harnesses
       `Apply` already adds.
  - `public McpServer AddToLibrary(McpRegistry registry, IReadOnlyList<ScannedFolder> folders, string serverName)`:
    1. Re-matches `folders`. The occurrences are matches with `Found.Name == serverName` whose `Library` is null or
       not in `registry.Servers` (by `Id`). Throws `ArgumentException("{serverName} was not found in your configs.")`
       when there are none.
    2. Adds the first occurrence's `Imported` to the library.
    3. For every occurrence that has an existing place (`FindPlace`), is `Found.IsEnabled`, and whose `Imported` has
       no differences from the first (`ServerDiffService.Compare(first.Imported, occ.Imported).HasDifferences ==
       false`): add the server id to `EnabledServers`, remove it from `DisabledServers`, add `serverName` to
       `AdoptedServerNames`.
    4. Returns the library server.

- [ ] **Step 1: Write the failing tests:**
  - `Promote_adopts_only_known_library_servers`:
    - The folder has `known` (same as library), `differs` (library has `tok-a`, found has `tok-b`) and `unknown`.
    - After `PromoteFolder`, the place `EnabledServers` == {known.Id} and `AdoptedServerNames` == {"known"}.
    - `registry.Servers.Count` is unchanged; no `unknown` was added.
  - `Promote_folder_without_configs_creates_empty_place`: `Clients = None` → a place with `EnabledClients == None`, `Name == "<folder name>"`.
  - `Add_to_library_adopts_identical_occurrences_only`:
    - `jira` is enabled with `tok-a` in place P1 and with `tok-b` in place P2, and with `tok-a` in candidate folder
      C (no place).
    - Result: the library gains one `jira` with `tok-a`. P1 is enabled and adopted. P2 is neither enabled nor
      adopted. No place exists for C.
  - `Add_to_library_unknown_name_throws`: `Assert.Throws<ArgumentException>`.
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~ScanImportPlannerTests"`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement** both methods.
- [ ] **Step 4: Run** `dotnet test`. Expected: all PASS.
- [ ] **Step 5: Commit** `feat: promote found folders and add unknown servers without touching differing entries`

### Task 3: Matrix model reads the snapshot

**Files:**
- Create: `src/McpManager/ViewModels/ProjectChoiceKind.cs`
- Create: `src/McpManager/ViewModels/ProjectChoiceViewModel.cs`
- Create: `src/McpManager/ViewModels/MatrixCellViewModel.cs`
- Create: `src/McpManager/ViewModels/UnknownServerRowViewModel.cs`
- Create: `src/McpManager/ViewModels/CandidateEditedEventArgs.cs`
- Modify: `src/McpManager/ViewModels/MatrixViewModel.cs`, `MatrixRowViewModel.cs`, `PlaceColumnViewModel.cs`
- Modify: `src/McpManager/ViewModels/McpServerViewModel.cs` (add `public McpServer Model => _model;`)
- Test: `tests/McpManager.Tests/MatrixViewModelTests.cs` (update existing; `Cells[i].IsEnabled` becomes `Cells[i].Selection.IsEnabled`; `Projects`/`SelectedProject` become `Choices`/`SelectedChoice`)

**Interfaces:**
- Consumes: `DiskSnapshot`, `ProjectFolderRules` (T1).
- Produces:
  - `enum ProjectChoiceKind { Project, Header, Found, OtherFolder }`.
  - `ProjectChoiceViewModel` with:
    - `Kind`, `Header`, `Path`, `IReadOnlyList<string> Chips`;
    - `bool IsSelectable` (false only for `Header`);
    - `PlaceColumnViewModel? Column` (Project), `ScannedFolder? Folder` (Found).
  - `MatrixCellViewModel(ServerSelectionViewModel selection, ScanServerMatch? difference)` with `Selection`,
    `Difference`, `bool HasDifference`.
  - `MatrixRowViewModel.Cells` is now `IReadOnlyList<MatrixCellViewModel>`.
  - `UnknownServerRowViewModel(string name, IReadOnlyList<bool> foundIn)` with `Name` and `FoundIn` (one entry per
    visible column).
  - `CandidateEditedEventArgs(ScannedFolder folder, IReadOnlyList<ServerSelectionViewModel> selections) : EventArgs`.
  - `PlaceColumnViewModel`:
    - `bool IsCandidate` (ctor parameter, default `false`);
    - `GroupLabel` is `"Found on disk"` when `IsCandidate`, otherwise as now.
  - `MatrixViewModel(IEnumerable<McpServerViewModel> servers, IEnumerable<TargetFolderViewModel> places, Guid? selectedProjectId = null, DiskSnapshot? disk = null, IReadOnlyList<string>? scanRoots = null, string? selectedCandidatePath = null)` with:
    - `ObservableCollection<ProjectChoiceViewModel> Choices`;
    - `[ObservableProperty] ProjectChoiceViewModel? SelectedChoice`;
    - `ObservableCollection<UnknownServerRowViewModel> UnknownRows`, `bool HasUnknownRows`;
    - events `SelectedProjectChanged` (`EventHandler<Guid?>`, unchanged), `OtherFolderRequested` (`EventHandler`),
      `CandidateEdited` (`EventHandler<CandidateEditedEventArgs>`).

Rules:
- **Choices** are built in this order:
  1. projects A–Z;
  2. if any, one `Header` "Found on disk", then every non-global snapshot folder that has no place (`FolderFor`
     over places' models is null), `Clients != None`, and is not `LooksLikeTest` under
     the `scanRoots` ctor argument (null means none). Sorted A–Z by folder name; `Header` is the folder name;
   3. `OtherFolder` "Other folder…".
- **Initial selection:** the project with `selectedProjectId`, else the Found choice whose path is
  `selectedCandidatePath`, else the first project, else null.
- **Choosing `OtherFolder`** restores the previous selection and raises `OtherFolderRequested`.
- **Choosing `Found`** shows a candidate column. It is a transient
  `TargetFolderViewModel(new TargetFolder { Name = header, Path, EnabledClients = folder.Clients }, servers' Model list)`,
  not added to any place list. The selections of enabled Known matches whose library server is in the library are
  ticked. The first `IsEnabled` change on any of its selections raises `CandidateEdited` once, with all its
  selections.
- **Columns** are the globals plus the selected Project or candidate column.
- **Cell difference:** the first match in `disk.MatchesIn(folder)` with `Kind == Differs` and `Library?.Id ==
  server.Id`. The folder is `FolderFor(place.Model)`, or the candidate folder.
- **Unknown rows:** distinct `Found.Name` (A–Z) over matches in visible columns' folders whose `Library` is null or
  whose id is not among `servers`. They respect `ServerFilter`.

- [ ] **Step 1: Write the failing tests:**
  - `Choices_list_projects_header_found_and_other`:
    - Projects `Bbb` and `Aaa`; snapshot folders `<r>/zed` and `<r>/alpha` (CC), plus `<r>/tests/x` (test-looking)
      and a folder equal to the `Aaa` path.
    - `Choices` headers are `["Aaa", "Bbb", "Found on disk", "alpha", "zed", "Other folder…"]`.
    - The header is not selectable.
  - `Other_folder_restores_selection_and_raises_event`.
  - `Found_folder_shows_candidate_column_with_known_ticks_and_raises_candidate_edited_once`:
    - The group label is "Found on disk".
    - The known server is ticked; a differs server is not ticked but `HasDifference`.
    - Toggle two cells → the event is raised exactly once.
  - `Unknown_rows_only_for_visible_columns`:
    - Unknown `jira` is in the global Codex folder; unknown `old` is only in a non-selected found folder.
    - Rows are `["jira"]`, with `FoundIn` true only at the Codex column index.
  - `Stale_snapshot_is_ignored` (Review Focus):
    - A match's `Library.Id` is not among the servers, and a place has been deleted since the snapshot.
    - No exception; no difference markers; the name shows as unknown.
  - Existing tests are updated for the renamed members and keep their assertions.
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~MatrixViewModelTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `dotnet test`. Expected: all PASS. The app project must build, because `MatrixView.axaml`
  binds `Cells`. In this task, change only the cell template binding to `Selection.IsEnabled` and the picker to
  `Choices`/`SelectedChoice` with `Header` text; Task 4 does the full view.
- [ ] **Step 5: Commit** `feat: matrix model shows found folders, unknown servers and differences`

### Task 4: Wire the snapshot, the actions and the view

**Files:**
- Create: `src/McpManager/ViewModels/MainWindowViewModel.Disk.cs`
- Create: `src/McpManager/Views/ResolveDifferenceDialog.axaml` (+ `.axaml.cs`)
- Modify: `src/McpManager/ViewModels/MainWindowViewModel.Management.cs` (`RebuildMatrix` passes `_disk`,
  scan roots and `_candidatePath`; subscribes and unsubscribes the new matrix events)
- Modify: `MainWindowViewModel.cs`:
  - The injecting ctor gains a last optional parameter `DiskSnapshotService? diskService = null`. When it is null,
    `RefreshDiskAsync`/`RematchDiskAsync` return immediately. This keeps every existing test off the real `~/Repos`.
  - The parameterless ctor creates `new DiskSnapshotService(new ConfigScanService(_configExportService), planner)`.
  - At the end of `InitializeAsync`: `_ = RefreshDiskAsync()`.
- Modify: `MainWindowViewModel.Saving.cs` (after a successful save and after every deploy:
  `_ = RefreshDiskAsync()`)
- Modify: `src/McpManager/Views/MatrixView.axaml`
- Test: `tests/McpManager.Tests/DiskActionsTests.cs`

**Interfaces:**
- Consumes:
  - `DiskSnapshotService`, `ConfigScanService.ReadFolderAsync` (T1);
  - `PromoteFolder`, `AddToLibrary`, `Apply` (T2);
  - matrix events and types (T3).
- Produces in `MainWindowViewModel.Disk.cs`:
  - fields `DiskSnapshot _disk = DiskSnapshot.Empty`, `string? _candidatePath`, `int _diskVersion`;
  - `[RelayCommand] private async Task RefreshDiskAsync()`:
    - Calls `SyncModels()`.
    - Clones the registry with `RegistryService.Deserialize(RegistryService.Serialize(_registry))`.
    - Runs `Task.Run(() => service.LoadAsync(clone))`.
    - Drops the result if `_diskVersion` moved on; otherwise sets `_disk` and calls `RebuildMatrix()`.
    - On `IOException`/`UnauthorizedAccessException`/`InvalidDataException` it sets
      `StatusMessage = "Could not read configs: {message}"`.
  - `private async Task RematchDiskAsync()`: the same flow with `Rematch`.
  - `[RelayCommand] private async Task ResolveDifferenceAsync(MatrixCellViewModel cell)`:
    1. Opens `ResolveDifferenceDialog` with a `DiffViewModel("Library", "Found on disk", [diff with ServerName =
       "{name} · {folder name}"], [KeepLibrary, UseFoundVersion, ImportSeparately], (_, c) => c !=
       UseFoundVersion || match.CanUseFoundVersion)`.
    2. On Apply: re-matches the folder on the live registry, finds the same match (`FolderPath`, `Found.Name`,
       `Found.Client`), and calls `planner.Apply(registry, [folder], [match], {match: choice})`.
    3. Then `ReloadAfterRegistryChange(place id, $"Updated {name}. Save to keep it.")`.
    4. An `ArgumentException` goes to `StatusMessage`.
  - `[RelayCommand] private void AddUnknownToLibrary(UnknownServerRowViewModel row)`: `planner.AddToLibrary(registry,
    _disk.Folders, row.Name)`, then `ReloadAfterRegistryChange(current project id, $"Added {name} to your
    library. Save to keep it.")`.
  - The `CandidateEdited` handler:
    1. `PromoteFolder`.
    2. Copies `e.Selections` onto the new place (`IsEnabled` → `EnabledServers`/`DisabledServers` by `ServerId`).
    3. Sets `_candidatePath = null`.
    4. `ReloadAfterRegistryChange(place.Id, "Added project {name}. Save to keep it.")`.
  - The `OtherFolderRequested` handler:
    1. Shows the folder picker, then calls `ReadFolderAsync`.
    2. If the folder is an existing place, selects it.
    3. Else if `Clients != None`: appends it to `_disk` (`_disk with { Folders = [.. _disk.Folders, folder] }`
       plus `Rematch`), sets `_candidatePath`, and rebuilds.
    4. Else: `PromoteFolder`, `ReloadAfterRegistryChange`, opens the place editor (`SelectedTarget = vm`), and sets
       the status "Choose which harnesses {name} uses."
  - `private void ReloadAfterRegistryChange(Guid? selectProjectId, string message)`:
    - follows the `OnScanApplied` body: suppress rebuild, `RefreshFromRegistry`, `AttachManagement`;
    - sets the remembered project id;
    - `RebuildMatrix()`, `CheckTargets()`, `_ = RematchDiskAsync()`;
    - sets `StatusMessage = message`.
- View (`MatrixView.axaml`):
  - **Picker:** the item template switches on `Kind`. Project and Found items show `Header` (SemiBold), `Path`
    (opacity 0.5) and chips. The Header item shows small caps text at opacity 0.6. `OtherFolder` is italic. A
    `ComboBoxItem` style binds `IsEnabled` to `IsSelectable`.
  - A 28×28 refresh icon button sits right of the picker: `MdiRefresh` if it exists in the app resources, otherwise
    the text "↻". Tooltip "Look for changes on disk", command `RefreshDiskCommand`.
  - **Cell:** a 96-wide `Panel`, as now, holds the centred checkbox bound to `Selection.IsEnabled`. When
    `HasDifference`, an 8×8 orange `Ellipse` sits inside a borderless `Button` at the top-right. Tooltip "Differs
    from your library — click to compare", command `ResolveDifferenceCommand`, parameter the cell.
  - **Unknown section** (visible when `HasUnknownRows`, below the rows):
    - a section caption "Found in your configs, not in your library" (FontSize 11, SemiBold, opacity 0.75);
    - each row: the name (220 wide, opacity 0.75) and a 96-wide cell per `FoundIn`, with a 7×7 gray dot when
      true;
    - a "+ Library" button after the cells, command `AddUnknownToLibraryCommand`.
- `ResolveDifferenceDialog` mirrors `ExportConflictDialog`: a `DiffHost` with the shared `DiffView`, and buttons
  Cancel and Apply. Apply is enabled when `DiffViewModel.IsComplete`. It returns the `DiffChoice?` from
  `Choices[0]`.

- [ ] **Step 1: Write the failing tests** in `DiskActionsTests`. Construct `MainWindowViewModel` as
  `PlaceEditorTests.CreateWithExternalChangeAsync` does: a temp `RegistryService`, real export/import services, and
  `ShellStub`. Set `Settings.ScanRoots = [<tmp>/root]`. Pass `diskService: new DiskSnapshotService(new
  ConfigScanService(new ConfigExportService(), _ => [], homeDirectory: <tmp>/home), new ScanImportPlanner(new
  ConfigImportService(), new ConfigExportService()))`.
  - `Startup_refresh_lists_found_folder_in_choices`: `InitializeAsync`, then `RefreshDiskCommand.ExecuteAsync`. A
    choice with `Kind == Found` exists for `<root>/app`.
  - `Editing_candidate_promotes_it_and_marks_dirty`:
    - Select the Found choice and tick a library server.
    - A new place with `Path == <root>/app` exists, its `EnabledServers` contains that server, `SelectedChoice` is
      its Project choice, and the workspace is dirty.
    - `StatusMessage == "Added project app. Save to keep it."`
  - `Add_unknown_to_library_adds_server_and_row_disappears_after_rematch`.
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~DiskActionsTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the view model parts, then the XAML and the dialog.
- [ ] **Step 4: Run** `dotnet test` and `dotnet build src/McpManager`. Expected: all PASS, 0 errors.
- [ ] **Step 5: Commit** `feat: matrix acts on disk state - promote, add to library, resolve differences`

### Task 5: Remove the Scan page

**Files:**
- Delete:
  - `src/McpManager/Views/ScanView.axaml` and `ScanView.axaml.cs`;
  - `src/McpManager/ViewModels/ScanViewModel.cs`, `ScannedFolderViewModel.cs`;
  - `src/McpManager/ViewModels/MainWindowViewModel.Scanning.cs`: move the scan-root members (`ScanRoots`,
    `HasScanRoots`, `DefaultScanRootsText`, `LoadScanRoots`, `AddScanRootAsync`, `RemoveScanRoot`,
    `OnScanRootsChanged`) into `MainWindowViewModel.Disk.cs`. `AddScanRootAsync` and `RemoveScanRoot` also call
    `_ = RefreshDiskAsync()`.
  - `tests/McpManager.Tests/ScanViewModelTests.cs`: before deleting, move any assertion not already covered by
    `ScanImportPlannerTests`/`DiskSnapshotServiceTests` into those files.
- Modify:
  - `MainWindow.axaml`: remove the `ScanView` host and the overview "Scan" and "Add place" buttons; keep "Export…";
    rename the Preferences scan-roots label to "Folders to look for projects in";
  - `NavigationSnapshot.cs` and `MainWindowViewModel.cs`: remove `ShowScan`;
  - `README.md`: replace the Scan section with the picker, unknown rows and difference dots.
- Test: `dotnet test`.

- [ ] **Step 1: Delete and modify** as listed. `grep -rn "Scan(View|ViewModel)|ShowScan|OpenScan|ScannedFolderViewModel" src tests` returns nothing.
- [ ] **Step 2: Run** `dotnet test` and `dotnet build src/McpManager`. Expected: all PASS, 0 errors.
- [ ] **Step 3: Commit** `refactor: drop the scan page now that the matrix shows disk state`
