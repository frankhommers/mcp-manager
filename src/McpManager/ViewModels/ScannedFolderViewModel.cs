using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public partial class ScannedFolderViewModel : ViewModelBase
{
  private const string SummarySeparator = " · ";

  private static readonly string[] TestWords = ["tests", "test"];

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
      bool looksLikeTest = !folder.IsGlobal && LooksLikeTest(folder.Path, scanRoots);
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

  private static bool LooksLikeTest(string path, IEnumerable<string> scanRoots)
  {
    string relative = scanRoots
      .Select(root => System.IO.Path.GetRelativePath(root, path))
      .Where(r => r != ".." && !r.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && !System.IO.Path.IsPathRooted(r))
      .MinBy(r => r.Length) ?? path;
    return relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(IsTestSegment);
  }

  private static bool IsTestSegment(string segment) => TestWords.Any(word =>
    (segment.StartsWith(word, StringComparison.OrdinalIgnoreCase) && IsBoundary(segment, word.Length))
    || (segment.EndsWith(word, StringComparison.OrdinalIgnoreCase) && IsBoundary(segment, segment.Length - word.Length)));

  private static bool IsBoundary(string segment, int index) =>
    index <= 0 || index >= segment.Length || !char.IsLetter(segment[index]) || !char.IsLetter(segment[index - 1])
    || (char.IsLower(segment[index - 1]) && char.IsUpper(segment[index]));

  internal static string RelativeFile(ScannedFolder folder, string file) =>
    folder.IsGlobal ? System.IO.Path.GetFileName(file) : System.IO.Path.GetRelativePath(folder.Path, file);
}
