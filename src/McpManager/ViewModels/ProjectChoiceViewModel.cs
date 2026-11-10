using System.Collections.Generic;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public class ProjectChoiceViewModel : ViewModelBase
{
  public const string FoundHeader = "Found on disk";

  public const string OtherFolderHeader = "Other folder…";

  private readonly string _header;

  private ProjectChoiceViewModel(
    ProjectChoiceKind kind, string header, PlaceColumnViewModel? column = null, ScannedFolder? folder = null)
  {
    Kind = kind;
    _header = header;
    Column = column;
    Folder = folder;
  }

  public static ProjectChoiceViewModel ForProject(PlaceColumnViewModel column) =>
    new(ProjectChoiceKind.Project, string.Empty, column);

  public static ProjectChoiceViewModel ForFolder(ScannedFolder folder) =>
    new(ProjectChoiceKind.Found, FolderName(folder.Path), folder: folder);

  public static ProjectChoiceViewModel FoundOnDisk() => new(ProjectChoiceKind.Header, FoundHeader);

  public static ProjectChoiceViewModel OtherFolder() => new(ProjectChoiceKind.OtherFolder, OtherFolderHeader);

  public static string FolderName(string path)
  {
    string name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
    return string.IsNullOrEmpty(name) ? path : name;
  }

  public ProjectChoiceKind Kind { get; }

  public PlaceColumnViewModel? Column { get; }

  public ScannedFolder? Folder { get; }

  public string Header => Column?.Header ?? _header;

  public string Path => Column?.Place.Path ?? Folder?.Path ?? string.Empty;

  public IReadOnlyList<string> Chips => Column?.Chips
    ?? (Folder == null ? [] : PlaceColumnViewModel.ChipsFor(Folder.Clients));

  public bool IsSelectable => Kind != ProjectChoiceKind.Header;

  public void NotifyColumnChanged()
  {
    OnPropertyChanged(nameof(Header));
    OnPropertyChanged(nameof(Path));
    OnPropertyChanged(nameof(Chips));
  }
}
