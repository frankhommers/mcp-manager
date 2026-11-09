# Simpler UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the profile/target UI with a servers × places matrix, a folder scan that adopts existing configs, a shared field-level diff screen, and one Save button with an opt-in "Also deploy" checkbox.

**Architecture:** All decision logic moves into small, testable services in `McpManager.Core` (deploy, status, scan, diff, change tracking). `MainWindowViewModel` only wires them to the UI. The Avalonia views get a new matrix page, scan page, diff control and save/deploy bars; profile UI and bulk buttons go away.

**Tech Stack:** .NET 10 (SDK 10.0.403), C# latest, Avalonia 11.3, CommunityToolkit.Mvvm, xUnit 2.9, Tomlyn, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-11-09-simpler-ui-design.md`

## Global Constraints

- Follow `CLAUDE.md`: Allman braces, 2-space indent, explicit types (no `var`), `_camelCase` fields, `Async` suffix, one type per file, `ConfigureAwait(false)` in Core, no comments unless important.
- App UI text is English. User-facing terms: **Server**, **Place**, **Save**, **Deploy**. "Target" and "Profile" must not appear in the main UI.
- Workspace stays `formatVersion: 2`. Only additive fields: `TargetFolder.AdoptedServerNames`, `GlobalSettings.ScanRoots`.
- Never write client config files except on an explicit Deploy (Save with "Also deploy" checked, **Deploy now**, place **Deploy**, or **Overwrite** in the external-change diff).
- "Also deploy" checkbox is unchecked every time the workspace becomes dirty. Never persisted.
- Secrets are never shown unmasked unless the user clicks **Show** for that value.
- Commit messages: conventional prefix, explain why, no AI/tool attribution, no Co-Authored-By.
- Run all tests with `dotnet test McpManager.slnx` from the worktree root; baseline is 165 passing.

## Review Focus

1. **Secrets inside args** (e.g. `docker run -e JIRA_API_TOKEN=ATATT…`): must be masked in the diff like env values. Test in Task 4.
2. **Scan over a huge tree / symlink loops / unreadable folders**: must not hang or crash; symlinked dirs not followed, `UnauthorizedAccessException` skipped. Test in Task 6.
3. **Malformed config file found by scan** (invalid JSON/TOML): the folder is listed with "unreadable" and no servers, the rest of the scan continues. Test in Task 6.
4. **Never-deployed place with a same-name unmanaged entry**: status must not throw; it counts as "not deployed yet" and the file counts toward N. Test in Task 8.
5. **Revert after edits to servers *and* places**: Cancel restores every VM from the saved snapshot, including newly added servers/places being removed. Test in Task 9.

---

### Task 1: Extract deploy into `TargetDeployService` and allow multi-harness places

**Files:**
- Create: `src/McpManager.Core/Services/TargetDeployService.cs`, `src/McpManager.Core/Models/DeployResult.cs`
- Modify: `src/McpManager/ViewModels/MainWindowViewModel.Management.cs` (`UpdateTargetsAsync` delegates to the service), `src/McpManager/ViewModels/TargetFolderViewModel.cs` (`GetEnabledClients`: managed project targets use the `Enable*` flags, not `SelectedClient`; add `EnableCursor`, `EnableVsCode` observable bools mirroring the existing ones)
- Test: `tests/McpManager.Tests/TargetDeployServiceTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record DeployResult(bool Cancelled, IReadOnlyList<string> WrittenFiles);`
  - `public class TargetDeployService(IConfigExportService exportService)`
  - `public async Task<DeployResult> DeployAsync(IReadOnlyList<TargetFolder> targets, McpRegistry registry, Func<ExportConflict, Task<ExportConflictResolution?>> resolveConflictAsync, bool overwriteExternal = false)`
    - Throws `InvalidOperationException` when any target has external changes and `overwriteExternal` is false, when a never-deployed target contains managed servers missing from the library, or when a target has no client / non-absolute path.
    - For never-deployed targets: enables (in `EnabledServers`) library servers whose managed ID is present in the target's files, unless in `DisabledServers` (moved from the VM loop).
    - Same pre/post hash race check, `.claude/settings.json` handling for `ClaudeCodeGlobal`, `RecordApplied` per target, and clears `AdoptedServerNames` of each deployed target after a successful write.
    - Does **not** save the registry; caller saves.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public async Task Place_with_claude_code_and_opencode_writes_both_files_and_records_both_hashes()
{
  // target: Path=_directory, EnabledClients = ClaudeCode | OpenCode, EnabledServers = [server.Id]
  DeployResult result = await new TargetDeployService(new ConfigExportService())
    .DeployAsync([target], registry, _ => Task.FromResult<ExportConflictResolution?>(null));
  Assert.False(result.Cancelled);
  Assert.True(File.Exists(Path.Combine(_directory, ".mcp.json")));
  Assert.True(File.Exists(Path.Combine(_directory, "opencode.jsonc")));
  Assert.Equal(2, target.AppliedFileHashes.Count);
  Assert.NotNull(target.LastAppliedAt);
}

