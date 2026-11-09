# Simpler UI: matrix overview, folder scan, Save + Deploy

## Why

The shared-profiles release (`f4563fd`) made MCP Manager hard to use. The owner could not answer a basic
question in the app: *"How do I update the Atlassian MCP in `~/Repos/_argaleo` for all my harnesses?"*

Concrete problems found:

- **No clear save.** There are three save-like actions (Save all, Update all, Update selected) plus per-target
  Update. "Save all" only writes `workspace.json`; "Update" writes client files *and* saves. Users can't tell
  when their edit is stored, or where.
- **Structure doesn't match the mental model.** The owner thinks in *"this server, in these places"*. The app
  makes you think in profiles → targets → overrides.
- **Most real configs are invisible.** `~/Repos` holds 36 folders with MCP configs; the workspace manages 3.
  There is no way to pick up existing configs as targets.
- **Multi-client targets are broken.** `UpdateTargetsAsync` (`MainWindowViewModel.Management.cs`) rejects any
  target with more than one client. Melvin, 3D Drawings and Homelab (Claude Code + OpenCode) cannot be updated.

Success: the Atlassian question above takes one checkbox and one button.

## Goals

1. One overview: a matrix of servers × places, where one checkbox includes a server in a place.
2. A scan that turns existing config files into places, so the app reflects reality.
3. One Save button with an opt-in "deploy" checkbox. Clear dirty state while editing.

## Non-goals

- Background file watching or automatic re-scans.
- Different server sets per harness inside one project folder.
- Profile management UI (see Profiles below).
- Changing config generators (other than the OpenCode path below), merge logic, backups, or the workspace
  file format beyond the additive fields named in this document.

## Anti-goals (failure even if it "works")

- Writing to client config files without the user explicitly asking for it in that action.
- Silently losing half-finished edits, or silently writing half-finished edits.
- A scan that modifies any config file.

## Concepts (user-facing)

- **Server**: an entry in the library (unchanged).
- **Place**: a destination. Either a *global* harness (Claude Code global, Codex global, OpenCode global,
  Claude Desktop) or a *project folder* with one or more harnesses (e.g. `_argaleo` with Claude Code + OpenCode).
- **Save**: stores changes in MCP Manager (`workspace.json`).
- **Deploy**: writes the stored configuration into the harness config files.

The word "target" and "profile" no longer appear in the main UI.

## 1. Matrix overview (new main screen)

Replaces the current Dashboard/Overview as the default page.

- Rows: servers, A–Z, with the existing search box.
- Columns: places, in two groups — **Global** (one column per global harness) and **Projects** (one column per
  folder). Project column headers show the folder name and harness chips (CC, OC, CX, …).
- Cell: a checkbox. Checking it includes the server in **every harness of that place**.
- Clicking a row's server name opens the server editor (existing page).
- Clicking a column header opens the place editor: choose harnesses, per-server tool permissions, bridge args,
  remove place. This reuses the existing target detail page, minus profile controls.
- A column header shows a small "behind" marker when the place has saved-but-not-deployed changes or its files
  were changed externally (existing `NeedsUpdate` / `HasExternalChanges`).
- Clipboard and Quick Export are not columns; they stay reachable from a menu ("Export…"), unchanged.

Matrix edits follow the same Save rules as everything else (below): they mark the page dirty until saved.

### Places with multiple harnesses

A place covers all harnesses of one folder, with a single server selection. Builder's choice how to model this:

- (a) keep one `TargetFolder` with multiple `EnabledClients` flags and remove the single-client restriction in
  `UpdateTargetsAsync`, or
- (b) keep single-client targets and group targets by `Path` in the view model.

Either way, existing multi-client targets (Melvin, 3D Drawings, Homelab) must deploy without error, and
`AppliedFileHashes` / external-change detection must keep working per file.

## 2. Scan

- A **Scan** button on the matrix toolbar.
- Scan roots are configured in Preferences (list of folders). Default: `GlobalSettings.DefaultProjectsRoot`
  if set, otherwise `~/Repos` if it exists, otherwise the user's home directory.
- Walks each root to depth 4, skipping `.git`, `node_modules`, `bin`, `obj`, `.worktrees`, and hidden folders
  other than the harness config folders themselves.
- Detects per folder the harness config files the app already knows how to write (use the same paths as
  `ConfigExportService.GetConfigFilePaths`, e.g. `.mcp.json`, `opencode.json`/`opencode.jsonc`,
  `.codex/config.toml`, `.cursor/mcp.json`, `.vscode/mcp.json`). Also checks the global config paths.
- Results list: one row per folder with detected harnesses, a "new / already known" state, and a summary of
  servers found (recognized / unknown / differs from library). New folders are pre-checked; folders whose path
  contains `test` are unchecked and labelled "looks like test data".
- **Add selected** creates a place per checked folder with the detected harnesses and the found servers
  selected. Servers are matched to the library by managed ID first, then by name. Unknown servers are imported
  into the library (existing `ConfigImportService.ImportServer`).
- Within one folder, different harness files may list different servers: the place gets the union, and the
  scan row says "harnesses differ".
- A recognized server whose definition differs from the library (any field: command, args, url, env, headers)
  is labelled "differs". After **Add selected**, the user walks through every differing server in the
  **diff screen** (section 4), one at a time, and chooses: **Keep library**, **Put this version in the
  library**, or **Import as separate server**. Nothing is saved until the walk-through is finished; cancelling
  it adds nothing. Nothing about credentials is decided silently.
- Servers the place takes over from a scan (matched or imported) are *adopted*: the place records their names
  (new optional `TargetFolder.AdoptedServerNames`, additive to format 2) so the next deploy replaces those
  same-name unmanaged entries without a conflict prompt. The diff screen already got the user's decision.
