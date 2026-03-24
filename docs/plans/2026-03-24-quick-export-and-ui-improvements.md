# Quick Export Target & UI Improvements Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a Quick Export target for one-time directory exports, swap Import/Export button placement, and use radio buttons for Clipboard format selection.

**Architecture:** Three independent UI improvements touching the same files. Model gets `IsQuickExport` flag, ViewModel gets `EnsureQuickExportTarget()` and `SelectedClipboardFormat`, View gets updated XAML for all three features.

**Tech Stack:** Avalonia UI 11, CommunityToolkit.Mvvm, C# / .NET 10

---

### Task 1: Add `IsQuickExport` to Model

**Files:**
- Modify: `src/McpManager.Core/Models/McpRegistry.cs:16-64`

**Step 1: Add `IsQuickExport` property to `TargetFolder`**

In `McpRegistry.cs`, add after the `IsClipboard` property (line 51):

```csharp
  /// <summary>
  /// Is this a quick export target (reusable one-time export directory)?
  /// </summary>
  public bool IsQuickExport { get; set; }
```

**Step 2: Build to verify**

Run: `dotnet build src/McpManager.Core/McpManager.Core.csproj`
Expected: Build succeeded

**Step 3: Commit**

```
feat: add IsQuickExport property to TargetFolder model
```

---

### Task 2: Add `IsQuickExport` and `SelectedClipboardFormat` to ViewModel

**Files:**
- Modify: `src/McpManager/ViewModels/TargetFolderViewModel.cs:10-82`

**Step 1: Add `IsQuickExport` observable property**

After `_isClipboard` (line 23), add:

```csharp
  [ObservableProperty] private bool _isQuickExport;
```

**Step 2: Add `SelectedClipboardFormat` property for radio button binding**

After the `IsClaudeDesktopGlobal` property (line 41), add:

```csharp
  /// <summary>
  /// Selected clipboard format as string for RadioButton binding.
  /// Maps to/from EnabledClients flags ensuring exactly one is set.
  /// </summary>
  public string SelectedClipboardFormat
  {
    get
    {
      if (EnableClaudeDesktop) return "ClaudeDesktop";
      if (EnableOpenCode) return "OpenCode";
      return "ClaudeCode";
    }
    set
    {
      EnableClaudeCode = value == "ClaudeCode";
      EnableClaudeDesktop = value == "ClaudeDesktop";
      EnableOpenCode = value == "OpenCode";
      OnPropertyChanged();
    }
  }
```

**Step 3: Initialize `_isQuickExport` in the constructor**

In the constructor (after line 62 `_isClipboard = model.IsClipboard;`), add:

```csharp
    _isQuickExport = model.IsQuickExport;
```

**Step 4: Persist `IsQuickExport` in `UpdateModel()`**

In `UpdateModel()` (after line 129 `_model.IsClipboard = IsClipboard;`), add:

```csharp
    _model.IsQuickExport = IsQuickExport;
```

**Step 5: Build to verify**

Run: `dotnet build src/McpManager/McpManager.csproj`
Expected: Build succeeded

**Step 6: Commit**

```
feat: add IsQuickExport and SelectedClipboardFormat to TargetFolderViewModel
```

---

### Task 3: Add `EnsureQuickExportTarget()` to MainWindowViewModel

**Files:**
- Modify: `src/McpManager/ViewModels/MainWindowViewModel.cs:226-326`

**Step 1: Add `EnsureQuickExportTarget()` method**

After `EnsureClipboardTarget()` (after line 302), add:

```csharp
  private void EnsureQuickExportTarget()
  {
    if (_registry == null)
    {
      return;
    }

    if (!_registry.TargetFolders.Any(t => t.IsQuickExport))
    {
      TargetFolder quickExportTarget = new()
      {
        Name = "Quick Export",
        Path = "",
        IsQuickExport = true,
        EnabledClients = TargetClientFlags.ClaudeCode,
      };

      // Insert after clipboard target (index 1), or at beginning if no clipboard
      int insertIndex = _registry.TargetFolders.FindIndex(t => t.IsClipboard);
      _registry.TargetFolders.Insert(insertIndex >= 0 ? insertIndex + 1 : 0, quickExportTarget);
    }
  }
```

**Step 2: Wire it in `RefreshFromRegistry()`**

In `RefreshFromRegistry()` (line 234), after `EnsureCodexTarget();` add:

