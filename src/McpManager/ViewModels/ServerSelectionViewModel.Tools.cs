using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace McpManager.ViewModels;

public partial class ServerSelectionViewModel
{
  private List<ToolOverrideViewModel> _observedTools = [];

  [ObservableProperty] private bool _areToolsExpanded;
  [ObservableProperty] private string _toolSearchText = string.Empty;
  [ObservableProperty] private bool _isFetchingTools;
  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(HasToolFetchStatus))]
  private string _toolFetchStatus = string.Empty;

  public ObservableCollection<ToolOverrideViewModel> VisibleTools { get; } = [];
  public bool HasVisibleTools => VisibleTools.Count > 0;
  public bool HasToolSearch => !string.IsNullOrWhiteSpace(ToolSearchText);
  public bool HasNoToolMatches => HasToolOverrides && !HasVisibleTools;
  public bool HasToolFetchStatus => !string.IsNullOrWhiteSpace(ToolFetchStatus);
  public bool CanSelectTools => IsEnabled && HasVisibleTools;
  public string ToolSelectionSummary => HasToolOverrides
    ? $"{ToolOverrides.Count(tool => tool.IsAllowed)} of {ToolOverrides.Count} tools selected"
    : "No tools discovered";

  public ServerSelectionViewModel()
  {
    ToolOverrides.CollectionChanged += OnToolCollectionChanged;
  }

  partial void OnToolOverridesChanged(
    ObservableCollection<ToolOverrideViewModel>? oldValue, ObservableCollection<ToolOverrideViewModel> newValue)
  {
    if (oldValue != null)
    {
      oldValue.CollectionChanged -= OnToolCollectionChanged;
    }

    newValue.CollectionChanged += OnToolCollectionChanged;
    RefreshTools();
  }

  partial void OnToolSearchTextChanged(string value) => RefreshVisibleTools();

  partial void OnIsEnabledChanged(bool value) => NotifyToolCommandsChanged();

  private void OnToolCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshTools();

  private void RefreshTools()
  {
    foreach (ToolOverrideViewModel tool in _observedTools)
    {
      tool.PropertyChanged -= OnToolChanged;
    }

    _observedTools = ToolOverrides.ToList();
    foreach (ToolOverrideViewModel tool in _observedTools)
    {
      tool.PropertyChanged += OnToolChanged;
    }

    OnPropertyChanged(nameof(HasToolOverrides));
    OnPropertyChanged(nameof(ToolSelectionSummary));
    RefreshVisibleTools();
  }

  private void OnToolChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(ToolOverrideViewModel.IsAllowed))
    {
      OnPropertyChanged(nameof(ToolSelectionSummary));
    }
    else if (e.PropertyName == nameof(ToolOverrideViewModel.ToolName))
    {
      RefreshVisibleTools();
    }
  }

  private void RefreshVisibleTools()
  {
    string query = ToolSearchText.Trim();
    CollectionViewUpdater.Update(VisibleTools, ToolOverrides
      .Where(tool => tool.ToolName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
      .OrderBy(tool => tool.ToolName, StringComparer.CurrentCultureIgnoreCase));
    OnPropertyChanged(nameof(HasVisibleTools));
    OnPropertyChanged(nameof(HasToolSearch));
    OnPropertyChanged(nameof(HasNoToolMatches));
    NotifyToolCommandsChanged();
  }

  private void NotifyToolCommandsChanged()
  {
    OnPropertyChanged(nameof(CanSelectTools));
    SelectVisibleToolsCommand.NotifyCanExecuteChanged();
    ClearVisibleToolsCommand.NotifyCanExecuteChanged();
  }

  [RelayCommand]
  private void ClearToolSearch() => ToolSearchText = string.Empty;

  [RelayCommand(CanExecute = nameof(CanSelectTools))]
  private void SelectVisibleTools() => SetVisibleToolsAllowed(true);

  [RelayCommand(CanExecute = nameof(CanSelectTools))]
  private void ClearVisibleTools() => SetVisibleToolsAllowed(false);

  private void SetVisibleToolsAllowed(bool allowed)
  {
    if (!IsEnabled)
    {
      return;
    }

    foreach (ToolOverrideViewModel tool in VisibleTools)
    {
      tool.IsAllowed = allowed;
    }
  }
}
