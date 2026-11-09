using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public partial class MainWindowViewModel
{
  public Func<DiffViewModel, Task<DiffChoice?>> PromptExternalChangeAsync { get; set; } = ShowExternalChangeDialogAsync;

  [RelayCommand]
  private async Task CompareExternalChangesAsync()
  {
    TargetFolderViewModel? place = SelectedTarget;
    if (place?.IsManagedTarget != true || !EnsureCleanForDeploy())
    {
      return;
    }

    List<ServerDiff> items;
    try
    {
      items = BuildPlaceDiff(place, null);
    }
    catch (Exception ex)
    {
      StatusMessage = $"Could not compare the files: {ex.Message}";
      return;
    }

    if (items.Count == 0)
    {
      StatusMessage = "No server differences; the files differ only in other settings.";
    }

    DiffViewModel diff = new("In file", "MCP Manager will write", items, []);
    switch (await PromptExternalChangeAsync(diff))
    {
      case DiffChoice.Overwrite:
        await OverwriteExternalChangesAsync();
        break;
      case DiffChoice.KeepFile:
        await KeepExternalFileAsync();
        break;
    }
  }

  [RelayCommand]
  private async Task OverwriteExternalChangesAsync()
  {
    if (SelectedTarget?.IsManagedTarget == true)
    {
      await UpdateTargetsAsync([SelectedTarget], overwriteExternal: true);
    }
  }

  [RelayCommand]
  private async Task KeepExternalFileAsync()
  {
    TargetFolderViewModel? place = SelectedTarget;
    if (_registry == null || place?.IsManagedTarget != true || !EnsureCleanForDeploy())
    {
      return;
    }

    Dictionary<string, string> previous = new(place.Model.AppliedFileHashes);
    foreach (string path in previous.Keys)
    {
      place.Model.AppliedFileHashes[path] = TargetSyncService.FileHash(path);
    }

    IsLoading = true;
    try
    {
      await _registryService.SaveAsync(_registry);
      MarkWorkspaceSaved();
      StatusMessage = $"Kept the files of '{place.Name}' as they are. No files were written.";
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      place.Model.AppliedFileHashes = previous;
      StatusMessage = $"Error saving: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
      CheckTargets();
    }
  }

  private static async Task<DiffChoice?> ShowExternalChangeDialogAsync(DiffViewModel diff)
  {
    Window? window = GetMainWindow();
    return window == null ? null : await new Views.ExternalChangeDialog(diff).ShowDialog<DiffChoice?>(window);
  }
}
