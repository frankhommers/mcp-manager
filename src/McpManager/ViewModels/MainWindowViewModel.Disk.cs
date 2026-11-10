using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
  private readonly DiskSnapshotService? _diskService;
  private readonly List<string> _addedFolderPaths = [];
  private DiskSnapshot _disk = DiskSnapshot.Empty;
  private string? _candidatePath;
  private int _loadVersion;
  private int _rematchVersion;
  private HashSet<string> _shownProblems = [];
  private string? _problemsStatus;

  public Func<DiffViewModel, Task<DiffChoice?>> PromptResolveDifferenceAsync { get; set; } =
    ShowResolveDifferenceDialogAsync;

  internal Task PendingDiskWork { get; private set; } = Task.CompletedTask;

  [RelayCommand]
  private Task RefreshDiskAsync() => TrackDiskWork(LoadDiskAsync(announceProblems: true));

  private Task ReloadDiskAsync() => TrackDiskWork(LoadDiskAsync(announceProblems: false));

  private Task RematchDiskAsync() => TrackDiskWork(RematchCurrentDiskAsync());

  private Task TrackDiskWork(Task work) =>
    PendingDiskWork = PendingDiskWork.IsCompleted ? work : Task.WhenAll(PendingDiskWork, work);

  private async Task LoadDiskAsync(bool announceProblems)
  {
    if (_diskService is not { } service || _registry == null)
    {
      return;
    }

    SyncModels();
    int load = ++_loadVersion;
    List<string> added = [.. _addedFolderPaths];
    try
    {
      McpRegistry loadRegistry = CloneRegistry(_registry);
      DiskSnapshot loaded = await Task.Run(async () => WithFolders(
        await service.LoadAsync(loadRegistry), await ReadFoldersAsync(service, added, loadRegistry.Settings)));
      while (load == _loadVersion && _registry != null)
      {
        int rematch = _rematchVersion;
        DiskSnapshot merged = WithFolders(loaded, _disk.Folders.Where(f => IsAddedFolder(f.Path)));
        SyncModels();
        McpRegistry current = CloneRegistry(_registry);
        DiskSnapshot snapshot = await Task.Run(() => service.Rematch(merged, current));
        if (load != _loadVersion || rematch != _rematchVersion)
        {
          continue;
        }

        _rematchVersion++;
        _disk = snapshot;
        RebuildMatrix();
        ShowProblems(snapshot.Problems, announceProblems);
        return;
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
      if (load == _loadVersion)
      {
        StatusMessage = $"Could not read configs: {ex.Message}";
      }
    }
  }

  private void ShowProblems(IReadOnlyList<string> problems, bool announce)
  {
    if (problems.Count == 0)
    {
      if (_problemsStatus != null && StatusMessage == _problemsStatus)
      {
        StatusMessage = string.Empty;
      }

      _problemsStatus = null;
      _shownProblems = [];
      return;
    }

    if ((!announce && _shownProblems.SetEquals(problems))
        || StatusMessage.Contains(DeployFailedText, StringComparison.Ordinal))
    {
      return;
    }

    _shownProblems = [.. problems];
    string noun = problems.Count == 1 ? "problem" : "problems";
    _problemsStatus = $"{problems.Count} {noun} on disk: {problems[0]}";
    StatusMessage = _problemsStatus;
  }

  private async Task RematchCurrentDiskAsync()
  {
    if (_diskService is not { } service || _registry == null)
    {
      return;
    }

    SyncModels();
    int rematch = ++_rematchVersion;
    DiskSnapshot disk = _disk;
    DiskSnapshot snapshot;
    try
    {
      McpRegistry current = CloneRegistry(_registry);
      snapshot = await Task.Run(() => service.Rematch(disk, current));
    }
    catch (InvalidDataException ex)
    {
      if (rematch == _rematchVersion)
      {
        StatusMessage = $"Could not read configs: {ex.Message}";
      }

      return;
    }

    if (rematch != _rematchVersion)
    {
      return;
    }

    _disk = snapshot;
    RebuildMatrix();
  }

  private static async Task<List<ScannedFolder>> ReadFoldersAsync(
    DiskSnapshotService service, IEnumerable<string> paths, GlobalSettings settings)
  {
    List<ScannedFolder> folders = [];
    foreach (string path in paths.Where(Directory.Exists))
    {
      folders.Add(await service.ReadFolderAsync(path, settings).ConfigureAwait(false));
    }

    return folders;
  }

  private static DiskSnapshot WithFolders(DiskSnapshot snapshot, IEnumerable<ScannedFolder> extra)
  {
    List<ScannedFolder> missing = extra
      .Where(f => !snapshot.Folders.Any(e => !e.IsGlobal && ProjectFolderRules.PathsEqual(e.Path, f.Path)))
      .ToList();
    return missing.Count == 0 ? snapshot : snapshot with { Folders = [.. snapshot.Folders, .. missing] };
  }

  private bool IsAddedFolder(string path) => _addedFolderPaths.Any(p => ProjectFolderRules.PathsEqual(p, path));

  private static McpRegistry CloneRegistry(McpRegistry registry) =>
    RegistryService.Deserialize(RegistryService.Serialize(registry));

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

    ScanChoice scanChoice = ToScanChoice(choice);
    SyncModels();
    TargetFolder? existing = ScanImportPlanner.FindPlace(_registry, folder);
    ScannedFolder applied = existing == null
      ? folder
      : folder with { Clients = folder.Clients & existing.EnabledClients };
    IReadOnlyList<ScanServerMatch> matches = _planner.Match([applied], _registry);
    ScanServerMatch? live = matches.FirstOrDefault(m =>
      string.Equals(m.FolderPath, match.FolderPath, StringComparison.Ordinal)
      && string.Equals(m.Found.Name, match.Found.Name, StringComparison.Ordinal)
      && m.Found.Client == match.Found.Client
      && (m.Found.Client & applied.Clients) != 0);
    if (live == null)
    {
      StatusMessage = $"{match.Found.Name} is no longer in {folderName}. Refresh and try again.";
      return;
    }

    List<ScanServerMatch> chosen = existing == null
      ? [.. matches.Where(m => !ReferenceEquals(m, live) && m.Kind == ScanMatchKind.Known && m.Library != null
          && _registry.Servers.Any(s => s.Id == m.Library.Id)), live]
      : [live];
    try
    {
      _planner.Apply(_registry, [applied], chosen, new Dictionary<ScanServerMatch, ScanChoice>
      {
        [live] = scanChoice,
      });
    }
    catch (ArgumentException ex)
    {
      StatusMessage = ex.Message;
      return;
    }

    TargetFolder place = ScanImportPlanner.FindPlace(_registry, applied)!;
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
      _planner.AddToLibrary(
        _registry, [.. _disk.Folders.Where(f => f.Servers.Any(s => s.Name == row.Name))], row.Name);
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
    if (window == null || _registry == null)
    {
      return;
    }

    IReadOnlyList<IStorageFolder> picked = await window.StorageProvider.OpenFolderPickerAsync(
      new FolderPickerOpenOptions { Title = "Select Project Folder", AllowMultiple = false });
    if (picked.Count > 0)
    {
      await OpenFolderAsync(picked[0].Path.LocalPath);
    }
  }

  internal async Task OpenFolderAsync(string path)
  {
    if (_registry == null || _diskService == null)
    {
      return;
    }

    ScannedFolder folder;
    try
    {
      folder = await _diskService.ReadFolderAsync(path, _registry.Settings);
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
      if (!IsAddedFolder(folder.Path))
      {
        _addedFolderPaths.Add(folder.Path);
      }

      DiskSnapshot withFolder = WithFolders(_disk, [folder]);
      _candidatePath = folder.Path;
      if (ReferenceEquals(withFolder, _disk))
      {
        RebuildMatrix();
        return;
      }

      _disk = withFolder;
      _ = RematchDiskAsync();
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

  private static ScanChoice ToScanChoice(DiffChoice choice) => choice switch
  {
    DiffChoice.KeepLibrary => ScanChoice.KeepLibrary,
    DiffChoice.UseFoundVersion => ScanChoice.UseFoundVersion,
    DiffChoice.ImportSeparately => ScanChoice.ImportSeparately,
    _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, null),
  };

  public ObservableCollection<string> ScanRoots { get; } = [];

  public bool HasScanRoots => ScanRoots.Count > 0;

  public string DefaultScanRootsText => _registry == null || HasScanRoots
    ? string.Empty
    : "No folders chosen. Projects are looked for in " +
      string.Join(", ", RegistryService.GetDefaultScanRoots(_registry.Settings)) + ".";

  private void NotifyPageChanged()
  {
    OnPropertyChanged(nameof(CurrentPage));
    OnPropertyChanged(nameof(ShowOverview));
    OnPropertyChanged(nameof(IsServerListActive));
    OnPropertyChanged(nameof(IsTargetListActive));
  }

  private void LoadScanRoots(GlobalSettings settings)
  {
    ScanRoots.Clear();
    foreach (string root in settings.ScanRoots)
    {
      ScanRoots.Add(root);
    }

    OnScanRootsChanged();
  }

  [RelayCommand]
  private async Task AddScanRootAsync()
  {
    Window? window = GetMainWindow();
    if (window == null || _registry == null)
    {
      return;
    }

    IReadOnlyList<IStorageFolder> folders = await window.StorageProvider.OpenFolderPickerAsync(
      new FolderPickerOpenOptions { Title = "Select Folder With Projects", AllowMultiple = false });
    if (folders.Count == 0)
    {
      return;
    }

    string path = folders[0].Path.LocalPath;
    if (_registry.Settings.ScanRoots.Contains(path, StringComparer.OrdinalIgnoreCase))
    {
      return;
    }

    _registry.Settings.ScanRoots.Add(path);
    ScanRoots.Add(path);
    OnScanRootsChanged();
    ScheduleCheck();
    _ = RefreshDiskAsync();
  }

  [RelayCommand]
  private void RemoveScanRoot(string? path)
  {
    if (_registry == null || path == null || !_registry.Settings.ScanRoots.Remove(path))
    {
      return;
    }

    ScanRoots.Remove(path);
    OnScanRootsChanged();
    ScheduleCheck();
    _ = RefreshDiskAsync();
  }

  private void OnScanRootsChanged()
  {
    OnPropertyChanged(nameof(HasScanRoots));
    OnPropertyChanged(nameof(DefaultScanRootsText));
  }
}