[Fact]
public async Task External_change_blocks_deploy_unless_overwrite() { /* deploy, edit .mcp.json, deploy again → InvalidOperationException; with overwriteExternal: true → succeeds */ }

[Fact]
public async Task Cancelled_conflict_writes_nothing() { /* unmanaged same-name entry in .mcp.json, resolver returns null → result.Cancelled, file bytes unchanged */ }
```

- [ ] **Step 2: Run** `dotnet test McpManager.slnx --filter TargetDeployServiceTests` — Expected: FAIL (type missing).
- [ ] **Step 3: Implement** `TargetDeployService` by moving the body of `UpdateTargetsAsync` (Management.cs) minus VM status updates; drop the `(client & (client - 1)) != 0` single-client rule (keep "no client" and absolute-path checks). Make `UpdateTargetsAsync` call it, save the registry before and after, then set VM `SyncStatus`/`NeedsUpdate` as today. Update `GetEnabledClients` and the constructor to initialise `EnableCursor`/`EnableVsCode`.
- [ ] **Step 4: Run** `dotnet test McpManager.slnx` — Expected: all pass.
- [ ] **Step 5: Commit** `fix: deploy places with several harnesses` (why: Melvin, 3D Drawings and Homelab could not be updated).

---

### Task 2: Use `.opencode/opencode.json` when it exists

**Files:**
- Modify: `src/McpManager.Core/Services/ConfigExportService.cs` (`GetConfigFilePath`)
- Test: `tests/McpManager.Tests/ConfigGeneratorTests.cs` (add test)

**Interfaces:**
- Produces: OpenCode path precedence: `<path>/.opencode/opencode.json` if it exists → `<path>/opencode.jsonc` if it exists → `<path>/opencode.json` if it exists → `<path>/opencode.jsonc`.

- [ ] **Step 1: Write failing test** `OpenCode_uses_nested_opencode_folder_when_present`: create `.opencode/opencode.json`, assert `GetConfigFilePaths(target)[TargetClientFlags.OpenCode] == Path.Combine(dir, ".opencode", "opencode.json")`; and when both nested and root `opencode.jsonc` exist, nested wins.
- [ ] **Step 2: Run** filter `OpenCode_uses_nested` — FAIL.
- [ ] **Step 3: Implement** precedence in `GetConfigFilePath`.
- [ ] **Step 4: Run** full suite — PASS.
- [ ] **Step 5: Commit** `fix: write OpenCode config where the project keeps it`.

---

### Task 3: Adopted names and client on conflicts

**Files:**
- Modify: `src/McpManager.Core/Models/McpRegistry.cs` (`TargetFolder.AdoptedServerNames`), `src/McpManager.Core/Models/ExportConflict.cs`, `src/McpManager.Core/Services/ManagedConfigMerger.cs`, `src/McpManager.Core/Services/ConfigExportService.cs` (`PreviewConfigs`, `GenerateMergedConfig` pass client + adopted names)
- Test: `tests/McpManager.Tests/ManagedExportTests.cs`, `tests/McpManager.Tests/ExportConflictTests.cs`

**Interfaces:**
- Produces:
  - `public HashSet<string> AdoptedServerNames { get; set; } = [];` on `TargetFolder`
  - `public sealed record ExportConflict(string FilePath, string ServerName, string ExistingConfiguration, string ProposedConfiguration, TargetClientFlags Client);`
  - `ManagedConfigMerger.Merge(string filePath, IConfigGenerator generator, string generated, TargetClientFlags client, IReadOnlyDictionary<ExportConflict, ExportConflictResolution>? resolutions = null, IReadOnlySet<string>? adoptedNames = null)`; `MergeJson`/`MergeToml` gain `TargetClientFlags client` (after `filePath`) and `IReadOnlySet<string>? adoptedNames = null`. A same-name unmanaged entry whose name is in `adoptedNames` is replaced without raising `ExportConflictException`.

- [ ] **Step 1: Write failing tests** `Adopted_name_replaces_unmanaged_entry_without_conflict` (MergeJson with adoptedNames `{"atlassian-argaleo"}` → no exception, result contains the managed version) and `Conflict_reports_client` (OpenCode file conflict → `ex.Conflict.Client == TargetClientFlags.OpenCode`). Update existing `MergeJson` call sites in tests to pass `TargetClientFlags.ClaudeCode`.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.** `PreviewConfigs` passes the client of each block and `target.AdoptedServerNames`.
- [ ] **Step 4: Run** full suite — PASS.
- [ ] **Step 5: Commit** `feat: let adopted servers replace same-name entries`.

---

### Task 4: Secret masking and server diff

**Files:**
- Create: `src/McpManager.Core/Services/SecretMask.cs`, `src/McpManager.Core/Services/ServerDiffService.cs`, `src/McpManager.Core/Models/ServerDiff.cs`, `src/McpManager.Core/Models/FieldDiff.cs`, `src/McpManager.Core/Models/DiffKind.cs`, `src/McpManager.Core/Models/DiffToken.cs`
- Test: `tests/McpManager.Tests/ServerDiffServiceTests.cs`

**Interfaces:**
- Produces:
  - `public enum DiffKind { Same, Added, Removed, Changed }`
  - `public sealed record DiffToken(string Text, DiffKind Kind, bool IsSecret);`
  - `public sealed record FieldDiff(string Field, DiffKind Kind, bool IsSecret, IReadOnlyList<DiffToken> Left, IReadOnlyList<DiffToken> Right);` — Field names: `type`, `command`, `args`, `url`, `cwd`, `env:<KEY>`, `header:<KEY>`, `tools`.
  - `public sealed record ServerDiff(string ServerName, IReadOnlyList<FieldDiff> Fields) { public bool HasDifferences => Fields.Any(f => f.Kind != DiffKind.Same); }`
  - `public static class SecretMask { public static bool IsSecretKey(string key); public static string Mask(string value); }` — `IsSecretKey`: case-insensitive contains `TOKEN`, `KEY`, `SECRET`, `PASSWORD`, `AUTH`, or equals `Authorization`. `Mask`: `"••••••••" + last 4 chars` (values of length ≤ 4 → `"••••••••"`).
  - `public static class ServerDiffService { public static ServerDiff Compare(McpServer left, McpServer right); }` — ignores `ManagedServerIdentity.EnvironmentKey` and `HeaderKey`; args compared token-wise with LCS so inserted args are `Added` and others `Same`; an arg `NAME=value` with secret-like `NAME`, or an arg following a flag whose name is secret-like (`--token`, `--api-key`), is `IsSecret`; env/header values with secret-like keys are `IsSecret`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Inserted_arg_is_added_and_rest_same()
{
  // left args: run --rm -i mcp/atlassian ; right: run --rm -i --name atlassian-argaleo-mcp mcp/atlassian
  FieldDiff args = ServerDiffService.Compare(left, right).Fields.Single(f => f.Field == "args");
  Assert.Equal(DiffKind.Changed, args.Kind);
  Assert.Equal(["--name", "atlassian-argaleo-mcp"], args.Right.Where(t => t.Kind == DiffKind.Added).Select(t => t.Text));
}

[Fact]
public void Token_in_docker_env_arg_is_secret() { /* "-e", "JIRA_API_TOKEN=abcd1234" → token IsSecret */ }

[Fact]
public void Different_secrets_with_same_last_four_are_still_changed() { /* env API_KEY xx1234 vs yy1234 → Kind Changed */ }

[Fact]
public void Managed_markers_are_ignored() { /* MCP_MANAGER_ID only on one side → HasDifferences false */ }

[Theory]
[InlineData("JIRA_API_TOKEN", true)] [InlineData("Authorization", true)] [InlineData("JIRA_URL", false)]
public void Secret_keys(string key, bool expected) => Assert.Equal(expected, SecretMask.IsSecretKey(key));

[Fact]
public void Mask_shows_last_four() => Assert.Equal("••••••••7be5", SecretMask.Mask("ATATT3xFf7be5"));
```

- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat: field-level server diff with masked secrets`.

---

### Task 5: File-level diff

**Files:**
- Create: `src/McpManager.Core/Services/ConfigFileDiffService.cs`, `src/McpManager.Core/Models/ConfigFileDiff.cs`, `src/McpManager.Core/Models/ServerEntryDiff.cs`
- Modify: `src/McpManager.Core/Services/TargetConfigSelectionService.cs` (expose parsing)
- Test: `tests/McpManager.Tests/ConfigFileDiffServiceTests.cs`

**Interfaces:**
- Consumes: `ServerDiffService.Compare`, `IConfigImportService.ImportServer`, `ManagedServerIdentity.RestoreImportedId`.
- Produces:
  - `public static IReadOnlyList<ExistingTargetServer> TargetConfigSelectionService.Parse(string content, TargetClientFlags client, string path)` (wraps existing private `ReadJson`/`ReadToml`).
  - `public sealed record ServerEntryDiff(string ServerName, DiffKind Kind, ServerDiff? Details);` — `Added` = only on right, `Removed` = only on left.
  - `public sealed record ConfigFileDiff(string FilePath, TargetClientFlags Client, IReadOnlyList<ServerEntryDiff> Servers);`
  - `public class ConfigFileDiffService(IConfigImportService importService)` with `public ConfigFileDiff Compare(string filePath, TargetClientFlags client, string? leftContent, string rightContent)` (left null = file missing) and `public ServerDiff CompareConflict(ExportConflict conflict)`.

- [ ] **Step 1: Write failing tests** `Detects_added_removed_and_changed_servers` (Claude Code JSON with servers a,b vs b',c → a Removed, b Changed, c Added) and `Conflict_compare_parses_codex_toml` (ExportConflict with Codex client and `[server]` tables → `HasDifferences` true for a changed command).
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement** by parsing both sides with `Parse`, importing each entry via `ImportServer` + `RestoreImportedId`, matching by name.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat: compare config files per server`.

