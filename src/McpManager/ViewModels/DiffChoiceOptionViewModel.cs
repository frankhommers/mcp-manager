using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace McpManager.ViewModels;

public sealed class DiffChoiceOptionViewModel : ViewModelBase
{
  private readonly DiffViewModel _owner;

  public DiffChoiceOptionViewModel(DiffViewModel owner, DiffChoice choice, string description = "")
  {
    _owner = owner;
    Choice = choice;
    Description = description;
  }

  public DiffChoice Choice { get; }

  public string Description { get; }

  public bool HasDescription => Description.Length > 0;

  public string Label => Choice switch
  {
    DiffChoice.UseLibraryVersion => "Overwrite with library version",
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
