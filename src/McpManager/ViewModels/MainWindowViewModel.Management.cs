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
  public string UpdateSummary => $"{ManagedTargets.Count(t => t.NeedsUpdate || t.HasExternalChanges)} of {ManagedTargets.Count()} targets need attention";
  public string ServerUsage => SelectedServer == null ? "" : "Used by: " + string.Join(", ", ManagedTargets
    .Where(t => t.ServerSelections.Any(s => s.ServerId == SelectedServer.Id && s.IsEnabled)).Select(t => t.Name));
  [ObservableProperty] private ProfileViewModel? _selectedProfile;
  [ObservableProperty] private ProfileViewModel? _bulkProfile;
  public string ProfileUsage => SelectedProfile == null ? "" : "Linked targets: " + string.Join(", ",
    ManagedTargets.Where(t => t.SelectedProfile == SelectedProfile).Select(t => t.Name));

  [RelayCommand]
  private void AssignProfileToSelected()
  {
    if (BulkProfile == null) { StatusMessage = "Choose a profile first."; return; }
    List<TargetFolderViewModel> selected = ManagedTargets.Where(t => t.IsSelectedForBulk).ToList();
    foreach (TargetFolderViewModel target in selected) target.SelectedProfile = BulkProfile;
    CheckTargets();
    StatusMessage = $"Assigned '{BulkProfile.Name}' to {selected.Count} targets. Explicit overrides were kept.";
  }

  [RelayCommand]
  private Task UpdateSelectedTargetsAsync() => UpdateTargetsAsync(ManagedTargets.Where(t => t.IsSelectedForBulk).ToList());

  private int _checkVersion;
  private bool _checking;

  partial void OnSelectedProfileChanged(ProfileViewModel? value)
  {
    if (value != null)
    {
      SelectedServer = null;
      SelectedTarget = null;
      ShowSettings = false;
    }
    OnPropertyChanged(nameof(ProfileUsage));
    OnPropertyChanged(nameof(CurrentPage));
    OnPropertyChanged(nameof(ShowOverview));
    OnPropertyChanged(nameof(IsServerListActive));
    OnPropertyChanged(nameof(IsTargetListActive));
  }

  private void InitializeManagement()
  {
    Profiles.Clear();
    foreach (McpProfile profile in _registry!.Profiles) AttachProfile(profile);
    foreach (TargetFolderViewModel target in TargetFolders) AttachTarget(target);
    CheckTargets();
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
      throw new InvalidOperationException($"Profile for '{target.Name}' no longer exists.");
    target.SelectedProfile = Profiles.FirstOrDefault(p => p.Id == target.Model.ProfileId);
    target.ConfigurationChanged += (_, _) => ScheduleCheck();
    target.ApplyInheritance();
    OnPropertyChanged(nameof(ManagedTargets));
    ScheduleCheck();
  }

  [RelayCommand]
  private void AddProfile()
  {
    if (_registry == null) return;
    McpProfile profile = new();
    _registry.Profiles.Add(profile);
    AttachProfile(profile);
    SelectedProfile = Profiles.Last();
  }

  [RelayCommand]
  private void DeleteProfile()
  {
    if (_registry == null || SelectedProfile == null) return;
    if (TargetFolders.Any(t => t.SelectedProfile == SelectedProfile))
    {
      StatusMessage = "Detach or reassign this profile's targets before deleting it.";
      return;
    }
    _registry.Profiles.Remove(SelectedProfile.Profile);
    Profiles.Remove(SelectedProfile);
    SelectedProfile = null;
  }

  [RelayCommand]
  private void DetachProfile() { if (SelectedTarget != null) SelectedTarget.SelectedProfile = null; }

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
      foreach (TargetFolderViewModel target in ManagedTargets)
      {
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
      OnPropertyChanged(nameof(UpdateSummary));
      OnPropertyChanged(nameof(ServerUsage));
      OnPropertyChanged(nameof(ProfileUsage));
    }
    catch (Exception ex) { StatusMessage = $"Could not check targets: {ex.Message}"; }
    finally { _checking = false; }
  }

  [RelayCommand]
  private async Task ReapplyTargetAsync()
  {
    if (SelectedTarget?.IsManagedTarget == true) await UpdateTargetsAsync([SelectedTarget], true);
  }

  private async Task UpdateTargetsAsync(List<TargetFolderViewModel> targets, bool overwriteExternal = false)
  {
    if (_registry == null || IsLoading) return;
    CheckTargets();
    IsLoading = true;
    try
    {
      if (targets.Count == 0) { StatusMessage = "No managed targets yet."; return; }
      if (!overwriteExternal && targets.Any(t => t.HasExternalChanges))
      {
        StatusMessage = "External changes detected. Open the affected target, inspect its config, then explicitly reapply central settings.";
        return;
      }
      SyncModels();
      foreach (TargetFolderViewModel target in targets.Where(t => t.Model.LastAppliedAt == null))
      {
        ExistingTargetServers existing = await new TargetConfigSelectionService(_configExportService).ReadAsync(target.Model, _registry.Settings);
        if (existing.Servers.Any(s => s.ManagedId is Guid id && !_registry.Servers.Any(server => server.Id == id)))
          throw new InvalidOperationException($"Import the existing managed servers from '{target.Name}' before its first update.");
        HashSet<Guid> managedIds = existing.Servers.Select(s => s.ManagedId).OfType<Guid>().ToHashSet();
        foreach (ServerSelectionViewModel selection in target.ServerSelections.Where(s =>
                   managedIds.Contains(s.ServerId) && !s.IsEnabled && !target.Model.DisabledServers.Contains(s.ServerId)))
          selection.IsEnabled = true;
        target.UpdateModel();
      }
      await _registryService.SaveAsync(_registry);
      List<TargetFolder> effective = targets.Select(t => ProfileResolver.Resolve(t.Model, _registry.Profiles)).ToList();
      foreach (TargetFolder target in effective)
      {
        int client = (int)target.EnabledClients;
        if (client == 0 || (client & (client - 1)) != 0 || !System.IO.Path.IsPathFullyQualified(target.Path))
          throw new InvalidOperationException($"'{target.Name}' needs one client and an absolute destination path.");
      }
      Dictionary<string, string> before = [];
      foreach (TargetFolder target in effective)
      {
        foreach (string path in _configExportService.GetConfigFilePaths(target, _registry.Settings).Values)
          before[System.IO.Path.GetFullPath(path)] = TargetSyncService.FileHash(path);
      }
      if (effective.Any(t => t.EnabledClients == TargetClientFlags.ClaudeCodeGlobal))
      {
        string permissions = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
        before[permissions] = TargetSyncService.FileHash(permissions);
      }
      Dictionary<string, string>? configs = await _configExportService.PrepareExportAsync(
        effective, _registry.Servers, _registry.Settings, ResolveExportConflictAsync);
      if (configs == null) { StatusMessage = "Update cancelled. No target files changed."; return; }
      if (before.Any(p => TargetSyncService.FileHash(p.Key) != p.Value))
        throw new InvalidOperationException("A configuration changed during preparation. Check targets and try again.");
      await _configExportService.WriteConfigsAsync(configs);
      foreach (TargetFolderViewModel target in targets)
      {
        HashSet<string> paths = _configExportService.GetConfigFilePaths(target.Model, _registry.Settings)
          .Values.Select(System.IO.Path.GetFullPath).ToHashSet();
        if (target.Model.EnabledClients == TargetClientFlags.ClaudeCodeGlobal)
          paths.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"));
        TargetSyncService.RecordApplied(target.Model, configs.Where(p => paths.Contains(p.Key))
          .ToDictionary(p => p.Key, p => p.Value));
        target.HasExternalChanges = false;
        try
        {
          target.NeedsUpdate = TargetSyncService.NeedsUpdate(_configExportService.PreviewConfigs(
            ProfileResolver.Resolve(target.Model, _registry.Profiles), _registry.Servers, _registry.Settings));
          target.SyncStatus = target.NeedsUpdate ? "Updated with differences · inspect target"
            : "Updated · reload the client if needed";
        }
        catch (ExportConflictException)
        {
          target.NeedsUpdate = true;
          target.SyncStatus = "Updated · conflicting existing entries kept";
        }
      }
      await _registryService.SaveAsync(_registry);
      StatusMessage = $"Updated {targets.Count} targets. Backups saved beside existing configurations.";
    }
    catch (Exception ex)
    {
      foreach (TargetFolderViewModel target in targets) target.SyncStatus = $"Update failed: {ex.Message}";
      StatusMessage = $"Update failed: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
      OnPropertyChanged(nameof(UpdateSummary));
    }
  }
}
