# MCP Look and Feel Sync Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Align `mcp-manager` with the same app family look and feel as `git-auto-sync` and `rclone-mount` while preserving current MCP management behavior.

**Architecture:** Keep the existing Avalonia app and viewmodels, but split the current single-screen experience into explicit navigation states with a calmer Fluent shell and smaller, focused content sections. Do the work in thin slices so the app always builds and each UX step can be checked before moving deeper into the detail views.

**Tech Stack:** C#, .NET 10, Avalonia, FluentTheme, CommunityToolkit.Mvvm

---

### Task 1: Add app navigation state

**Files:**
- Modify: `src/McpManager/ViewModels/MainWindowViewModel.cs`
- Test: manual verification via app navigation and `dotnet build McpManager.slnx`

**Step 1: Write the failing test**

Define the desired state transitions before touching the view:

- default state is `Overview`
- opening settings switches to `Settings`
- selecting a server switches to `Servers`
- selecting a target switches to `Targets`
- clearing selection returns to `Overview`

If a formal test project is not added yet, write this as a temporary checklist in comments or notes for the task implementation and convert it to automated tests only if the viewmodel extraction becomes easy.

**Step 2: Run test to verify it fails**

Run: `dotnet build McpManager.slnx`
Expected: build passes before the behavior exists; manual state checklist is not yet satisfiable.

**Step 3: Write minimal implementation**

Add an explicit navigation concept such as `CurrentPage` or `SelectedSection` in `MainWindowViewModel`, and update the existing commands and selection callbacks to drive that state directly.

**Step 4: Run test to verify it passes**

Run: `dotnet build McpManager.slnx`
Expected: PASS

Manual check: open the app and confirm overview, settings, server, and target transitions behave predictably.

**Step 5: Commit**

```bash
git add src/McpManager/ViewModels/MainWindowViewModel.cs
git commit -m "refactor: make main navigation explicit"
```

### Task 2: Replace hardcoded shell styling with app resources

**Files:**
- Modify: `src/McpManager/App.axaml`
- Modify: `src/McpManager/Views/MainWindow.axaml`
- Test: manual theme check and `dotnet build McpManager.slnx`

**Step 1: Write the failing test**

Define the expected shell rules:

- no major layout region uses hardcoded dark hex backgrounds directly
- shared shell colors come from resources
- light and dark themes both remain readable

**Step 2: Run test to verify it fails**

Run: `dotnet build McpManager.slnx`
Expected: PASS, while `MainWindow.axaml` still contains hardcoded shell colors.

**Step 3: Write minimal implementation**

Add the shell resource set in `App.axaml` and update `MainWindow.axaml` to use those resources for app background, sidebar, cards, borders, and subtle text treatment.

**Step 4: Run test to verify it passes**

Run: `dotnet build McpManager.slnx`
Expected: PASS

Manual check: verify the shell feels closer to the sibling apps and still renders correctly in light and dark themes.

**Step 5: Commit**

```bash
git add src/McpManager/App.axaml src/McpManager/Views/MainWindow.axaml
git commit -m "refactor: move main shell styling to theme resources"
```

### Task 3: Introduce overview-first home screen

**Files:**
- Modify: `src/McpManager/Views/MainWindow.axaml`
- Modify: `src/McpManager/ViewModels/MainWindowViewModel.cs`
- Test: manual overview verification and `dotnet build McpManager.slnx`

**Step 1: Write the failing test**

Define the expected overview content:

- summary cards for counts and status
- quick actions grouped in one place
- cleaner empty state when no servers exist

**Step 2: Run test to verify it fails**

Run: `dotnet build McpManager.slnx`
Expected: PASS, while the home screen is still action-heavy and form-like.

**Step 3: Write minimal implementation**

Rework the default content area so it starts with overview cards and quick actions, and remove emoji-heavy blocks and duplicated visual weight.

**Step 4: Run test to verify it passes**

Run: `dotnet build McpManager.slnx`
Expected: PASS

Manual check: confirm the first screen answers "what is the current state of my setup?" before offering edits.

**Step 5: Commit**

```bash
git add src/McpManager/Views/MainWindow.axaml src/McpManager/ViewModels/MainWindowViewModel.cs
git commit -m "feat: add overview-first landing screen"
```

### Task 4: Convert server and target navigation to a calmer browse-detail layout

**Files:**
- Modify: `src/McpManager/Views/MainWindow.axaml`
- Modify: `src/McpManager/ViewModels/MainWindowViewModel.cs`
- Modify: `src/McpManager/ViewModels/McpServerViewModel.cs`
- Modify: `src/McpManager/ViewModels/TargetFolderViewModel.cs`
- Test: manual layout verification and `dotnet build McpManager.slnx`

**Step 1: Write the failing test**