---

### Task 6: Folder scan

**Files:**
- Create: `src/McpManager.Core/Services/ConfigScanService.cs`, `src/McpManager.Core/Models/ScannedFolder.cs`
- Modify: `src/McpManager.Core/Models/McpRegistry.cs` (`GlobalSettings.ScanRoots`), `src/McpManager.Core/Services/RegistryService.cs` (`public static IReadOnlyList<string> GetDefaultScanRoots(GlobalSettings settings)`)
- Test: `tests/McpManager.Tests/ConfigScanServiceTests.cs`

**Interfaces:**
- Consumes: `IConfigExportService.GetConfigFilePaths`, `TargetConfigSelectionService.ReadAsync`.
- Produces:
  - `public List<string> ScanRoots { get; set; } = [];` on `GlobalSettings`. `GetDefaultScanRoots`: `ScanRoots` if non-empty, else `DefaultProjectsRoot` if set, else `~/Repos` if it exists, else home.
  - `public sealed record ScannedFolder(string Path, bool IsGlobal, TargetClientFlags Clients, IReadOnlyList<ExistingTargetServer> Servers, IReadOnlyList<string> UnreadableFiles, bool HarnessesDiffer);`
  - `public class ConfigScanService(IConfigExportService exportService)` with `public const int MaxDepth = 4;` and `public async Task<IReadOnlyList<ScannedFolder>> ScanAsync(IEnumerable<string> roots, GlobalSettings settings, CancellationToken cancellationToken = default)`.
    - Visits directories at depth 0..`MaxDepth` below each root; skips names starting with `.`, `node_modules`, `bin`, `obj`; never follows symlinked directories; skips `UnauthorizedAccessException`/`IOException`.
    - Per directory, detects project clients by file existence: ClaudeCode `.mcp.json`; OpenCode `.opencode/opencode.json`, `opencode.jsonc`, `opencode.json`; Codex `.codex/config.toml`; Cursor `.cursor/mcp.json`; VsCode `.vscode/mcp.json`. Reads servers with `TargetConfigSelectionService.ReadAsync` on a temporary `TargetFolder`.
    - Adds one `IsGlobal` entry per global harness whose file exists (Claude Code global, Codex global, OpenCode global, Claude Desktop paths from `RegistryService`/settings).
    - `HarnessesDiffer` = the set of server names differs between the folder's harness files.
    - Results ordered by path; duplicate paths from overlapping roots appear once.

