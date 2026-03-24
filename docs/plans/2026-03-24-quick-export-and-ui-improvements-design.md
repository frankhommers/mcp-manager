# Quick Export Target & UI Improvements Design

**Date:** 2026-03-24
**Status:** Approved

## Summary

Three related UI improvements to streamline the export workflow:

1. **Quick Export target** - a permanent target for one-time directory exports
2. **Export button in top bar** - replace Import with Export, move Import to Overview only
3. **Clipboard radio buttons** - single-select format for clipboard exports

## 1. Quick Export Target

### Problem
Currently, exporting to a new project directory requires creating a full target (name, path, clients, servers). For one-off exports this is too heavy.

### Solution
A permanent "Quick Export" target in the targets list with a pastable text field + folder picker. The path is remembered between sessions but designed to be changed frequently.

### Model
- `TargetFolder.IsQuickExport` (bool) - new property, analogous to `IsClipboard` and `IsGlobal`

### ViewModel
- `EnsureQuickExportTarget()` at startup, similar to `EnsureClipboardTarget()`
- Placed after Clipboard in the targets list
- Default: `EnabledClients = TargetClientFlags.ClaudeCode`
- Not deletable (like Clipboard)
- Saved to registry (path persists)

### UI (Target detail panel when IsQuickExport)
- Badge: "QUICK"
- Description: "One-time export directory. Paste a path or browse to quickly export configs."
- Destination card with TextBox (pastable) + Browse button
- All standard target features available (server selection, client flags, tool overrides)
- Checkboxes for export format (multi-select, like regular folder targets)

## 2. Import/Export Button Swap

### Problem
The "Import" button in the top bar is counterintuitive. Users spend most time exporting, not importing. Import is an onboarding/setup action.

### Solution
- Top bar: Replace `[Import]` with `[Export]` bound to `ExportAllTargetsCommand`
- Import remains accessible via Overview page action cards (Import File, Claude Desktop, Codex, Scan Projects)

## 3. Clipboard Radio Buttons

### Problem
The Clipboard target shows checkboxes for export format, but clipboard can only hold one format at a time. Multiple selections are misleading.

### Solution
- Replace CheckBox controls with RadioButton controls in the Clipboard export format card
- Add `SelectedClipboardFormat` string property to `TargetFolderViewModel` that maps to/from `EnabledClients` flags (ensuring exactly one flag is set)
- Radio button options: Claude Code, Claude Desktop, OpenCode

## Files to Modify

### Core (McpManager.Core)
- `Models/McpRegistry.cs` - add `IsQuickExport` to `TargetFolder`

### ViewModel (McpManager)
- `ViewModels/MainWindowViewModel.cs` - add `EnsureQuickExportTarget()`, wire at startup
- `ViewModels/TargetFolderViewModel.cs` - add `IsQuickExport` property, `SelectedClipboardFormat` property for radio binding

### View (McpManager)
- `Views/MainWindow.axaml` - top bar button swap, Quick Export target detail UI, Clipboard radio buttons

### Converters (if needed)
- May need a converter or direct binding approach for RadioButton group <-> TargetClientFlags
