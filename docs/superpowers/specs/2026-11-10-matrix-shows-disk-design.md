# Matrix shows what is on disk — design

Date: 2026-11-10. Builds on `2026-11-09-simpler-ui-design.md`.

## Problem

"Scan" mixed two jobs (finding folders and importing servers) and the user could not tell what it added.
"Add project" must not be a scan either. The user wants no scan or import concept at all.

## Principle

The matrix shows what is on disk and you change it right there. No Scan button, no Import button.

## Behaviour

1. **Discovery runs by itself.** At startup, after Save, after a deploy, and when the user clicks the small refresh
   icon next to the project picker, the app reads in the background:
   - the scan roots (`RegistryService.GetDefaultScanRoots`, max depth 4, same exclusions as today) and the global
     harness configs, and
   - every existing project place, including places outside the scan roots.

   The result is a *disk snapshot*: folders plus server matches against the library (`ScanImportPlanner.Match`).
   Discovery never blocks the UI and never changes the workspace. Failures show in the status bar.
2. **Project picker.** The picker has these items, in order:
   - your projects (A–Z);
   - a disabled header "Found on disk", then folders from the snapshot that are not a place yet (A–Z by folder
     name), each with path and harness chips;
   - "Other folder…".
3. **A found folder is a candidate.** Picking it shows a column labelled "Found on disk". A cell is ticked when the
   server is in the library, enabled in that folder's config, and matches the library (Known).
   The first cell the user toggles promotes the folder to a project:
   - it gets the folder's harnesses;
   - the Known servers are adopted;
   - the user's toggle is applied on top.

   The workspace becomes dirty and the status bar says "Added project <name>. Save to keep it."
   The picker then shows the new project.
4. **Other folder…** opens the folder picker.
   - If the folder is already a project, that project is selected.
   - If it has MCP configs, it becomes a candidate (point 3) and is selected.
   - If it has no configs, a project is created with no harnesses, the place editor opens, and the status bar says
     "Choose which harnesses <name> uses."
5. **Unknown servers.** Below the library rows, a section "Found in your configs, not in your library" lists, by
   name, servers that appear in a visible column's folder but are not in the library.
   - Each visible column shows a read-only dot where the server was found.
   - "+ Library" adds the server (definition from its first occurrence) to the library. It also adopts and enables
     it in every existing place where it was found enabled.
   - Candidate folders are not promoted by this.
6. **Differences.** A library row cell gets an orange dot when that column's folder has the server with a different
   definition or token (Differs). Clicking the dot opens a diff dialog (the existing `DiffView`) with Keep library /
   Use found version / Import separately, the same availability rule as before (`CanUseFoundVersion`).
   Applying uses `ScanImportPlanner.Apply` for that one match. A candidate folder gets promoted by this.
   Nothing is ever overwritten silently.
7. **Removed:** the Scan page (`ScanView`, `ScanViewModel`, `ScannedFolderViewModel`), the overview's Scan and
   Add place buttons. Overview keeps Export…. The sidebar "+" for places stays. Scan roots stay in Preferences,
   relabelled "Folders to look for projects in".
8. Save / Cancel / "Deploy across the whole universe" are unchanged.

## Decisions

- Path equality between places and found folders ignores case and trailing separators (`Melvin` has a trailing `/`).
- The snapshot is computed on a deserialized copy of the registry. Actions (promote, + Library, resolve diff)
  re-match the affected folder against the live registry before changing anything.
- A match is "in the library" when its `Library` server's `Id` is in the live library.
- Only the project id is remembered in `ui-state.json`, not candidates.