- [ ] **Step 1: Write failing tests** in a temp tree: `Finds_folders_with_harnesses` (`a/.mcp.json` + `a/opencode.jsonc` → one folder, clients ClaudeCode|OpenCode), `Skips_node_modules_hidden_and_too_deep` (depth 5 folder not found), `Does_not_follow_symlinked_directories` (symlink loop terminates), `Malformed_file_is_reported_unreadable_and_scan_continues`, `Harnesses_differ_flag`, `Scan_writes_nothing` (all file bytes/mtimes unchanged).
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement** with an explicit stack (no recursion), honouring `cancellationToken`.
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat: find existing MCP configs in project folders`.

---

### Task 7: Turn scan results into places

**Files:**
- Create: `src/McpManager.Core/Services/ScanImportPlanner.cs`, `src/McpManager.Core/Models/ScanServerMatch.cs`, `src/McpManager.Core/Models/ScanMatchKind.cs`, `src/McpManager.Core/Models/ScanChoice.cs`
- Test: `tests/McpManager.Tests/ScanImportPlannerTests.cs`

**Interfaces:**
- Consumes: `ScannedFolder`, `ServerDiffService.Compare`, `IConfigImportService.ImportServer`, `ManagedServerIdentity.RestoreImportedId`.
- Produces:
  - `public enum ScanMatchKind { Known, Differs, Unknown }`
  - `public enum ScanChoice { KeepLibrary, UseFoundVersion, ImportSeparately }`
  - `public sealed record ScanServerMatch(string FolderPath, ExistingTargetServer Found, McpServer Imported, McpServer? Library, ScanMatchKind Kind, ServerDiff? Diff);` — match by managed ID first, then by name.
  - `public class ScanImportPlanner(IConfigImportService importService)`:
    - `public IReadOnlyList<ScanServerMatch> Match(IEnumerable<ScannedFolder> folders, IReadOnlyList<McpServer> library)` — one entry per distinct (folder, server name); for a name found in several harness files of a folder, the first file in client order is used.
    - `public void Apply(McpRegistry registry, IEnumerable<ScannedFolder> selected, IReadOnlyList<ScanServerMatch> matches, IReadOnlyDictionary<ScanServerMatch, ScanChoice> choices)` — creates or updates one `TargetFolder` per selected folder (name = folder name, `EnabledClients` = detected, `IsGlobal` for global entries, `EnabledServers` = union). Unknown → imported into library once (first occurrence), later folders reuse it. `KeepLibrary` → use library server. `UseFoundVersion` → overwrite library server's definition fields (command, args, url, type, cwd, env, headers), keep its Id. `ImportSeparately` → add as new server named `<name>-<folder name>`, unique. Every server name the place takes over is added to `AdoptedServerNames`. Existing places (same full path, or same global client) are updated, not duplicated. Throws `ArgumentException` if a `Differs` match has no choice.

- [ ] **Step 1: Write failing tests** `Unknown_server_is_imported_once_across_folders`, `Differs_requires_a_choice`, `Use_found_version_updates_library_keeping_id`, `Import_separately_creates_unique_name`, `Union_of_harness_servers_and_adopted_names`, `Rescan_updates_existing_place_instead_of_duplicating`.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat: adopt scanned configs as places`.

---

### Task 8: Place status and deploy count

**Files:**
- Create: `src/McpManager.Core/Services/PlaceStatusService.cs`, `src/McpManager.Core/Models/PlaceStatus.cs`
- Test: `tests/McpManager.Tests/PlaceStatusServiceTests.cs`

**Interfaces:**
- Produces:
  - `public enum PlaceStatus { NotDeployedYet, UpToDate, Behind, ExternallyChanged, NeedsAttention }`
  - `public class PlaceStatusService(IConfigExportService exportService)`:
    - `public PlaceStatus GetStatus(TargetFolder target, McpRegistry registry)` — `ExternallyChanged` if `TargetSyncService.HasExternalChanges`; `NotDeployedYet` if `LastAppliedAt == null`; `Behind` if any preview file's content hash differs from `AppliedFileHashes` (missing key counts); else `UpToDate`. `ExportConflictException` → `NotDeployedYet` when never deployed, otherwise `NeedsAttention`; other exceptions → `NeedsAttention`. Uses `ProfileResolver.Resolve`.
    - `public int CountChangedFiles(McpRegistry saved, McpRegistry current)` — for every managed target (not clipboard/quick export) in `current`, compare preview of `current` vs preview of the same target id in `saved` (missing in saved = all its files); for targets whose previews differ, count files whose current preview hash differs from disk (`FileHash`). A preview that throws `ExportConflictException` counts all its files.

