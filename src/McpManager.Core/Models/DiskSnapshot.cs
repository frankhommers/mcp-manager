using McpManager.Core.Services;

namespace McpManager.Core.Models;

public sealed record DiskSnapshot(
  IReadOnlyList<ScannedFolder> Folders,
  IReadOnlyList<ScanServerMatch> Matches,
  IReadOnlyList<string> Problems)
{
  public static DiskSnapshot Empty { get; } = new([], [], []);

  public ScannedFolder? FolderFor(TargetFolder place) =>
    Folders.FirstOrDefault(f => place.IsGlobal
      ? f.IsGlobal && f.Clients == place.EnabledClients
      : !f.IsGlobal && ProjectFolderRules.PathsEqual(f.Path, place.Path));

  public IReadOnlyList<ScanServerMatch> MatchesIn(ScannedFolder folder) =>
    Matches.Where(m => string.Equals(m.FolderPath, folder.Path, StringComparison.Ordinal)).ToList();
}
