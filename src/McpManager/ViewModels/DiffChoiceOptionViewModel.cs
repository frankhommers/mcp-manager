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

  public string Label => Choice switch
  {
    DiffChoice.KeepLibrary => "Keep library",
    DiffChoice.UseFoundVersion => "Put this version in the library",
    DiffChoice.ImportSeparately => "Import as separate server",
    DiffChoice.Replace => "Replace with MCP Manager",
    DiffChoice.KeepExisting => "Keep existing",
    DiffChoice.Overwrite => "Overwrite with MCP Manager",
    DiffChoice.KeepFile => "Keep file",
    _ => Choice.ToString(),
  };

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
