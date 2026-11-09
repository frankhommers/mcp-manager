using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public partial class PlaceColumnViewModel : ViewModelBase
{
  private static readonly (TargetClientFlags Flag, string Chip)[] ChipMap =
  [
    (TargetClientFlags.ClaudeCode | TargetClientFlags.ClaudeCodeGlobal, "CC"),
    (TargetClientFlags.ClaudeDesktop, "CD"),
    (TargetClientFlags.OpenCode, "OC"),
    (TargetClientFlags.Codex, "CX"),
    (TargetClientFlags.Cursor, "CU"),
    (TargetClientFlags.VsCode, "VS"),
    (TargetClientFlags.Windsurf, "WS"),
  ];

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(StatusText))]
  [NotifyPropertyChangedFor(nameof(ShowStatusDot))]
  [NotifyPropertyChangedFor(nameof(ShowHollowDot))]
  [NotifyPropertyChangedFor(nameof(ToolTipText))]
  private PlaceStatus _status = PlaceStatus.NotDeployedYet;

  public PlaceColumnViewModel(TargetFolderViewModel place)
  {
    Place = place;
  }

  public TargetFolderViewModel Place { get; }

  public void NotifyPlaceChanged()
  {
    OnPropertyChanged(nameof(Header));
    OnPropertyChanged(nameof(Chips));
    OnPropertyChanged(nameof(IsGlobal));
    OnPropertyChanged(nameof(GroupLabel));
    OnPropertyChanged(nameof(ToolTipText));
  }

  public bool IsGlobal => Place.IsGlobal;

  public string GroupLabel => IsGlobal ? "Global" : "Projects";

  public string Header
  {
    get
    {
      if (Place.IsGlobal)
      {
        return Place.ClientName;
      }

      if (!string.IsNullOrWhiteSpace(Place.Name))
      {
        return Place.Name;
      }

      string trimmed = Place.Path.TrimEnd('/', '\\');
      string last = System.IO.Path.GetFileName(trimmed);
      return string.IsNullOrEmpty(last) ? Place.Path : last;
    }
  }

  public IReadOnlyList<string> Chips => ChipsFor(Place.SelectedClient);

  public static IReadOnlyList<string> ChipsFor(TargetClientFlags clients)
  {
    List<string> chips = [];
    foreach ((TargetClientFlags flag, string chip) in ChipMap)
    {
      if ((clients & flag) != TargetClientFlags.None)
      {
        chips.Add(chip);
      }
    }

    return chips;
  }

  public string StatusText => Status switch
  {
    PlaceStatus.NotDeployedYet => "Not deployed yet",
    PlaceStatus.UpToDate => "Up to date",
    PlaceStatus.Behind => "Not deployed",
    PlaceStatus.ExternallyChanged => "Changed outside MCP Manager",
    PlaceStatus.NeedsAttention => "Needs attention",
    _ => string.Empty,
  };

  public bool ShowStatusDot => Status is PlaceStatus.Behind or PlaceStatus.ExternallyChanged;

  public bool ShowHollowDot => Status == PlaceStatus.NotDeployedYet;

  public string ToolTipText =>
    string.Join(Environment.NewLine, new[] { Header, Place.Path, StatusText }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
