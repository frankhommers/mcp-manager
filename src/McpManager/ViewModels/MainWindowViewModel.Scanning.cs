using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public partial class MainWindowViewModel
{
  [ObservableProperty] private bool _showScan;

  public ScanViewModel Scan { get; }

  public ObservableCollection<string> ScanRoots { get; } = [];

  public bool HasScanRoots => ScanRoots.Count > 0;

  public string DefaultScanRootsText => _registry == null || HasScanRoots
    ? string.Empty
    : "No folders chosen. Scanning uses " +
      string.Join(", ", RegistryService.GetDefaultScanRoots(_registry.Settings)) + ".";

  partial void OnShowScanChanged(bool oldValue, bool newValue) =>
    Navigate(new NavigationSnapshot(SelectedServer, SelectedTarget, ShowSettings, oldValue), () =>
    {
      if (!newValue)
      {
        Scan.CancelWalkthrough();
      }

      NotifyPageChanged();
    });

  private ScanViewModel CreateScan(ConfigScanService scanService) => new(
    scanService,
    new ScanImportPlanner(_configImportService, _configExportService),
    () =>
    {
      SyncModels();
      return _registry;
    },
    OnScanApplied);

  private void NotifyPageChanged()
  {
    OnPropertyChanged(nameof(CurrentPage));
    OnPropertyChanged(nameof(ShowOverview));
    OnPropertyChanged(nameof(IsServerListActive));
    OnPropertyChanged(nameof(IsTargetListActive));
  }

  [RelayCommand]
  private void OpenScan()
  {
    Navigate(CurrentNavigation(), () =>
    {
      SelectedProfile = null;
      SelectedServer = null;
      SelectedTarget = null;
      ShowSettings = false;
      ShowScan = true;
    });
    if (!Scan.ScanCommand.IsRunning && !Scan.IsWalkthroughOpen)
    {
      Scan.ScanCommand.Execute(null);
    }
  }

  private void OnScanApplied(int count)
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

    RebuildMatrix();
    CheckTargets();
    StatusMessage = $"Added {PlaceCount(count)}. Save to keep them.";
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
      new FolderPickerOpenOptions { Title = "Select Scan Folder", AllowMultiple = false });
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
  }

  private void OnScanRootsChanged()
  {
    OnPropertyChanged(nameof(HasScanRoots));
    OnPropertyChanged(nameof(DefaultScanRootsText));
  }
}
