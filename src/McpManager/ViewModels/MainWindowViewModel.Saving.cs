using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public partial class MainWindowViewModel
{
  private readonly WorkspaceChangeTracker _changeTracker = new();
  private bool _hasSavedSnapshot;
  private bool _bypassNavigationGuard;
  private bool _navigating;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(ShowDeployBar))]
  private bool _isDirty;

  [ObservableProperty] private bool _alsoDeploy;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(AlsoDeployText))]
  [NotifyPropertyChangedFor(nameof(CanAlsoDeploy))]
  private int _changedFileCount;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(ShowDeployBar))]
  [NotifyPropertyChangedFor(nameof(DeployBarText))]
  private int _behindCount;

  public string AlsoDeployText => ChangedFileCount switch
  {
    0 => "Also deploy (no files change)",
    1 => "Also deploy (1 file)",
    _ => $"Also deploy ({ChangedFileCount} files)",
  };

  public bool CanAlsoDeploy => ChangedFileCount > 0;

  public bool ShowDeployBar => !IsDirty && BehindCount > 0;

  public string DeployBarText => $"{PlaceCount(BehindCount)} not deployed";

  public Func<Task<UnsavedChoice>> PromptUnsavedAsync { get; set; } = ShowUnsavedChangesDialogAsync;

  internal Action<Func<Task>> ScheduleNavigationGuard { get; set; } = PostNavigationGuard;

  partial void OnIsDirtyChanged(bool value)
  {
    if (value)
    {
      AlsoDeploy = false;
    }
  }

  partial void OnChangedFileCountChanged(int value)
  {
    if (value == 0)
    {
      AlsoDeploy = false;
    }
  }

  [RelayCommand]
  private async Task SaveAsync()
  {
    if (_registry == null || !_hasSavedSnapshot)
    {
      return;
    }

    CheckTargets();
    string? error = WorkspaceValidator.Validate(_registry);
    if (error != null)
    {
      StatusMessage = error;
      return;
    }

    McpRegistry before = _changeTracker.SavedCopy();
    bool deploy = AlsoDeploy;
    IsLoading = true;
    try
    {
      await _registryService.SaveAsync(_registry);
      MarkWorkspaceSaved();
      StatusMessage = "Saved.";
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      StatusMessage = $"Error saving: {ex.Message}";
      return;
    }
    finally
    {
      IsLoading = false;
    }

    if (deploy)
    {
      await DeployAfterSaveAsync(before);
    }

    CheckTargets();
  }

  [RelayCommand]
  private void Cancel()
  {
    if (_registry == null || !_hasSavedSnapshot)
    {
      return;
    }

    Guid? serverId = SelectedServer?.Id;
    Guid? placeId = SelectedTarget?.Id;
    bool showSettings = ShowSettings;
    _registry = _changeTracker.RestoreSaved();
    _suppressMatrixRebuild = true;
    try
    {
      WithoutNavigationGuard(() =>
      {
        SelectedProfile = null;
        SelectedServer = null;
        SelectedTarget = null;
        RefreshFromRegistry();
        LoadSettings(_registry.Settings);
        InitializeManagement();
        SelectedServer = Servers.FirstOrDefault(s => s.Id == serverId);
        SelectedTarget = TargetFolders.FirstOrDefault(t => t.Id == placeId);
        ShowSettings = showSettings;
      });
    }
    finally
    {
      _suppressMatrixRebuild = false;
    }

    RebuildMatrix();
    StatusMessage = "Changes discarded.";
  }

  [RelayCommand]
  private async Task DeployNowAsync()
  {
    if (!EnsureCleanForDeploy())
    {
      return;
    }

    List<TargetFolderViewModel> behind = BehindPlaces();
    if (behind.Count == 0)
    {
      StatusMessage = "Nothing to deploy.";
      return;
    }

    await UpdateTargetsAsync(behind);
  }

  [RelayCommand]
  private async Task DeployPlaceAsync(TargetFolderViewModel? place)
  {
    if (place?.IsManagedTarget != true || !EnsureCleanForDeploy())
    {
      return;
    }

    await UpdateTargetsAsync([place]);
  }

  [RelayCommand]
  private async Task ReviewDeployAsync()
  {
    Window? window = GetMainWindow();
    if (window == null || !EnsureCleanForDeploy())
    {
      return;
    }

    List<ServerDiff> items;
    try
    {
      items = BuildDeployReview();
    }
    catch (Exception ex)
    {
      StatusMessage = $"Could not prepare the review: {ex.Message}";
      return;
    }

    if (items.Count == 0)
    {
      StatusMessage = "No server changes to show; only settings files differ.";
    }

    DiffViewModel diff = new("In file", "MCP Manager will write", items, []);
    bool deploy = await new Views.ReviewDeployDialog(diff).ShowDialog<bool>(window);
    if (deploy)
    {
      await DeployNowAsync();
    }
  }

  public async Task<bool> ConfirmLeaveAsync()
  {
    if (_registry == null || !_hasSavedSnapshot)
    {
      return true;
    }

    CheckTargets();
    if (!IsDirty)
    {
      return true;
    }

    switch (await PromptUnsavedAsync())
    {
      case UnsavedChoice.Save:
        await SaveAsync();
        return !IsDirty;
      case UnsavedChoice.Discard:
        Cancel();
        return true;
      default:
        return false;
    }
  }

  private void MarkWorkspaceSaved()
  {
    _changeTracker.MarkSaved(_registry!);
    _hasSavedSnapshot = true;
  }

  private void UpdateSaveState(PlaceStatusService statusService, int behindCount)
  {
    BehindCount = behindCount;
    if (_registry == null || !_hasSavedSnapshot)
    {
      return;
    }

    IsDirty = _changeTracker.IsDirty(_registry);
    ChangedFileCount = IsDirty ? statusService.CountChangedFiles(_changeTracker.SavedCopy(), _registry) : 0;
  }

  private async Task DeployAfterSaveAsync(McpRegistry before)
  {
    PlaceStatusService statusService = new(_configExportService);
    HashSet<Guid> changed = statusService.GetChangedPlaces(before, _registry!).Select(t => t.Id).ToHashSet();
    Dictionary<Guid, PlaceStatus> statuses = ManagedTargets.ToDictionary(
      t => t.Id, t => statusService.GetStatus(t.Model, _registry!));
    List<TargetFolderViewModel> candidates = ManagedTargets
      .Where(t => changed.Contains(t.Id) || statuses[t.Id] == PlaceStatus.Behind)
      .ToList();
    List<TargetFolderViewModel> external = candidates
      .Where(t => statuses[t.Id] == PlaceStatus.ExternallyChanged)
      .ToList();
    List<TargetFolderViewModel> targets = candidates.Except(external).ToList();
    string skipped = external.Count == 0
      ? string.Empty
      : $" Not deployed because they changed outside MCP Manager: {string.Join(", ", external.Select(t => t.Name))}.";
    if (targets.Count == 0)
    {
      StatusMessage = "Saved. No files needed deploying." + skipped;
      return;
    }

    StatusMessage = await UpdateTargetsAsync(targets)
      ? $"Saved and deployed {PlaceCount(targets.Count)}." + skipped
      : $"Saved, but not deployed: {StatusMessage}";
  }

  private bool EnsureCleanForDeploy()
  {
    if (_registry == null)
    {
      return false;
    }

    CheckTargets();
    if (!IsDirty)
    {
      return true;
    }

    StatusMessage = "Save or cancel your changes before deploying.";
    return false;
  }

  private List<TargetFolderViewModel> BehindPlaces() => ManagedTargets
    .Where(t => _placeStatuses.GetValueOrDefault(t.Id) == PlaceStatus.Behind)
    .ToList();

  internal List<ServerDiff> BuildDeployReview()
  {
    ConfigFileDiffService diffService = new(_configImportService);
    List<ServerDiff> items = [];
    foreach (TargetFolderViewModel place in BehindPlaces())
    {
      string placeName = string.IsNullOrWhiteSpace(place.Name) ? place.Path : place.Name;
      TargetFolder effective = ProfileResolver.Resolve(place.Model, _registry!.Profiles);
      Dictionary<string, TargetClientFlags> clients = [];
      foreach ((TargetClientFlags client, string path) in _configExportService.GetConfigFilePaths(effective, _registry.Settings))
      {
        clients[Path.GetFullPath(path)] = client;
      }

      foreach ((string path, string content) in _configExportService.PreviewConfigs(
                 effective, _registry.Servers, _registry.Settings))
      {
        if (!clients.TryGetValue(Path.GetFullPath(path), out TargetClientFlags client))
        {
          continue;
        }

        string? onDisk = File.Exists(path) ? File.ReadAllText(path) : null;
        string fileName = Path.GetFileName(path);
        items.AddRange(diffService.Compare(path, client, onDisk, content).Servers
          .Where(s => s.Kind != DiffKind.Same)
          .Select(s => s.Details with { ServerName = $"{s.ServerName} · {placeName} · {fileName}" }));
      }
    }

    return items;
  }

  private static string PlaceCount(int count) => count == 1 ? "1 place" : $"{count} places";

  private NavigationSnapshot CurrentNavigation() => new(SelectedServer, SelectedTarget, ShowSettings);

  private void Navigate(NavigationSnapshot previous, Action apply)
  {
    if (_navigating)
    {
      apply();
      return;
    }

    _navigating = true;
    try
    {
      apply();
    }
    finally
    {
      _navigating = false;
    }

    if (_bypassNavigationGuard || _registry == null || !_hasSavedSnapshot)
    {
      return;
    }

    ScheduleNavigationGuard(async () =>
    {
      if (!await ConfirmLeaveAsync())
      {
        RestoreNavigation(previous);
      }
    });
  }

  private void RestoreNavigation(NavigationSnapshot previous) => WithoutNavigationGuard(() =>
  {
    SelectedProfile = null;
    SelectedServer = previous.Server != null && Servers.Contains(previous.Server) ? previous.Server : null;
    SelectedTarget = previous.Target != null && TargetFolders.Contains(previous.Target) ? previous.Target : null;
    ShowSettings = previous.ShowSettings;
  });

  private void WithoutNavigationGuard(Action apply)
  {
    bool previous = _bypassNavigationGuard;
    _bypassNavigationGuard = true;
    try
    {
      apply();
    }
    finally
    {
      _bypassNavigationGuard = previous;
    }
  }

  private static void PostNavigationGuard(Func<Task> guard)
  {
    if (GetMainWindow() != null)
    {
      Dispatcher.UIThread.Post(() => _ = guard());
    }
  }

  private static async Task<UnsavedChoice> ShowUnsavedChangesDialogAsync()
  {
    Window? window = GetMainWindow();
    if (window == null)
    {
      return UnsavedChoice.Stay;
    }

    UnsavedChoice? choice = await new Views.UnsavedChangesDialog().ShowDialog<UnsavedChoice?>(window);
    return choice ?? UnsavedChoice.Stay;
  }
}