- [ ] **Step 1: Write failing tests** `Never_deployed_is_not_behind`, `Saved_change_after_deploy_is_behind`, `External_edit_is_externally_changed`, `Never_deployed_with_unmanaged_same_name_entry_does_not_throw`, `Count_changed_files_only_for_affected_places` (two places, server enabled in one → count = that place's files).
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat: tell deployed, behind and never-deployed places apart`.

---

### Task 9: Unsaved-change tracking

**Files:**
- Create: `src/McpManager.Core/Services/WorkspaceChangeTracker.cs`
- Modify: `src/McpManager.Core/Services/RegistryService.cs` (`public static string Serialize(McpRegistry registry)` and `public static McpRegistry Deserialize(string json)` using the existing `JsonOptions`; `SaveAsync`/`LoadAsync` use them)
- Test: `tests/McpManager.Tests/WorkspaceChangeTrackerTests.cs`

**Interfaces:**
- Produces: `public class WorkspaceChangeTracker` with `public void MarkSaved(McpRegistry registry)`, `public bool IsDirty(McpRegistry registry)`, `public McpRegistry RestoreSaved()` (fresh deep copy), `public McpRegistry SavedCopy()` (fresh deep copy, used by `CountChangedFiles`).

- [ ] **Step 1: Write failing tests** `Clean_after_mark_saved`, `Dirty_after_server_edit_and_after_new_place`, `Restore_returns_saved_state_without_new_server_or_place`, `Restored_copy_is_independent` (mutating it doesn't change the tracker).
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat: track unsaved workspace changes`.

---

### Task 10: Diff control and new conflict dialog

**Files:**
- Create: `src/McpManager/ViewModels/DiffViewModel.cs`, `src/McpManager/ViewModels/FieldDiffViewModel.cs`, `src/McpManager/ViewModels/DiffChoice.cs`, `src/McpManager/Views/DiffView.axaml(.cs)`
- Modify: `src/McpManager/Views/ExportConflictDialog.axaml(.cs)` (host `DiffView`; buttons stay Replace with MCP Manager / Keep existing / Cancel export), `src/McpManager/ViewModels/MainWindowViewModel.cs` (`ResolveExportConflictAsync` passes a `DiffViewModel` built from `ConfigFileDiffService.CompareConflict`)
- Test: `tests/McpManager.Tests/DiffViewModelTests.cs`

**Interfaces:**
- Consumes: `ServerDiff`, `FieldDiff`, `SecretMask`.
- Produces:
  - `public enum DiffChoice { KeepLibrary, UseFoundVersion, ImportSeparately, Replace, KeepExisting, Overwrite, KeepFile }`
  - `public partial class DiffViewModel : ViewModelBase` — ctor `(string leftLabel, string rightLabel, IReadOnlyList<ServerDiff> items, IReadOnlyList<DiffChoice> choices)`; properties `Current` (`ServerDiff`), `Fields` (`IReadOnlyList<FieldDiffViewModel>`), `Position` (`"1 of 3"`), `SelectedChoice` (`DiffChoice?`, per item), `CanGoNext`, `IsComplete` (every item has a choice); commands `Next`, `Previous`; `IReadOnlyList<DiffChoice?> Choices`.
  - `public partial class FieldDiffViewModel` — `Field`, `Kind`, `LeftText`, `RightText` (masked unless `IsRevealed`), `ToggleRevealCommand`; `IsDimmed` when `Kind == Same`.
- View: two columns with labels, equal rows dimmed, added tokens green background, removed tokens red + strikethrough (theme brushes), args one token per line, **Show** button per secret value, choice radio buttons, Previous/Next.

- [ ] **Step 1: Write failing tests** `Secrets_masked_until_revealed`, `Cannot_complete_until_every_item_has_choice`, `Navigation_keeps_choice_per_item`.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement** VM and views; delete the two raw JSON text boxes from `ExportConflictDialog`.
- [ ] **Step 4: Run** `dotnet test McpManager.slnx` and `dotnet build McpManager.slnx` — PASS, no new warnings.
- [ ] **Step 5: Commit** `feat: readable diff for conflicts`.

---

### Task 11: Matrix overview

**Files:**
- Create: `src/McpManager/ViewModels/MatrixViewModel.cs`, `src/McpManager/ViewModels/PlaceColumnViewModel.cs`, `src/McpManager/ViewModels/MatrixRowViewModel.cs`, `src/McpManager/Views/MatrixView.axaml(.cs)`
- Modify: `src/McpManager/Views/MainWindow.axaml` (Overview page hosts `MatrixView`; remove old overview list with bulk checkboxes, "Assign profile", "Update selected"), `src/McpManager/ViewModels/MainWindowViewModel.Management.cs` (expose `Matrix`, rebuild on server/place add/remove/revert, refresh status after checks)
- Test: `tests/McpManager.Tests/MatrixViewModelTests.cs`

**Interfaces:**
- Consumes: `TargetFolderViewModel.ServerSelections` (`ServerSelectionViewModel.IsEnabled`), `PlaceStatus`.
- Produces:
  - `public partial class MatrixViewModel : ViewModelBase` — ctor `(IEnumerable<McpServerViewModel> servers, IEnumerable<TargetFolderViewModel> places)`; `Columns` (managed places only: global first by client name, then projects by name), `Rows` (servers A–Z by name), `[ObservableProperty] string ServerFilter`, `[ObservableProperty] string PlaceFilter` (case-insensitive contains on name/path), `void SetStatus(Guid placeId, PlaceStatus status)`.
  - `PlaceColumnViewModel` — `Place`, `Header` (folder name or client name), `Chips` (e.g. `["CC","OC"]`; CC ClaudeCode/ClaudeCodeGlobal, CD ClaudeDesktop, OC OpenCode, CX Codex, CU Cursor, VS VsCode, WS Windsurf), `IsGlobal`, `Status`, `StatusText`.
  - `MatrixRowViewModel` — `Server`, `Cells` (`IReadOnlyList<ServerSelectionViewModel>` aligned with visible `Columns`). Checking a cell sets `IsEnabled` on that place's selection, which already marks the place configuration changed.
- View: outer vertical `ScrollViewer` with a two-column grid: fixed server-name column (clicking a name selects that server), and a horizontal `ScrollViewer` with the group header row ("Global" / "Projects"), column headers (clicking selects that place; status dot for Behind/ExternallyChanged, hollow dot for NotDeployedYet) and checkbox cells (fixed 72 px columns).

- [ ] **Step 1: Write failing tests** `Columns_exclude_clipboard_and_quick_export_global_first`, `Cell_toggles_place_selection`, `Place_filter_hides_columns_and_cells_stay_aligned`, `Chips_for_claude_code_and_opencode`.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** tests + build — PASS.
- [ ] **Step 5: Commit** `feat: servers by places overview`.

---

### Task 12: Save, deploy and unsaved-changes flow

**Files:**
- Create: `src/McpManager/Views/UnsavedChangesDialog.axaml(.cs)`, `src/McpManager/ViewModels/UnsavedChoice.cs`, `src/McpManager/ViewModels/MainWindowViewModel.Saving.cs`
- Modify: `src/McpManager/ViewModels/MainWindowViewModel.cs` (`SaveAsync` replaced; `InitializeAsync` calls `MarkSaved`), `MainWindowViewModel.Management.cs` (`CheckTargets` updates `IsDirty`, `ChangedFileCount`, `BehindCount`, column status via `PlaceStatusService`), `MainWindowViewModel.Sidebar.cs`, `src/McpManager/Views/MainWindow.axaml` (save bar + deploy bar; remove **Save all**, **Update all**, PROFILES section, profile picker in the place editor, Profile page)
- Test: `tests/McpManager.Tests/SaveFlowTests.cs`

**Interfaces:**
- Consumes: `WorkspaceChangeTracker`, `PlaceStatusService`, `TargetDeployService`, `RegistryService.Serialize/Deserialize`.
- Produces on `MainWindowViewModel`:
  - `[ObservableProperty] bool IsDirty`, `[ObservableProperty] bool AlsoDeploy` (set to `false` whenever `IsDirty` turns from false to true), `[ObservableProperty] int ChangedFileCount`, `[ObservableProperty] int BehindCount`, `string AlsoDeployText` (`"Also deploy (3 files)"` / `"Also deploy (no files change)"`, checkbox disabled at 0).
  - `[RelayCommand] Task SaveAsync()` — validates (stdio server needs a command; HTTP/SSE/Streamable HTTP needs an absolute `http(s)` URL; place needs ≥1 harness and an absolute path); on failure sets `StatusMessage` naming the field and returns without saving. Saves, `MarkSaved`, then if `AlsoDeploy` deploys every managed place whose status became `Behind` or whose preview changed (same set as `CountChangedFiles`), via `TargetDeployService`, saves again.
  - `[RelayCommand] void Cancel()` — `RestoreSaved()`, rebuild servers/places/matrix, reselect by id when still present.
  - `[RelayCommand] Task DeployNowAsync()` — deploys all `Behind` places.
  - `[RelayCommand] Task DeployPlaceAsync(TargetFolderViewModel place)`.
  - `public async Task<bool> ConfirmLeaveAsync()` — when dirty, asks via `Func<Task<UnsavedChoice>> PromptUnsavedAsync` (defaults to `UnsavedChangesDialog`; tests replace it): `Save` → `SaveAsync` then true if clean; `Discard` → `Cancel` then true; `Stay` → false. Sidebar/list selection changes go through it; on `false` the previous selection is restored.
  - `public enum UnsavedChoice { Save, Discard, Stay }`
- Bars: save bar (only when dirty): "● Unsaved changes", **Cancel**, checkbox, **Save**. Deploy bar (when not dirty and `BehindCount > 0`): "N places not deployed", **Review** (opens `DiffView` per file, read-only, choices Deploy/Close), **Deploy now**.

- [ ] **Step 1: Write failing tests** using `MainWindowViewModel` with `RegistryService(tempPath)`, real export/import services and a fake `IShellEnvironmentService`: `Editing_marks_dirty_and_resets_also_deploy`, `Save_without_deploy_writes_workspace_only`, `Save_with_deploy_writes_affected_place_files`, `Cancel_restores_saved_state`, `Invalid_stdio_server_blocks_save`, `Stay_keeps_selection`, `Never_deployed_place_not_counted_in_behind`.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** tests + build — PASS.
- [ ] **Step 5: Commit** `feat: one Save button with optional deploy`.

---

### Task 13: Scan page and walk-through

**Files:**
- Create: `src/McpManager/ViewModels/ScanViewModel.cs`, `src/McpManager/ViewModels/ScannedFolderViewModel.cs`, `src/McpManager/Views/ScanView.axaml(.cs)`
- Modify: `MainWindow.axaml` (Scan button on matrix toolbar opens the scan page; Preferences gets a "Scan folders" list editor bound to `GlobalSettings.ScanRoots`), `MainWindowViewModel.cs` (remove old `ScanForMcpConfigsAsync` and its `.mcp.json`-only import)
- Test: `tests/McpManager.Tests/ScanViewModelTests.cs`

**Interfaces:**
- Consumes: `ConfigScanService`, `ScanImportPlanner`, `DiffViewModel`, `RegistryService.GetDefaultScanRoots`.
- Produces: `ScanViewModel` — `Folders` (`ScannedFolderViewModel`: `Path`, `ClientChips`, `IsNew`, `IsSelected` (new = true, path containing `test` case-insensitive = false and `Note = "Looks like test data"`), `Summary` like `"2 recognized · 1 differs · 1 unknown"`, `"Harnesses differ"`, `"Unreadable: <file>"`, and `"New harness found"` for a known place whose folder gained a harness file — selected by default); `[RelayCommand] Task ScanAsync()`; `[RelayCommand] Task AddSelectedAsync()` → if any `Differs`, shows `DiffViewModel` walk-through (left "Library", right `"Found in <relative file>"`, choices KeepLibrary/UseFoundVersion/ImportSeparately); cancel = nothing changes; on completion calls `ScanImportPlanner.Apply`, refreshes VMs/matrix. Result is an unsaved change (save bar appears); nothing written to config files.

- [ ] **Step 1: Write failing tests** `Test_folders_unselected_by_default`, `Add_without_differences_creates_places_and_marks_dirty`, `Cancelled_walkthrough_changes_nothing`.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** tests + build — PASS.
- [ ] **Step 5: Commit** `feat: scan folders and adopt their configs`.

---

### Task 14: Place editor, export menu, README

**Files:**
- Modify: `MainWindow.axaml` (place editor: harness checkboxes for project places, **Deploy** button, status text; external change: **Compare** opens `DiffView` disk-vs-MCP-Manager per file with **Overwrite with MCP Manager** → `TargetDeployService.DeployAsync(..., overwriteExternal: true)` and **Keep file** → set `AppliedFileHashes` to current disk hashes, nothing written; remove the old "Reapply central settings" button; Clipboard and Quick Export move into an **Export…** menu button on the toolbar, otherwise unchanged; sidebar section "TARGETS" renamed "PLACES"), `NewTargetDialog.axaml` (title "New place", multi-harness checkboxes for project places), `README.md` (replace "Central management" and "Updating and conflicts" with the Save/Deploy/Scan/matrix workflow; drop profiles from features)
- Test: `tests/McpManager.Tests/PlaceEditorTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 5, 8, 10, 12.
- Produces: `[RelayCommand] Task CompareExternalChangesAsync()` and `[RelayCommand] void KeepExternalFile()` on `MainWindowViewModel`.

- [ ] **Step 1: Write failing tests** `Keep_file_accepts_disk_state_without_writing`, `Overwrite_deploys_over_external_change`.
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.** Grep `MainWindow.axaml` and view models for user-visible "Target"/"Profile" strings and replace with "Place" / remove.
- [ ] **Step 4: Run** tests + build; launch with `dotnet run --project src/McpManager/McpManager.csproj` against a copy of the workspace (`RegistryService` custom path via a temp `HOME`/AppData) and check: matrix shows, Scan finds `~/Repos/_argaleo`, Save bar appears on edit, Cancel reverts.
- [ ] **Step 5: Commit** `feat: place editor and export menu for the simpler UI`.

---

## After the plan (not code)

With the app closed and a backup of `~/Library/Application Support/McpManager/workspace.json`, delete the target named "Argaleo" whose path is the home folder (owner approved).