- OpenCode: if `<folder>/.opencode/opencode.json` exists, that file is the OpenCode config for the place, for
  both scan and deploy. Otherwise the existing root `opencode.json`/`opencode.jsonc` logic applies.
- Scanning and adding never write to config files. Adding is a Save (workspace only).
- Already-known folders that gained a harness file are shown as "new harness found" and can be updated the same way.

## 3. Save and Deploy

Applies to the server editor, the place editor and the matrix.

- Editing anything marks the current page dirty: a "● Unsaved changes" badge next to the page title.
- Bottom/toolbar of the page: **Cancel**, a checkbox **"Also deploy (N files)"**, and **Save**.
  - The checkbox is **unchecked every time** a page becomes dirty. It is never remembered.
  - N is the number of config files that would actually change (existing `PreviewConfigs` vs disk hash),
    across all places affected by the pending change. When N is 0 the checkbox is disabled with "(no files change)".
- **Save** validates first. Invalid input (e.g. empty command for stdio, invalid URL) blocks Save with a message
  next to the field; nothing is stored.
- **Save** stores to `workspace.json`. With the checkbox on, it then deploys the affected places using the
  existing update path: conflict dialog for same-name unmanaged entries, external-change block, backups,
  rollback on write failure.
- **Cancel** reverts the page to the last saved state.
- Navigating away from a dirty page asks: **Save**, **Discard**, or **Stay**.
- A place is **behind** when it was deployed before (`LastAppliedAt` set) and its generated config no longer
  matches the hashes recorded at that deploy (`AppliedFileHashes`). A place that was never deployed is
  **not deployed yet**: shown neutrally on its column, never counted in the bar, and only written when the user
  deploys it explicitly (place editor **Deploy**, or Save with "Also deploy" on a change that affects it).
- When places are behind, a bar shows across the app: *"N places not deployed · Review · Deploy now"*.
  Deploy now deploys all behind places in one batch. Review opens the diff screen per file.
- The old **Save all**, **Update all**, **Update selected** buttons and the bulk-selection checkboxes are removed.
- External changes: if a place's files changed outside the app, deploying it is blocked and its column shows
  the marker. The place editor opens the diff screen (file on disk vs. what MCP Manager would write) with
  **Overwrite with MCP Manager** or **Keep file**, replacing the current "Reapply central settings" button.

## 4. Diff screen (one shared component)

One component, used for: scan "differs" walk-through, the export conflict dialog (`ExportConflictDialog`,
replacing its two raw JSON text boxes), external changes, and Review before deploy.

- Field-level comparison per server: rows for command, args (one per line), url, type, each env var, each
  header, tool permissions. Columns: left side vs. right side, labelled (e.g. "Library" vs. "Found in
  `_argaleo/.mcp.json`", or "In file" vs. "MCP Manager will write").
- Equal fields are dimmed; added parts highlighted green, removed parts red/struck through, at token level for
  args (so an inserted `--name x` stands out).
- Secret-like values (env vars and headers whose key contains TOKEN, KEY, SECRET, PASSWORD, AUTH, or values
  in `Authorization` headers) are masked as `••••••••` plus the last 4 characters, with a **Show** toggle per
  value. Two different secrets must be visibly different even when masked.
- When several servers/files differ: "1 of N" with Previous/Next; the choice is per item.
- Choices depend on the context: scan (Keep library / Put this version in the library / Import as separate
  server), conflict (Replace with MCP Manager / Keep existing / Cancel), external change (Overwrite / Keep
  file), Review (read-only, Deploy / Close).
- Servers that exist on only one side show as a single "only in file" / "only in MCP Manager" row.

## Profiles

Profiles are hidden from the UI: no sidebar section, no profile picker in the place editor. Data stays:
`McpRegistry.Profiles` and `TargetFolder.ProfileId` keep loading, saving and resolving through
`ProfileResolver` exactly as now, so no workspace data is lost. A place linked to a profile shows the
profile-resolved state in the matrix; editing a cell stores an explicit place override (existing behaviour).
Whether to delete profiles from the model entirely is a later decision.

One-off cleanup (owner approved): the "Argaleo" target pointing at the home folder (created by mistake,
linked to the "Argaleo" profile, would write `~/.mcp.json`) is deleted from the owner's workspace. This is a
data fix with the app closed, not a code change.

## UI language

The app UI stays English (project convention). The Dutch labels in the mockups are illustrative.

## Testing

- Unit tests for the scanner (temp directory tree with config files for several harnesses, skip rules,
  depth limit, matching by ID/name, unknown/differs classification, no writes).
- Unit tests for multi-client place deploy (a place with Claude Code + OpenCode writes both files and records
  both hashes).
- View-model tests for dirty state, Cancel revert, Save with and without deploy, deploy-file count, and the
  "not deployed" bar.
- Existing test suite must stay green.

## Decisions made without the owner (check these)

- Never-deployed places are not "behind" and are never written without an explicit deploy.
- Scan-adopted servers replace same-name entries on deploy without a conflict prompt (decided in the diff screen).
- Within a folder, the place takes the union of servers from all its harness files.
- `.opencode/opencode.json` is used when present.
- Secret detection by key name (TOKEN, KEY, SECRET, PASSWORD, AUTH) and Authorization headers.

- Matrix replaces the Overview as the start page; the server editor and place editor stay as detail pages.
- Scan depth 4, default root `~/Repos`, folders with `test` in the path are unchecked by default.
- Matching scanned servers by name when no managed ID; the library definition wins on deploy.
- Clipboard and Quick Export move to an "Export…" menu, otherwise unchanged.
- Profiles hidden, not deleted.
- Invalid input blocks Save rather than saving a draft.
