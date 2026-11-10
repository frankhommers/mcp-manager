using McpManager.Core.Models;

namespace McpManager.Core.Services;

public class DiskSnapshotService(ConfigScanService scanService, ScanImportPlanner planner)
{
  public async Task<DiskSnapshot> LoadAsync(McpRegistry registry, CancellationToken cancellationToken = default)
  {
    List<ScannedFolder> folders = [.. await scanService
      .ScanAsync(RegistryService.GetDefaultScanRoots(registry.Settings), registry.Settings, cancellationToken)
      .ConfigureAwait(false)];
    foreach (TargetFolder place in registry.TargetFolders)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (place.IsGlobal || place.IsClipboard || place.IsQuickExport || !Directory.Exists(place.Path)
          || folders.Any(f => !f.IsGlobal && ProjectFolderRules.PathsEqual(f.Path, place.Path)))
      {
        continue;
      }

      folders.Add(await scanService.ReadPlaceAsync(place, registry.Settings).ConfigureAwait(false));
    }

    return MatchAll(folders, registry);
  }

  public DiskSnapshot Rematch(DiskSnapshot snapshot, McpRegistry registry) =>
    MatchAll(snapshot.Folders, registry);

  private DiskSnapshot MatchAll(IReadOnlyList<ScannedFolder> folders, McpRegistry registry)
  {
    List<ScanServerMatch> matches = [];
    List<string> problems = [];
    foreach (ScannedFolder folder in folders)
    {
      try
      {
        matches.AddRange(planner.Match([folder], registry));
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        problems.Add($"{folder.Path}: {ex.Message}");
      }
    }

    return new DiskSnapshot(folders, matches, problems);
  }
}
