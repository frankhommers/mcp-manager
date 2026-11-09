using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace McpManager.ViewModels;

public sealed class DiffChoiceOptionViewModel : ViewModelBase
{
  private readonly DiffViewModel _owner;

  public DiffChoiceOptionViewModel(DiffViewModel owner, DiffChoice choice)
  {
    _owner = owner;
    Choice = choice;
  }

  public DiffChoice Choice { get; }

  public string Label => Choice.ToString();

  public bool IsSelected
  {
    get => _owner.SelectedChoice == Choice;
    set
    {
      if (value)
      {
        _owner.SelectedChoice = Choice;
      }
    }
  }

  public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}
