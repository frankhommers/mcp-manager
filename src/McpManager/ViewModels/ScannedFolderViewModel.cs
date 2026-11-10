using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public partial class ScannedFolderViewModel : ViewModelBase
{
  private const string SummarySeparator = " · ";

  [ObservableProperty] private bool _isSelected;

  public ScannedFolderViewModel(
    ScannedFolder folder,
    TargetFolder? existing,
    IReadOnlyList<ScanServerMatch> matches,
    IReadOnlyList<string> skipped,
    IEnumerable<string> scanRoots,
    string? error = null)
  {
    Folder = folder;
    IsNew = existing == null;
    CanSelect = error == null;
    if (existing == null)
    {
      bool looksLikeTest = !folder.IsGlobal && ProjectFolderRules.LooksLikeTest(folder.Path, scanRoots);
      Note = looksLikeTest ? "Looks like test data" : string.Empty;
      _isSelected = !looksLikeTest;
    }
    else
    {
      bool gainedHarness = (folder.Clients & ~existing.EnabledClients) != TargetClientFlags.None;
      Note = gainedHarness ? "New harness found" : "Already added";
      _isSelected = gainedHarness;
    }

    if (error != null)
    {
      _isSelected = false;
    }

    Summary = error == null ? BuildSummary(folder, matches, skipped) : $"Could not compare: {error}";
  }

  public ScannedFolder Folder { get; }

  public string Path => Folder.Path;

  public string DisplayPath => Folder.IsGlobal ? $"Global · {Folder.Path}" : Folder.Path;

  public IReadOnlyList<string> ClientChips => PlaceColumnViewModel.ChipsFor(Folder.Clients);

  public bool IsNew { get; }

  public bool CanSelect { get; }

  public string Note { get; }

  public bool HasNote => Note.Length > 0;

  public string Summary { get; }

  private static string BuildSummary(
    ScannedFolder folder, IReadOnlyList<ScanServerMatch> matches, IReadOnlyList<string> skipped)
  {
    List<string> parts = [];
    List<ScanMatchKind> kinds = matches
      .GroupBy(m => m.Found.Name, StringComparer.Ordinal)
      .Select(g => g.Any(m => m.Kind == ScanMatchKind.Differs) ? ScanMatchKind.Differs
        : g.Any(m => m.Kind == ScanMatchKind.Unknown) ? ScanMatchKind.Unknown
        : ScanMatchKind.Known)
      .ToList();
    AddCount(parts, kinds.Count(k => k == ScanMatchKind.Known), "recognized");
    AddCount(parts, kinds.Count(k => k == ScanMatchKind.Differs), "differs");
    AddCount(parts, kinds.Count(k => k == ScanMatchKind.Unknown), "unknown");
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
