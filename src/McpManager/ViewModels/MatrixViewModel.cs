using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public partial class MatrixViewModel : ViewModelBase, IDisposable
{
  private readonly List<McpServerViewModel> _servers;
  private readonly List<PlaceColumnViewModel> _allColumns;
  private bool _syncingProjects;

  [ObservableProperty] private string _serverFilter = string.Empty;

  [ObservableProperty] private PlaceColumnViewModel? _selectedProject;

  public MatrixViewModel(
    IEnumerable<McpServerViewModel> servers, IEnumerable<TargetFolderViewModel> places, Guid? selectedProjectId = null)
  {
    _servers = servers.ToList();
    _allColumns = places.Where(p => p.IsManagedTarget).Select(p => new PlaceColumnViewModel(p)).ToList();
    _allColumns.Sort(CompareColumns);
    foreach (McpServerViewModel server in _servers)
    {
      server.PropertyChanged += OnServerChanged;
    }

    foreach (PlaceColumnViewModel column in _allColumns)
    {
      column.Place.PropertyChanged += OnPlaceChanged;
      column.Place.ServerSelections.CollectionChanged += OnSelectionsChanged;
    }

    _syncingProjects = true;
    SyncProjects();
    SelectedProject = Projects.FirstOrDefault(p => p.Place.Id == selectedProjectId) ?? Projects.FirstOrDefault();
    _syncingProjects = false;
    Refresh();
  }

  public event EventHandler<Guid?>? SelectedProjectChanged;

  public void Dispose()
  {
    foreach (McpServerViewModel server in _servers)
    {
      server.PropertyChanged -= OnServerChanged;
    }

    foreach (PlaceColumnViewModel column in _allColumns)
    {
      column.Place.PropertyChanged -= OnPlaceChanged;
      column.Place.ServerSelections.CollectionChanged -= OnSelectionsChanged;
    }
  }

  public ObservableCollection<PlaceColumnViewModel> Projects { get; } = [];

  public bool HasProjects => Projects.Count > 0;

  public ObservableCollection<PlaceColumnViewModel> Columns { get; } = [];

  public ObservableCollection<MatrixRowViewModel> Rows { get; } = [];

  public void SetStatus(Guid placeId, PlaceStatus status)
  {
    PlaceColumnViewModel? column = _allColumns.FirstOrDefault(c => c.Place.Id == placeId);
    if (column != null)
    {
      column.Status = status;
    }
  }

  public void Refresh()
  {
    Columns.Clear();
    foreach (PlaceColumnViewModel column in _allColumns.Where(c => c.IsGlobal || c == SelectedProject))
    {
      Columns.Add(column);
    }

    Rows.Clear();
    foreach (McpServerViewModel server in _servers
      .Where(MatchesServerFilter)
      .OrderBy(s => s.ListName, StringComparer.CurrentCultureIgnoreCase))
    {
      Rows.Add(new MatrixRowViewModel(server, Columns.Select(c => CellFor(c, server)).ToList()));
    }
  }

  partial void OnSelectedProjectChanged(PlaceColumnViewModel? value)
  {
    if (_syncingProjects)
    {
      return;
    }

    Refresh();
    SelectedProjectChanged?.Invoke(this, value?.Place.Id);
  }

  private void SyncProjects()
  {
    List<PlaceColumnViewModel> projects = _allColumns.Where(c => !c.IsGlobal).ToList();
    if (projects.SequenceEqual(Projects))
    {
      return;
    }

    PlaceColumnViewModel? selected = SelectedProject;
    bool wasSyncing = _syncingProjects;
    _syncingProjects = true;
    Projects.Clear();
    foreach (PlaceColumnViewModel project in projects)
    {
      Projects.Add(project);
    }

    SelectedProject = selected != null && Projects.Contains(selected) ? selected : Projects.FirstOrDefault();
    _syncingProjects = wasSyncing;
    OnPropertyChanged(nameof(HasProjects));
  }

  private void OnServerChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName is nameof(McpServerViewModel.ListName) or nameof(McpServerViewModel.Name)
      or nameof(McpServerViewModel.DisplayName))
    {
      Refresh();
    }
  }

  private void OnPlaceChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName is nameof(TargetFolderViewModel.Name) or nameof(TargetFolderViewModel.Path)
      or nameof(TargetFolderViewModel.SelectedClient) or nameof(TargetFolderViewModel.IsGlobal)
      or nameof(TargetFolderViewModel.ClientName))
    {
      foreach (PlaceColumnViewModel column in _allColumns)
      {
        column.NotifyPlaceChanged();
      }

      _allColumns.Sort(CompareColumns);
      SyncProjects();
      Refresh();
    }
  }

  private void OnSelectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

  private static int CompareColumns(PlaceColumnViewModel left, PlaceColumnViewModel right)
  {
    int group = left.IsGlobal.CompareTo(right.IsGlobal) * -1;
    return group != 0 ? group : StringComparer.CurrentCultureIgnoreCase.Compare(left.Header, right.Header);
  }

  partial void OnServerFilterChanged(string value) => Refresh();

  private static ServerSelectionViewModel CellFor(PlaceColumnViewModel column, McpServerViewModel server) =>
    column.Place.ServerSelections.FirstOrDefault(s => s.ServerId == server.Id)
    ?? new ServerSelectionViewModel { ServerId = Guid.Empty, ServerName = server.ListName };

  private bool MatchesServerFilter(McpServerViewModel server)
  {
    string filter = ServerFilter.Trim();
    return filter.Length == 0
      || server.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
      || server.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
  }
}
