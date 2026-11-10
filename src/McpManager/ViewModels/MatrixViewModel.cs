using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public partial class MatrixViewModel : ViewModelBase, IDisposable
{
  private readonly List<McpServerViewModel> _servers;
  private readonly List<PlaceColumnViewModel> _allColumns;
  private readonly DiskSnapshot _disk;
  private readonly Dictionary<PlaceColumnViewModel, ProjectChoiceViewModel> _projectChoices = [];
  private readonly List<ProjectChoiceViewModel> _foundChoices;
  private readonly ProjectChoiceViewModel _foundHeader = ProjectChoiceViewModel.FoundOnDisk();
  private readonly ProjectChoiceViewModel _otherFolder = ProjectChoiceViewModel.OtherFolder();
  private readonly Dictionary<ScannedFolder, PlaceColumnViewModel> _candidates = [];
  private readonly HashSet<ScannedFolder> _editedCandidates = [];
  private bool _syncingChoices;

  [ObservableProperty] private string _serverFilter = string.Empty;

  [ObservableProperty] private ProjectChoiceViewModel? _selectedChoice;

  public MatrixViewModel(
    IEnumerable<McpServerViewModel> servers, IEnumerable<TargetFolderViewModel> places, Guid? selectedProjectId = null,
    DiskSnapshot? disk = null, IReadOnlyList<string>? scanRoots = null, string? selectedCandidatePath = null,
    IReadOnlyCollection<string>? pinnedPaths = null)
  {
    _servers = servers.ToList();
    _disk = disk ?? DiskSnapshot.Empty;
    _allColumns = places.Where(p => p.IsManagedTarget).Select(p => new PlaceColumnViewModel(p)).ToList();
    _allColumns.Sort(CompareColumns);
    List<string> pinned = pinnedPaths == null ? [] : [.. pinnedPaths];
    if (selectedCandidatePath != null)
    {
      pinned.Add(selectedCandidatePath);
    }

    _foundChoices = FoundChoices(scanRoots ?? [], pinned);
    foreach (McpServerViewModel server in _servers)
    {
      server.PropertyChanged += OnServerChanged;
    }

    foreach (PlaceColumnViewModel column in _allColumns)
    {
      column.Place.PropertyChanged += OnPlaceChanged;
      column.Place.ServerSelections.CollectionChanged += OnSelectionsChanged;
    }

    _syncingChoices = true;
    SyncChoices();
    SelectedChoice = _projectChoices.Values.FirstOrDefault(c => c.Column!.Place.Id == selectedProjectId)
      ?? _foundChoices.FirstOrDefault(c => selectedCandidatePath != null
        && ProjectFolderRules.PathsEqual(c.Path, selectedCandidatePath))
      ?? FirstProject();
    _syncingChoices = false;
    Refresh();
  }

  public event EventHandler<Guid?>? SelectedProjectChanged;

  public event EventHandler? OtherFolderRequested;

  public event EventHandler<CandidateEditedEventArgs>? CandidateEdited;

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

    foreach (PlaceColumnViewModel candidate in _candidates.Values)
    {
      foreach (ServerSelectionViewModel selection in candidate.Place.ServerSelections)
      {
        selection.PropertyChanged -= OnCandidateSelectionChanged;
      }
    }
  }

  public ObservableCollection<ProjectChoiceViewModel> Choices { get; } = [];

  public ObservableCollection<PlaceColumnViewModel> Columns { get; } = [];

  public ObservableCollection<MatrixRowViewModel> Rows { get; } = [];

  public ObservableCollection<UnknownServerRowViewModel> UnknownRows { get; } = [];

  public bool HasUnknownRows => UnknownRows.Count > 0;

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
    PlaceColumnViewModel? chosen = SelectedChoice?.Kind switch
    {
      ProjectChoiceKind.Project => SelectedChoice.Column,
      ProjectChoiceKind.Found => CandidateFor(SelectedChoice.Folder!),
      _ => null,
    };
    Columns.Clear();
    foreach (PlaceColumnViewModel column in _allColumns.Where(c => c.IsGlobal))
    {
      Columns.Add(column);
    }

    if (chosen != null)
    {
      Columns.Add(chosen);
    }

    List<IReadOnlyList<ScanServerMatch>> matches = Columns
      .Select(c => c.IsCandidate ? _disk.MatchesIn(CandidateFolder(c)) : _disk.MatchesFor(c.Place.Model))
      .ToList();
    Rows.Clear();
    foreach (McpServerViewModel server in _servers
      .Where(s => MatchesServerFilter(s.Name) || MatchesServerFilter(s.DisplayName))
      .OrderBy(s => s.ListName, StringComparer.CurrentCultureIgnoreCase))
    {
      Rows.Add(new MatrixRowViewModel(
        server, Columns.Select((c, i) => new MatrixCellViewModel(CellFor(c, server), DifferenceFor(matches[i], server)))
          .ToList()));
    }

    RefreshUnknownRows(matches);
  }

  partial void OnSelectedChoiceChanged(ProjectChoiceViewModel? oldValue, ProjectChoiceViewModel? newValue)
  {
    if (_syncingChoices)
    {
      return;
    }

    if (newValue is not { Kind: ProjectChoiceKind.Project or ProjectChoiceKind.Found })
    {
      _syncingChoices = true;
      SelectedChoice = oldValue;
      _syncingChoices = false;
      if (newValue?.Kind == ProjectChoiceKind.OtherFolder)
      {
        OtherFolderRequested?.Invoke(this, EventArgs.Empty);
      }

      return;
    }

    Refresh();
    SelectedProjectChanged?.Invoke(this, newValue.Column?.Place.Id);
  }

  private List<ProjectChoiceViewModel> FoundChoices(IReadOnlyList<string> scanRoots, IReadOnlyList<string> pinned)
  {
    HashSet<ScannedFolder> placed = _allColumns.Select(c => _disk.FolderFor(c.Place.Model)).OfType<ScannedFolder>()
      .ToHashSet();
    return _disk.Folders
      .Where(f => !f.IsGlobal && f.Clients != TargetClientFlags.None && !placed.Contains(f)
        && (!ProjectFolderRules.LooksLikeTest(f.Path, scanRoots)
          || pinned.Any(p => ProjectFolderRules.PathsEqual(p, f.Path))))
      .Select(ProjectChoiceViewModel.ForFolder)
      .OrderBy(c => c.Header, StringComparer.CurrentCultureIgnoreCase)
      .ToList();
  }

  private ProjectChoiceViewModel? FirstProject() => Choices.FirstOrDefault(c => c.Kind == ProjectChoiceKind.Project);

  private void SyncChoices()
  {
    List<ProjectChoiceViewModel> choices = _allColumns.Where(c => !c.IsGlobal)
      .Select(c => _projectChoices.TryGetValue(c, out ProjectChoiceViewModel? choice)
        ? choice : _projectChoices[c] = ProjectChoiceViewModel.ForProject(c))
      .ToList();
    if (_foundChoices.Count > 0)
    {
      choices.Add(_foundHeader);
      choices.AddRange(_foundChoices);
    }

    choices.Add(_otherFolder);
    if (choices.SequenceEqual(Choices))
    {
      return;
    }

    ProjectChoiceViewModel? selected = SelectedChoice;
    bool wasSyncing = _syncingChoices;
    _syncingChoices = true;
    Choices.Clear();
    foreach (ProjectChoiceViewModel choice in choices)
    {
      Choices.Add(choice);
    }

    SelectedChoice = selected != null && Choices.Contains(selected) ? selected : FirstProject();
    _syncingChoices = wasSyncing;
  }

  private PlaceColumnViewModel CandidateFor(ScannedFolder folder)
  {
    if (_candidates.TryGetValue(folder, out PlaceColumnViewModel? existing))
    {
      return existing;
    }

    HashSet<Guid> libraryIds = _servers.Select(s => s.Id).ToHashSet();
    TargetFolder model = new()
    {
      Name = ProjectChoiceViewModel.FolderName(folder.Path),
      Path = folder.Path,
      EnabledClients = folder.Clients,
      EnabledServers = _disk.MatchesIn(folder)
        .Where(m => m.Kind == ScanMatchKind.Known && m.Found.IsEnabled && m.Library != null
          && libraryIds.Contains(m.Library.Id))
        .Select(m => m.Library!.Id)
        .ToHashSet(),
    };
    PlaceColumnViewModel candidate = new(
      new TargetFolderViewModel(model, _servers.Select(s => s.Model).ToList()), true);
    foreach (ServerSelectionViewModel selection in candidate.Place.ServerSelections)
    {
      selection.PropertyChanged += OnCandidateSelectionChanged;
    }

    _candidates[folder] = candidate;
    return candidate;
  }

  private void OnCandidateSelectionChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName != nameof(ServerSelectionViewModel.IsEnabled)
      || sender is not ServerSelectionViewModel selection)
    {
      return;
    }

    (ScannedFolder folder, PlaceColumnViewModel candidate) = _candidates
      .First(pair => pair.Value.Place.ServerSelections.Contains(selection));
    if (_editedCandidates.Add(folder))
    {
      CandidateEdited?.Invoke(this, new CandidateEditedEventArgs(folder, candidate.Place.ServerSelections.ToList()));
    }
  }

  private ScannedFolder CandidateFolder(PlaceColumnViewModel candidate) =>
    _candidates.First(pair => pair.Value == candidate).Key;

  private static ScanServerMatch? DifferenceFor(IReadOnlyList<ScanServerMatch> matches, McpServerViewModel server) =>
    matches.FirstOrDefault(m => m.Kind == ScanMatchKind.Differs && m.Library?.Id == server.Id);

  private void RefreshUnknownRows(IReadOnlyList<IReadOnlyList<ScanServerMatch>> matches)
  {
    HashSet<Guid> libraryIds = _servers.Select(s => s.Id).ToHashSet();
    List<HashSet<string>> unknownByColumn = matches
      .Select(list => list.Where(m => m.Library == null || !libraryIds.Contains(m.Library.Id))
        .Select(m => m.Found.Name).ToHashSet(StringComparer.Ordinal))
      .ToList();
    UnknownRows.Clear();
    foreach (string name in unknownByColumn.SelectMany(names => names).Distinct(StringComparer.Ordinal)
      .Where(MatchesServerFilter)
      .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
    {
      UnknownRows.Add(
        new UnknownServerRowViewModel(name, unknownByColumn.Select(names => names.Contains(name)).ToList()));
    }

    OnPropertyChanged(nameof(HasUnknownRows));
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

      foreach (ProjectChoiceViewModel choice in _projectChoices.Values)
      {
        choice.NotifyColumnChanged();
      }

      _allColumns.Sort(CompareColumns);
      SyncChoices();
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

  private bool MatchesServerFilter(string name)
  {
    string filter = ServerFilter.Trim();
    return filter.Length == 0 || name.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
  }
}
