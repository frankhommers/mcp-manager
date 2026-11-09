using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public partial class ScannedFolderViewModel : ViewModelBase
{
  private const string SummarySeparator = " · ";

  [ObservableProperty] private bool _isSelected;

  public ScannedFolderViewModel(
    ScannedFolder folder,
    TargetFolder? existing,
    IReadOnlyList<ScanServerMatch> matches,
    IReadOnlyList<string> skipped)
  {
    Folder = folder;
    IsNew = existing == null;
    if (existing == null)
    {
      bool looksLikeTest = folder.Path.Contains("test", StringComparison.OrdinalIgnoreCase);
      Note = looksLikeTest ? "Looks like test data" : string.Empty;
      _isSelected = !looksLikeTest;
    }
    else
    {
      bool gainedHarness = (folder.Clients & ~existing.EnabledClients) != TargetClientFlags.None;
      Note = gainedHarness ? "New harness found" : "Already added";
      _isSelected = gainedHarness;
    }

    Summary = BuildSummary(folder, matches, skipped);
  }

  public ScannedFolder Folder { get; }

  public string Path => Folder.Path;

  public string DisplayPath => Folder.IsGlobal ? $"Global · {Folder.Path}" : Folder.Path;

  public IReadOnlyList<string> ClientChips => PlaceColumnViewModel.ChipsFor(Folder.Clients);

  public bool IsNew { get; }

  public string Note { get; }

  public bool HasNote => Note.Length > 0;

  public string Summary { get; }

  private static string BuildSummary(
    ScannedFolder folder, IReadOnlyList<ScanServerMatch> matches, IReadOnlyList<string> skipped)
  {
    List<string> parts = [];
    AddCount(parts, matches.Count(m => m.Kind == ScanMatchKind.Known), "recognized");
    AddCount(parts, matches.Count(m => m.Kind == ScanMatchKind.Differs), "differs");
    AddCount(parts, matches.Count(m => m.Kind == ScanMatchKind.Unknown), "unknown");
    if (parts.Count == 0 && skipped.Count == 0)
    {
      parts.Add("No servers");
    }

    if (folder.HarnessesDiffer)
    {
      parts.Add("Harnesses differ");
    }

    parts.AddRange(folder.UnreadableFiles.Select(file => $"Unreadable: {RelativeFile(folder, file)}"));
    parts.AddRange(skipped.Select(entry => $"Skipped {entry}"));
    return string.Join(SummarySeparator, parts);
  }

  private static void AddCount(List<string> parts, int count, string label)
  {
    if (count > 0)
    {
      parts.Add($"{count} {label}");
    }
  }

  internal static string RelativeFile(ScannedFolder folder, string file) =>
    folder.IsGlobal ? System.IO.Path.GetFileName(file) : System.IO.Path.GetRelativePath(folder.Path, file);
}
