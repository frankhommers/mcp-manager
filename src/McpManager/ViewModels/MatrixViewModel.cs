using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public partial class MatrixViewModel : ViewModelBase
{
  private readonly List<McpServerViewModel> _servers;
  private readonly List<PlaceColumnViewModel> _allColumns;

  [ObservableProperty] private string _serverFilter = string.Empty;

  [ObservableProperty] private string _placeFilter = string.Empty;

  public MatrixViewModel(IEnumerable<McpServerViewModel> servers, IEnumerable<TargetFolderViewModel> places)
  {
    _servers = servers.ToList();
    _allColumns = places.Where(p => p.IsManagedTarget)
      .Select(p => new PlaceColumnViewModel(p))
      .OrderBy(c => c.IsGlobal ? 0 : 1)
      .ThenBy(c => c.Header, StringComparer.CurrentCultureIgnoreCase)
      .ToList();
    Refresh();
  }

  public ObservableCollection<PlaceColumnViewModel> Columns { get; } = [];

  public ObservableCollection<MatrixRowViewModel> Rows { get; } = [];

  public bool HasMissingCells => Rows.Any(r => r.Cells.Any(c => c.ServerId == Guid.Empty));

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
    foreach (PlaceColumnViewModel column in _allColumns.Where(MatchesPlaceFilter))
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

  partial void OnServerFilterChanged(string value) => Refresh();

  partial void OnPlaceFilterChanged(string value) => Refresh();

  private static ServerSelectionViewModel CellFor(PlaceColumnViewModel column, McpServerViewModel server) =>
    column.Place.ServerSelections.FirstOrDefault(s => s.ServerId == server.Id)
    ?? new ServerSelectionViewModel { ServerId = Guid.Empty, ServerName = server.ListName };

  private bool MatchesPlaceFilter(PlaceColumnViewModel column)
  {
    string filter = PlaceFilter.Trim();
    return filter.Length == 0
      || column.Header.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
      || column.Place.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
      || column.Place.Path.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
  }

  private bool MatchesServerFilter(McpServerViewModel server)
  {
    string filter = ServerFilter.Trim();
    return filter.Length == 0
      || server.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
      || server.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
  }
}
