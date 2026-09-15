using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace McpManager.ViewModels;

public partial class TargetFolderViewModel
{
  [ObservableProperty] private string _serverSearchText = string.Empty;

  public ObservableCollection<ServerSelectionViewModel> SortedServerSelections { get; } = [];
  public ObservableCollection<ServerSelectionViewModel> VisibleServerSelections { get; } = [];

  public bool HasVisibleServers => VisibleServerSelections.Count > 0;
  public bool HasServerSearch => !string.IsNullOrWhiteSpace(ServerSearchText);
  public bool HasNoServerMatches => ServerSelections.Count > 0 && !HasVisibleServers;

  public string ServerSelectionSummary =>
    $"{ServerSelections.Count(s => s.IsEnabled)} of {ServerSelections.Count} selected" +
    (HasServerSearch ? $" · {VisibleServerSelections.Count} shown" : " · A–Z");

  partial void OnServerSearchTextChanged(string value) => RefreshServerList();

  partial void OnServerSelectionsChanged(
    ObservableCollection<ServerSelectionViewModel>? oldValue, ObservableCollection<ServerSelectionViewModel> newValue)
  {
    if (oldValue != null)
    {
      oldValue.CollectionChanged -= OnSelectionCollectionChanged;
    }

    newValue.CollectionChanged += OnSelectionCollectionChanged;
    RefreshServerList();
  }

  private void OnSelectionCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshServerList();

  private void RefreshServerList()
  {
    CollectionViewUpdater.Update(SortedServerSelections, ServerSelections
      .OrderBy(s => s.ServerName, StringComparer.CurrentCultureIgnoreCase)
      .ThenBy(s => s.ServerKey, StringComparer.OrdinalIgnoreCase)
      .ThenBy(s => s.ServerId));
    string query = ServerSearchText.Trim();
    CollectionViewUpdater.Update(VisibleServerSelections, SortedServerSelections.Where(s =>
      s.ServerName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
      s.ServerKey.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
      s.Group.Contains(query, StringComparison.CurrentCultureIgnoreCase)));
    OnPropertyChanged(nameof(HasVisibleServers));
    OnPropertyChanged(nameof(HasServerSearch));
    OnPropertyChanged(nameof(HasNoServerMatches));
    OnPropertyChanged(nameof(ServerSelectionSummary));
    SelectVisibleServersCommand.NotifyCanExecuteChanged();
    DeselectVisibleServersCommand.NotifyCanExecuteChanged();
    RefreshFoundServers();
  }

  [RelayCommand]
  private void ClearServerSearch() => ServerSearchText = string.Empty;

  [RelayCommand(CanExecute = nameof(HasVisibleServers))]
  private void SelectVisibleServers() => SetVisibleServersEnabled(true);

  [RelayCommand(CanExecute = nameof(HasVisibleServers))]
  private void DeselectVisibleServers() => SetVisibleServersEnabled(false);

  private void SetVisibleServersEnabled(bool enabled)
  {
    foreach (ServerSelectionViewModel server in VisibleServerSelections.ToList())
    {
      server.IsEnabled = enabled;
    }
  }
}
