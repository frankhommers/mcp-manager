using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public partial class MainWindowViewModel
{
  public string[] ClipboardFormats { get; } = ["ClaudeCode", "ClaudeDesktop", "OpenCode"];
  public ObservableCollection<ProfileViewModel> Profiles { get; } = [];
  public IEnumerable<TargetFolderViewModel> ManagedTargets => TargetFolders.Where(t => t.IsManagedTarget)
    .OrderBy(t => t.Scope).ThenBy(t => t.Name);
  public string ServerUsage => SelectedServer == null ? "" : "Used by: " + string.Join(", ", ManagedTargets
    .Where(t => t.ServerSelections.Any(s => s.ServerId == SelectedServer.Id && s.IsEnabled)).Select(t => t.Name));
  private readonly Dictionary<Guid, PlaceStatus> _placeStatuses = [];
  [ObservableProperty] private MatrixViewModel _matrix = new([], []);
  [ObservableProperty] private ProfileViewModel? _selectedProfile;

  private int _checkVersion;
  private bool _checking;

  partial void OnSelectedProfileChanged(ProfileViewModel? value)
  {
    if (value != null)
    {
      SelectedServer = null;
      SelectedTarget = null;
      ShowSettings = false;
      ShowScan = false;
    }
    NotifyPageChanged();
  }

  private bool _suppressMatrixRebuild;
  private UiState? _uiState;

  private void RebuildMatrix()
  {
    if (_suppressMatrixRebuild) return;
    MatrixViewModel previous = Matrix;
    _uiState ??= UiStateStore.NextTo(_registryService.RegistryFilePath).Load();
    IReadOnlyList<string> scanRoots = _registry == null ? [] : RegistryService.GetDefaultScanRoots(_registry.Settings);
    MatrixViewModel matrix = new(Servers, TargetFolders, _candidatePath == null ? _uiState.LastProjectId : null, _disk,
      scanRoots, _candidatePath, _addedFolderPaths)
    {
      ServerFilter = Matrix.ServerFilter,
    };
    if (_candidatePath != null && matrix.SelectedChoice?.Kind != ProjectChoiceKind.Found)
    {
      _candidatePath = null;
      matrix.SelectedChoice = matrix.Choices.FirstOrDefault(c => c.Column?.Place.Id == _uiState.LastProjectId)
        ?? matrix.SelectedChoice;
    }

    matrix.SelectedProjectChanged += OnMatrixProjectChanged;
    matrix.OtherFolderRequested += OnOtherFolderRequested;
    matrix.CandidateEdited += OnCandidateEdited;
    foreach ((Guid id, PlaceStatus status) in _placeStatuses)
    {
      matrix.SetStatus(id, status);
    }

    Matrix = matrix;
    previous.SelectedProjectChanged -= OnMatrixProjectChanged;
    previous.OtherFolderRequested -= OnOtherFolderRequested;
    previous.CandidateEdited -= OnCandidateEdited;
    previous.Dispose();
  }

  private void OnMatrixProjectChanged(object? sender, Guid? projectId)
  {
    if (projectId == null && sender is MatrixViewModel { SelectedChoice.Folder: { } folder })
    {
      _candidatePath = folder.Path;
      return;
    }

    _candidatePath = null;
    RememberProject(projectId);
  }

  private void RememberProject(Guid? projectId)
  {
    _uiState ??= UiStateStore.NextTo(_registryService.RegistryFilePath).Load();
    if (_uiState.LastProjectId == projectId)
    {
      return;
    }

    _uiState = _uiState with { LastProjectId = projectId };
    UiStateStore.NextTo(_registryService.RegistryFilePath).Save(_uiState);
  }

  private void InitializeManagement()
  {
    AttachManagement();
    MarkWorkspaceSaved();
    CheckTargets();
  }

  private void AttachManagement()
  {
    Profiles.Clear();
    foreach (McpProfile profile in _registry!.Profiles) AttachProfile(profile);
    foreach (TargetFolderViewModel target in TargetFolders) AttachTarget(target);
    SyncModels();
  }

  private void AttachProfile(McpProfile profile)
  {
    ProfileViewModel vm = new(profile, _registry!.Servers);
    vm.ConfigurationChanged += (_, _) => ScheduleCheck();
    vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ProfileViewModel.Name)) ScheduleCheck(); };
    Profiles.Add(vm);
  }

  private void AttachTarget(TargetFolderViewModel target)
  {
    if (target.Model.ProfileId is Guid id && Profiles.All(p => p.Id != id))
      throw new InvalidOperationException($"The shared settings for place '{target.Name}' are missing.");
    target.SelectedProfile = Profiles.FirstOrDefault(p => p.Id == target.Model.ProfileId);
    target.ConfigurationChanged += (_, _) => ScheduleCheck();
    target.ApplyInheritance();
    OnPropertyChanged(nameof(ManagedTargets));
    ScheduleCheck();
  }

  [RelayCommand]
  private void OpenTarget(TargetFolderViewModel target) => SelectedTarget = target;

  private void ScheduleCheck() => _ = CheckAfterEditAsync();

  private async Task CheckAfterEditAsync()
  {
    if (_checking || _registry == null) return;
    int version = ++_checkVersion;
    await Task.Delay(250);
    if (version != _checkVersion || IsLoading) return;
    CheckTargets();
  }

  private void SyncModels()
  {
    if (_registry == null) return;
    foreach (McpServerViewModel server in Servers) server.UpdateModel();
    foreach (ProfileViewModel profile in Profiles)
    {
      profile.RefreshServers(_registry.Servers);
      profile.UpdateProfile();
    }
    foreach (TargetFolderViewModel target in TargetFolders)
    {
      target.RefreshServers(_registry.Servers);
      target.UpdateModel();
    }
  }

  [RelayCommand]
  private void CheckTargets()
  {
    if (_registry == null || _checking) return;
    _checking = true;
    try
    {
      SyncModels();
      PlaceStatusService statusService = new(_configExportService);
      int behindCount = 0;
      foreach (TargetFolderViewModel target in ManagedTargets)
      {
        PlaceStatus status = statusService.GetStatus(target.Model, _registry);
        _placeStatuses[target.Id] = status;
        if (status == PlaceStatus.Behind) behindCount++;
        Matrix.SetStatus(target.Id, status);
        try
        {
          TargetFolder effective = ProfileResolver.Resolve(target.Model, _registry.Profiles);
          target.HasExternalChanges = TargetSyncService.HasExternalChanges(target.Model);
          Dictionary<string, string> preview = _configExportService.PreviewConfigs(effective, _registry.Servers, _registry.Settings);
          target.ChangeSummary = "Desired servers: " + string.Join(", ", _registry.Servers
            .Where(s => effective.EnabledServers.Contains(s.Id) && !effective.DisabledServers.Contains(s.Id)).Select(s => s.Name))
            + Environment.NewLine + string.Join(Environment.NewLine, preview.Select(p =>
              $"{(TargetSyncService.FileHash(p.Key) == TargetSyncService.ContentHash(p.Value) ? "Unchanged" : System.IO.File.Exists(p.Key) ? "Update" : "Create")}: {p.Key}"));
          target.NeedsUpdate = TargetSyncService.NeedsUpdate(preview);
          target.SyncStatus = target.HasExternalChanges ? "Externally changed · review before updating"
            : target.NeedsUpdate ? $"Update needed · {preview.Count} file(s)"
            : "Up to date";
        }
        catch (Exception ex)
        {
          target.ChangeSummary = ex.Message;
          target.NeedsUpdate = true;
          target.SyncStatus = $"Needs attention: {ex.Message}";
        }
      }
      OnPropertyChanged(nameof(ManagedTargets));
      OnPropertyChanged(nameof(ServerUsage));
      UpdateSaveState(statusService, behindCount);
    }
    catch (Exception ex) { StatusMessage = $"Could not check places: {ex.Message}"; }
    finally { _checking = false; }
  }

  private async Task<bool> UpdateTargetsAsync(List<TargetFolderViewModel> targets, bool overwriteExternal = false)
  {
    if (_registry == null) return false;
    if (IsLoading)
    {
      StatusMessage = "Another operation is still running. Deploy again when it has finished.";
      return false;
    }
    if (!EnsureCleanForDeploy()) return false;
    IsLoading = true;
    try
    {
      if (targets.Count == 0) { StatusMessage = "No places to deploy."; return false; }
      if (!overwriteExternal && targets.Any(t => t.HasExternalChanges))
      {
        StatusMessage = "External changes detected. Open the place and use Compare before deploying.";
        return false;
      }
      DeployResult result;
      try
      {
        result = await new TargetDeployService(_configExportService).DeployAsync(
          targets.Select(t => t.Model).ToList(), _registry, ResolveExportConflictAsync, overwriteExternal);
      }
      finally
      {
        foreach (TargetFolderViewModel target in targets) target.ReloadSelectionsFromModel();
      }
      if (result.Cancelled) { StatusMessage = "Deploy cancelled. No files changed."; return false; }
      foreach (TargetFolderViewModel target in targets)
      {
        target.HasExternalChanges = false;
        try
        {
          target.NeedsUpdate = TargetSyncService.NeedsUpdate(_configExportService.PreviewConfigs(
            ProfileResolver.Resolve(target.Model, _registry.Profiles), _registry.Servers, _registry.Settings));
          target.SyncStatus = target.NeedsUpdate ? "Deployed with differences · inspect place"
            : "Deployed · reload the client if needed";
        }
        catch (ExportConflictException)
        {
          target.NeedsUpdate = true;
          target.SyncStatus = "Deployed · conflicting existing entries kept";
        }
      }
      SyncModels();
      await _registryService.SaveAsync(_registry);
      MarkWorkspaceSaved();
      StatusMessage = $"Deployed {PlaceCount(targets.Count)}. Backups saved beside existing configurations.";
      return true;
    }
    catch (Exception ex)
    {
      foreach (TargetFolderViewModel target in targets) target.SyncStatus = $"Deploy failed: {ex.Message}";
      StatusMessage = $"Deploy failed: {ex.Message}";
      return false;
    }
    finally
    {
      IsLoading = false;
      CheckTargets();
      _ = RefreshDiskAsync();
    }
  }
}
