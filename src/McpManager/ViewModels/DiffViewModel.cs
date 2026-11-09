using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public partial class DiffViewModel : ViewModelBase
{
  private readonly IReadOnlyList<ServerDiff> _items;
  private readonly DiffChoice?[] _selected;
  private int _index;

  public DiffViewModel(
    string leftLabel,
    string rightLabel,
    IReadOnlyList<ServerDiff> items,
    IReadOnlyList<DiffChoice> choices)
  {
    LeftLabel = leftLabel;
    RightLabel = rightLabel;
    _items = items;
    Options = [.. choices.Select(c => new DiffChoiceOptionViewModel(this, c))];
    _selected = new DiffChoice?[items.Count];
    Fields = BuildFields();
  }

  public string LeftLabel { get; }

  public string RightLabel { get; }

  public IReadOnlyList<DiffChoiceOptionViewModel> Options { get; }

  public bool HasChoices => Options.Count > 0;

  public bool HasMultipleItems => _items.Count > 1;

  public IReadOnlyList<FieldDiffViewModel> Fields { get; private set; }

  public ServerDiff? Current => _index < _items.Count ? _items[_index] : null;

  public bool HasTitle => !string.IsNullOrEmpty(Current?.ServerName);

  public string Position => _items.Count == 0 ? string.Empty : $"{_index + 1} of {_items.Count}";

  public bool CanGoNext => _index < _items.Count - 1;

  public bool CanGoPrevious => _index > 0;

  public IReadOnlyList<DiffChoice?> Choices => _selected;

  public bool IsComplete => !HasChoices || _selected.All(c => c.HasValue);

  public DiffChoice? SelectedChoice
  {
    get => Current == null ? null : _selected[_index];
    set
    {
      if (Current == null || _selected[_index] == value)
      {
        return;
      }

      _selected[_index] = value;
      OnPropertyChanged();
      OnPropertyChanged(nameof(IsComplete));
      OnPropertyChanged(nameof(Choices));
      RefreshOptions();
    }
  }

  [RelayCommand(CanExecute = nameof(CanGoNext))]
  private void Next() => MoveTo(_index + 1);

  [RelayCommand(CanExecute = nameof(CanGoPrevious))]
  private void Previous() => MoveTo(_index - 1);

  private void MoveTo(int index)
  {
    _index = index;
    Fields = BuildFields();
    OnPropertyChanged(nameof(Current));
    OnPropertyChanged(nameof(HasTitle));
    OnPropertyChanged(nameof(Fields));
    OnPropertyChanged(nameof(Position));
    OnPropertyChanged(nameof(SelectedChoice));
    RefreshOptions();
    OnPropertyChanged(nameof(CanGoNext));
    OnPropertyChanged(nameof(CanGoPrevious));
    NextCommand.NotifyCanExecuteChanged();
    PreviousCommand.NotifyCanExecuteChanged();
  }

  private void RefreshOptions()
  {
    foreach (DiffChoiceOptionViewModel option in Options)
    {
      option.Refresh();
    }
  }

  private List<FieldDiffViewModel> BuildFields() =>
    Current == null ? [] : [.. Current.Fields.Select(f => new FieldDiffViewModel(f))];
}
