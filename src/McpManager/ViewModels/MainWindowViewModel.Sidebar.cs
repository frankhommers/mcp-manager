using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace McpManager.ViewModels;

public partial class MainWindowViewModel
{
  public ObservableCollection<McpServerViewModel> SidebarServers { get; } = [];
  public ObservableCollection<TargetFolderViewModel> SidebarTargets { get; } = [];

  private void InitializeSidebar()
  {
    OnServersChanged(Servers, Servers);
    OnTargetFoldersChanged(TargetFolders, TargetFolders);
  }

  partial void OnServersChanged(
    ObservableCollection<McpServerViewModel>? oldValue, ObservableCollection<McpServerViewModel> newValue)
  {
    if (oldValue != null)
    {
      oldValue.CollectionChanged -= OnSidebarServersChanged;
    }

    newValue.CollectionChanged += OnSidebarServersChanged;
    RefreshSidebarServers();
  }

  partial void OnTargetFoldersChanged(
    ObservableCollection<TargetFolderViewModel>? oldValue, ObservableCollection<TargetFolderViewModel> newValue)
  {
    if (oldValue != null)
    {
      oldValue.CollectionChanged -= OnSidebarTargetsChanged;
    }

    newValue.CollectionChanged += OnSidebarTargetsChanged;
    RefreshSidebarTargets();
  }

  private void OnSidebarServersChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSidebarServers();

  private void OnSidebarTargetsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSidebarTargets();

  private void OnSidebarServerChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(McpServerViewModel.ListName))
    {
      RefreshSidebarServers();
    }
  }

  private void OnSidebarTargetChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName is nameof(TargetFolderViewModel.Name) or nameof(TargetFolderViewModel.IsClipboard)
        or nameof(TargetFolderViewModel.IsQuickExport) or nameof(TargetFolderViewModel.IsGlobal))
    {
      RefreshSidebarTargets();
    }
  }

  private void RefreshSidebarServers()
  {
    foreach (McpServerViewModel server in SidebarServers)
    {
      server.PropertyChanged -= OnSidebarServerChanged;
    }

    foreach (McpServerViewModel server in Servers)
    {
      server.PropertyChanged += OnSidebarServerChanged;
    }

    CollectionViewUpdater.Update(SidebarServers, Servers
      .OrderBy(s => s.ListName, StringComparer.CurrentCultureIgnoreCase)
      .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
      .ThenBy(s => s.Id));
  }

  private void RefreshSidebarTargets()
  {
    foreach (TargetFolderViewModel target in SidebarTargets)
    {
      target.PropertyChanged -= OnSidebarTargetChanged;
    }

    foreach (TargetFolderViewModel target in TargetFolders)
    {
      target.PropertyChanged += OnSidebarTargetChanged;
    }

    CollectionViewUpdater.Update(SidebarTargets, TargetFolders
      .OrderBy(t => t.IsClipboard ? 0 : t.IsQuickExport ? 1 : t.IsGlobal ? 2 : 3)
      .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
      .ThenBy(t => t.Id));
  }
}