Define the target behavior:

- navigation remains stable while switching between items
- detail cards are grouped by concern
- server and target sections feel visually related

**Step 2: Run test to verify it fails**

Run: `dotnet build McpManager.slnx`
Expected: PASS, while the current layout still feels like one oversized editor.

**Step 3: Write minimal implementation**

Refactor the screen regions so server and target selection feel like dedicated sections, and tighten card order, spacing, headings, and action placement.

**Step 4: Run test to verify it passes**

Run: `dotnet build McpManager.slnx`
Expected: PASS

Manual check: confirm item switching is fast to scan and the detail side is easier to understand.

**Step 5: Commit**

```bash
git add src/McpManager/Views/MainWindow.axaml src/McpManager/ViewModels/MainWindowViewModel.cs src/McpManager/ViewModels/McpServerViewModel.cs src/McpManager/ViewModels/TargetFolderViewModel.cs
git commit -m "refactor: simplify browse and detail flows"
```

### Task 5: Replace emoji-driven affordances with consistent semantic UI

**Files:**
- Modify: `src/McpManager/Views/MainWindow.axaml`
- Modify: `src/McpManager/ViewModels/Converters.cs`
- Modify: `src/McpManager/Assets/` as needed for icons
- Test: manual icon and status review plus `dotnet build McpManager.slnx`

**Step 1: Write the failing test**

Define the visual rule set:

- no emoji in core navigation, action buttons, or section titles
- transport and target indicators use consistent semantics
- status colors remain readable and theme-safe

**Step 2: Run test to verify it fails**

Run: `dotnet build McpManager.slnx`
Expected: PASS, while emoji and ad-hoc converter output still exist.

**Step 3: Write minimal implementation**

Update converters and XAML bindings to use simple symbolic output, consistent labels, and theme-friendly status styling.

**Step 4: Run test to verify it passes**

Run: `dotnet build McpManager.slnx`
Expected: PASS

Manual check: confirm the app now visually matches the sibling tools more closely.

**Step 5: Commit**

```bash
git add src/McpManager/Views/MainWindow.axaml src/McpManager/ViewModels/Converters.cs src/McpManager/Assets
git commit -m "refactor: unify iconography and status styling"
```

### Task 6: Polish settings and diagnostics presentation

**Files:**
- Modify: `src/McpManager/Views/MainWindow.axaml`
- Modify: `src/McpManager/ViewModels/MainWindowViewModel.cs`
- Test: manual settings flow review and `dotnet build McpManager.slnx`

**Step 1: Write the failing test**

Define the expected outcome:

- settings reads as app preferences, not a debug screen
- diagnostics stay available but visually secondary
- bridge and MCP test outputs are still copyable and useful

**Step 2: Run test to verify it fails**

Run: `dotnet build McpManager.slnx`
Expected: PASS, while settings and diagnostics still dominate visually.

**Step 3: Write minimal implementation**

Tone down verbose panels, tighten explanatory copy, and structure settings/testing blocks so primary tasks stay prominent.

**Step 4: Run test to verify it passes**

Run: `dotnet build McpManager.slnx`
Expected: PASS

Manual check: confirm test and bridge tooling still work but no longer hijack the whole screen.

**Step 5: Commit**

```bash
git add src/McpManager/Views/MainWindow.axaml src/McpManager/ViewModels/MainWindowViewModel.cs
git commit -m "refactor: polish settings and diagnostics layout"
```

### Task 7: Final verification and cleanup

**Files:**
- Modify: any touched files from previous tasks if cleanup is required
- Test: `dotnet build McpManager.slnx`

**Step 1: Write the failing test**

Create a final verification checklist:

- build succeeds cleanly
- overview, servers, targets, and settings all render
- light and dark themes are readable
- no core shell sections depend on emoji or ad-hoc dark styling

**Step 2: Run test to verify it fails**

Run: `dotnet build McpManager.slnx`
Expected: PASS unless cleanup introduced regressions; checklist may still reveal UI polish gaps.

**Step 3: Write minimal implementation**

Do only final cleanup needed to satisfy the checklist. Do not add extra features.

**Step 4: Run test to verify it passes**

Run: `dotnet build McpManager.slnx`
Expected: PASS

Manual check: user opens the app and confirms the refreshed look and feel matches the sibling tools closely enough.

**Step 5: Commit**

```bash
git add src/McpManager/App.axaml src/McpManager/Views/MainWindow.axaml src/McpManager/ViewModels/MainWindowViewModel.cs src/McpManager/ViewModels/Converters.cs src/McpManager/ViewModels/McpServerViewModel.cs src/McpManager/ViewModels/TargetFolderViewModel.cs
git commit -m "feat: align mcp manager with shared app styling"
```
