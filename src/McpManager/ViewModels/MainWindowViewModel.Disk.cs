using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public partial class MainWindowViewModel
{
  private readonly ScanImportPlanner _planner;
  private readonly ConfigScanService _scanService;
  private readonly DiskSnapshotService? _diskService;
  private DiskSnapshot _disk = DiskSnapshot.Empty;
  private string? _candidatePath;
  private int _diskVersion;

  public Func<DiffViewModel, Task<DiffChoice?>> PromptResolveDifferenceAsync { get; set; } =
    ShowResolveDifferenceDialogAsync;

  internal Task PendingDiskWork { get; private set; } = Task.CompletedTask;

  [RelayCommand]
  private Task RefreshDiskAsync() => PendingDiskWork = UpdateDiskAsync(
    (service, registry) => service.LoadAsync(registry), reportProblems: true);

  private Task RematchDiskAsync()
  {
    DiskSnapshot disk = _disk;
    return PendingDiskWork = UpdateDiskAsync(
      (service, registry) => Task.FromResult(service.Rematch(disk, registry)), reportProblems: false);
  }

  private async Task UpdateDiskAsync(
    Func<DiskSnapshotService, McpRegistry, Task<DiskSnapshot>> load, bool reportProblems)
  {
    if (_diskService is not { } service || _registry == null)
    {
      return;
    }

    SyncModels();
    int version = ++_diskVersion;
    DiskSnapshot snapshot;
    try
    {
      McpRegistry clone = RegistryService.Deserialize(RegistryService.Serialize(_registry));
      snapshot = await Task.Run(() => load(service, clone));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
      if (version == _diskVersion)
      {
        StatusMessage = $"Could not read configs: {ex.Message}";
      }

      return;
    }

    if (version != _diskVersion)
    {
      return;
    }

    _disk = snapshot;
    RebuildMatrix();
    if (reportProblems && snapshot.Problems.Count > 0)
    {
      string folders = snapshot.Problems.Count == 1 ? "folder" : "folders";
      StatusMessage = $"Could not compare {snapshot.Problems.Count} {folders}: {snapshot.Problems[0]}";
    }
  }

  [RelayCommand]
  private async Task ResolveDifferenceAsync(MatrixCellViewModel cell)
  {
    if (_registry == null || cell.Difference is not { Diff: not null } match)
    {
      return;
    }

    ScannedFolder? folder = _disk.Folders.FirstOrDefault(f =>
      string.Equals(f.Path, match.FolderPath, StringComparison.Ordinal));
    if (folder == null)
    {
      return;
    }

    string folderName = folder.IsGlobal ? "Global" : ProjectChoiceViewModel.FolderName(folder.Path);
    DiffViewModel diff = new(
      "Library",
      "Found on disk",
      [match.Diff with { ServerName = $"{match.Found.Name} · {folderName}" }],
      [DiffChoice.KeepLibrary, DiffChoice.UseFoundVersion, DiffChoice.ImportSeparately],
      (_, c) => c != DiffChoice.UseFoundVersion || match.CanUseFoundVersion);
    if (await PromptResolveDifferenceAsync(diff) is not { } choice)
    {
      return;
    }

    SyncModels();
    ScanServerMatch? live = _planner.Match([folder], _registry).FirstOrDefault(m =>
      string.Equals(m.FolderPath, match.FolderPath, StringComparison.Ordinal)
      && string.Equals(m.Found.Name, match.Found.Name, StringComparison.Ordinal)
      && m.Found.Client == match.Found.Client);
    if (live == null)
    {
      StatusMessage = $"{match.Found.Name} is no longer in {folderName}. Refresh and try again.";
      return;
    }

    TargetFolder place;
    try
    {
      if (ScanImportPlanner.FindPlace(_registry, folder) == null)
      {
        _planner.PromoteFolder(_registry, folder);
      }

      _planner.Apply(_registry, [folder], [live], new Dictionary<ScanServerMatch, ScanChoice>
      {
        [live] = ScanViewModel.ToScanChoice(choice),
      });
      place = ScanImportPlanner.FindPlace(_registry, folder)!;
    }
    catch (ArgumentException ex)
    {
      StatusMessage = ex.Message;
      return;
    }

    ReloadAfterRegistryChange(place.IsGlobal ? CurrentProjectId() : place.Id,
      $"Updated {match.Found.Name}. Save to keep it.");
  }

  [RelayCommand]
  private void AddUnknownToLibrary(UnknownServerRowViewModel row)
  {
    if (_registry == null)
    {
      return;
    }

    SyncModels();
    try
    {
      _planner.AddToLibrary(_registry, _disk.Folders, row.Name);
    }
    catch (ArgumentException ex)
    {
      StatusMessage = ex.Message;
      return;
    }

    ReloadAfterRegistryChange(CurrentProjectId(), $"Added {row.Name} to your library. Save to keep it.");
  }

  private void OnCandidateEdited(object? sender, CandidateEditedEventArgs e)
  {
    if (_registry == null)
    {
      return;
    }

    SyncModels();
    TargetFolder place = _planner.PromoteFolder(_registry, e.Folder);
    foreach (ServerSelectionViewModel selection in e.Selections.Where(s => s.ServerId != Guid.Empty))
    {
      if (selection.IsEnabled)
      {
        place.EnabledServers.Add(selection.ServerId);
        place.DisabledServers.Remove(selection.ServerId);
      }
      else
      {
        place.EnabledServers.Remove(selection.ServerId);
      }
    }

    _candidatePath = null;
    ReloadAfterRegistryChange(place.Id, $"Added project {place.Name}. Save to keep it.");
  }

  private void OnOtherFolderRequested(object? sender, EventArgs e) => _ = PickOtherFolderAsync();

  private async Task PickOtherFolderAsync()
  {
    Window? window = GetMainWindow();
    if (window == null || _registry == null || _diskService == null)
    {
      return;
    }

    IReadOnlyList<IStorageFolder> picked = await window.StorageProvider.OpenFolderPickerAsync(
      new FolderPickerOpenOptions { Title = "Select Project Folder", AllowMultiple = false });
    if (picked.Count == 0)
    {
      return;
    }

    ScannedFolder folder;
    try
    {
      folder = await _scanService.ReadFolderAsync(picked[0].Path.LocalPath, _registry.Settings);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
      StatusMessage = $"Could not read configs: {ex.Message}";
      return;
    }

    SyncModels();
    if (ScanImportPlanner.FindPlace(_registry, folder) is { } existing)
    {
      Matrix.SelectedChoice = Matrix.Choices.FirstOrDefault(c => c.Column?.Place.Id == existing.Id)
        ?? Matrix.SelectedChoice;
      return;
    }

    if (folder.Clients != TargetClientFlags.None)
    {
      if (!_disk.Folders.Any(f => !f.IsGlobal && ProjectFolderRules.PathsEqual(f.Path, folder.Path)))
      {
        _diskVersion++;
        _disk = _diskService.Rematch(_disk with { Folders = [.. _disk.Folders, folder] }, _registry);
      }

      _candidatePath = folder.Path;
      RebuildMatrix();
      return;
    }

    TargetFolder place = _planner.PromoteFolder(_registry, folder);
    ReloadAfterRegistryChange(place.Id, string.Empty);
    WithoutNavigationGuard(() => SelectedTarget = TargetFolders.FirstOrDefault(t => t.Id == place.Id));
    StatusMessage = $"Choose which harnesses {place.Name} uses.";
  }

  private void ReloadAfterRegistryChange(Guid? selectProjectId, string message)
  {
    _suppressMatrixRebuild = true;
    try
    {
      WithoutNavigationGuard(() =>
      {
        SelectedProfile = null;
        SelectedServer = null;
        SelectedTarget = null;
        ShowSettings = false;
        ShowScan = false;
        RefreshFromRegistry();
        AttachManagement();
      });
    }
    finally
    {
      _suppressMatrixRebuild = false;
    }

    if (selectProjectId != null)
    {
      _candidatePath = null;
      RememberProject(selectProjectId);
    }

    RebuildMatrix();
    CheckTargets();
    _ = RematchDiskAsync();
    StatusMessage = message;
  }

  private Guid? CurrentProjectId() => Matrix.SelectedChoice?.Column?.Place.Id;

  private static async Task<DiffChoice?> ShowResolveDifferenceDialogAsync(DiffViewModel diff)
  {
    Window? window = GetMainWindow();
    return window == null ? null : await new Views.ResolveDifferenceDialog(diff).ShowDialog<DiffChoice?>(window);
  }
}