```csharp
    EnsureQuickExportTarget();
```

**Step 3: Guard deletion of Quick Export target**

In `DeleteTargetFolder()` (line 1096), after the clipboard guard (line 1107), add:

```csharp
    // Cannot delete quick export target
    if (SelectedTarget.IsQuickExport)
    {
      return;
    }
```

**Step 4: Build to verify**

Run: `dotnet build src/McpManager/McpManager.csproj`
Expected: Build succeeded

**Step 5: Commit**

```
feat: add EnsureQuickExportTarget and guard against deletion
```

---

### Task 4: Update converters for Quick Export target

**Files:**
- Modify: `src/McpManager/ViewModels/TargetIconConverter.cs:15-21`
- Modify: `src/McpManager/ViewModels/TargetBadgeConverter.cs:13-33`

**Step 1: Add Quick Export to TargetIconConverter**

Replace the switch expression in `TargetIconConverter.Convert()` (lines 15-21):

```csharp
    string resourceKey = value switch
    {
      TargetFolderViewModel target when target.IsClipboard => "MdiClipboardOutline",
      TargetFolderViewModel target when target.IsQuickExport => "MdiLightningBoltOutline",
      TargetFolderViewModel target when target.IsCodex => "CodexLogo",
      TargetFolderViewModel target when target.IsGlobal => "ClaudeLogo",
      _ => "MdiFolderOutline",
    };
```

Note: Check if `MdiLightningBoltOutline` exists in Assets. If not, use `MdiExportVariant` or `MdiRocketLaunchOutline` or fall back to `MdiFolderOutline`. The exact icon resource name depends on what's available — check `App.axaml` resources first.

**Step 2: Add Quick Export to TargetBadgeConverter**

In `TargetBadgeConverter.Convert()`, after the clipboard check (line 18), add:

```csharp
      if (target.IsQuickExport)
      {
        return "QUICK";
      }
```

**Step 3: Add Quick Export to TargetIconColorConverter**

In `TargetIconColorConverter.Convert()` (line 42-52), add a Quick Export color case. Add before the `IsGlobal` check:

```csharp
    if (value is TargetFolderViewModel { IsQuickExport: true })
    {
      return new SolidColorBrush(Color.Parse("#E5C07B"));
    }
```

**Step 4: Build to verify**

Run: `dotnet build src/McpManager/McpManager.csproj`
Expected: Build succeeded

**Step 5: Commit**

```
feat: add Quick Export icon and badge to converters
```

---

### Task 5: Swap Import/Export button in top bar

**Files:**
- Modify: `src/McpManager/Views/MainWindow.axaml:568`

**Step 1: Replace Import button with Export button**

Change line 568 from:

```xml
<Button Content="Import" Command="{Binding ImportFromFileCommand}" Classes="toolbarButton" />
```

To:

```xml
<Button Content="Export" Command="{Binding ExportAllTargetsCommand}" Classes="toolbarButton" />
```

**Step 2: Build to verify**

Run: `dotnet build src/McpManager/McpManager.csproj`
Expected: Build succeeded

**Step 3: Commit**

```
feat: replace Import with Export button in top bar
```

---

### Task 6: Add Clipboard radio buttons

**Files:**
- Modify: `src/McpManager/Views/MainWindow.axaml:1333-1350`

**Step 1: Replace Clipboard CheckBoxes with RadioButtons**

Replace the entire clipboard export format card content (lines 1333-1350):

```xml
<Border Classes="detailCard" IsVisible="{Binding SelectedTarget.IsClipboard}">
    <StackPanel Spacing="16">
        <TextBlock Text="Export format" FontWeight="SemiBold" Foreground="{DynamicResource SystemControlForegroundBaseHighBrush}" />
        <TextBlock Text="Select which format should be copied to the clipboard."
                   Opacity="0.75" FontSize="12" />
        <StackPanel Spacing="8">
            <RadioButton Content="Claude Code (.mcp.json)"
                         GroupName="ClipboardFormat"
                         IsChecked="{Binding SelectedTarget.SelectedClipboardFormat, Converter={x:Static vm:StringEqualsConverter.Instance}, ConverterParameter=ClaudeCode}" />
            <RadioButton Content="Claude Desktop (claude_desktop_config.json)"
                         GroupName="ClipboardFormat"
                         IsChecked="{Binding SelectedTarget.SelectedClipboardFormat, Converter={x:Static vm:StringEqualsConverter.Instance}, ConverterParameter=ClaudeDesktop}" />
            <RadioButton Content="OpenCode (opencode.json)"
                         GroupName="ClipboardFormat"
                         IsChecked="{Binding SelectedTarget.SelectedClipboardFormat, Converter={x:Static vm:StringEqualsConverter.Instance}, ConverterParameter=OpenCode}" />
        </StackPanel>
    </StackPanel>
</Border>
```

**Step 2: Create `StringEqualsConverter`**

Add to `src/McpManager/ViewModels/Converters.cs`:

```csharp
public class StringEqualsConverter : IValueConverter
{
  public static readonly StringEqualsConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    return value?.ToString() == parameter?.ToString();
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is true)
    {
      return parameter?.ToString();
    }

    return Avalonia.Data.BindingOperations.DoNothing;
  }
}
```

Make sure the file has the necessary `using` directives (likely already present): `System`, `System.Globalization`, `Avalonia.Data.Converters`.

**Step 3: Build to verify**

Run: `dotnet build src/McpManager/McpManager.csproj`
Expected: Build succeeded

**Step 4: Commit**

```
feat: use radio buttons for clipboard export format selection
```

---

### Task 7: Add Quick Export target detail UI in XAML

**Files:**
- Modify: `src/McpManager/Views/MainWindow.axaml`

**Step 1: Add Quick Export description text**

In the target detail panel, after the clipboard description text (line 1169) and before the Claude Desktop description (line 1170), add a description for Quick Export:

```xml
<TextBlock Text="One-time export directory. Paste a path or browse to quickly export configs."
           Opacity="0.75"
           TextWrapping="Wrap"
           IsVisible="{Binding SelectedTarget.IsQuickExport}" />
```

**Step 2: Add Quick Export badge in the type display area**

In the badges section (after line 1207), add:

```xml
<Border Grid.Column="1" Classes="inlineBadge" IsVisible="{Binding SelectedTarget.IsQuickExport}">
    <TextBlock Text="QUICK" Classes="inlineBadgeText" />
</Border>
```

**Step 3: Show destination card for Quick Export**

The existing destination card (lines 1232-1260) is currently hidden for Global and Clipboard targets. The Quick Export target is neither Global nor Clipboard, so it will already show the destination card with Name + Path + Browse button. This is correct behavior — no change needed.

**Step 4: Show export formats for Quick Export**

The existing "Export formats" card (lines 1311-1331) is already shown for non-global, non-clipboard targets. Quick Export is neither, so it will show checkboxes. This is correct behavior — no change needed.

**Step 5: Hide Delete button for Quick Export**

The Delete button visibility (lines 1148-1154) currently hides for Global and Clipboard. Add `IsQuickExport` guard. Replace:

```xml
<Button.IsVisible>
    <MultiBinding Converter="{x:Static BoolConverters.And}">
        <Binding Path="!SelectedTarget.IsGlobal" />
        <Binding Path="!SelectedTarget.IsClipboard" />
    </MultiBinding>
</Button.IsVisible>
```

With:

```xml
<Button.IsVisible>
    <MultiBinding Converter="{x:Static BoolConverters.And}">
        <Binding Path="!SelectedTarget.IsGlobal" />
        <Binding Path="!SelectedTarget.IsClipboard" />
        <Binding Path="!SelectedTarget.IsQuickExport" />
    </MultiBinding>
</Button.IsVisible>
```

**Step 6: Update FOLDER-type visibility guards**

There are several places in the XAML where `!IsGlobal && !IsClipboard` determines visibility of "regular folder" content. The Quick Export target should show the same content, so it should keep passing these guards (it is not Global, not Clipboard). Verify that these guards don't need updating — Quick Export should show the same Destination card and Export formats card as regular folders. No change needed.

**Step 7: Build to verify**

Run: `dotnet build src/McpManager/McpManager.csproj`
Expected: Build succeeded

**Step 8: Commit**

```
feat: add Quick Export target UI with badge and description
```

---

### Task 8: Final build and verification

**Step 1: Full solution build**

Run: `dotnet build McpManager.slnx`
Expected: Build succeeded with 0 errors

**Step 2: Run tests**

Run: `dotnet test McpManager.slnx`
Expected: All tests pass

**Step 3: Final commit if needed (aggregate small fixes)**

Only if build/test fixes were required.
