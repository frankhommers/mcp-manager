using System.Text.Json;
using McpManager.Core.Models;
using Tomlyn;

namespace McpManager.Core.Services;

public class PlaceStatusService(IConfigExportService exportService)
{
  public PlaceStatus GetStatus(TargetFolder target, McpRegistry registry)
  {
    bool neverDeployed = target.LastAppliedAt == null;
    try
    {
      if (TargetSyncService.HasExternalChanges(target))
      {
        return PlaceStatus.ExternallyChanged;
      }

      TargetFolder effective = ProfileResolver.Resolve(target, registry.Profiles);
      Dictionary<string, string> preview = exportService.PreviewConfigs(effective, registry.Servers, registry.Settings);
      if (neverDeployed)
      {
        return PlaceStatus.NotDeployedYet;
      }

      bool behind = preview.Any(pair =>
        !target.AppliedFileHashes.TryGetValue(Path.GetFullPath(pair.Key), out string? applied)
        || applied != TargetSyncService.ContentHash(pair.Value));
      return behind ? PlaceStatus.Behind : PlaceStatus.UpToDate;
    }
    catch (ExportConflictException)
    {
      return neverDeployed ? PlaceStatus.NotDeployedYet : PlaceStatus.NeedsAttention;
    }
    catch (Exception ex) when (IsPreviewFailure(ex))
    {
      return PlaceStatus.NeedsAttention;
    }
  }

  public int CountChangedFiles(McpRegistry saved, McpRegistry current) =>
    ManagedPlaces(current).Sum(target => CountChangedFilesSafely(target, saved, current));

  public IReadOnlyList<TargetFolder> GetChangedPlaces(McpRegistry saved, McpRegistry current) =>
    ManagedPlaces(current).Where(target => CountChangedFilesSafely(target, saved, current) > 0).ToList();

  private static IEnumerable<TargetFolder> ManagedPlaces(McpRegistry registry) =>
    registry.TargetFolders.Where(t => !t.IsClipboard && !t.IsQuickExport);

  private int CountChangedFilesSafely(TargetFolder target, McpRegistry saved, McpRegistry current)
  {
    try
    {
      return CountChangedFiles(target, saved, current);
    }
    catch (Exception ex) when (IsPreviewFailure(ex))
    {
      return CountAllFiles(target, current);
    }
  }

  private int CountChangedFiles(TargetFolder target, McpRegistry saved, McpRegistry current)
  {
    Dictionary<string, string>? after = TryPreview(target, current);
    if (after == null)
    {
      return CountAllFiles(target, current);
    }

    TargetFolder? savedTarget = saved.TargetFolders.FirstOrDefault(t => t.Id == target.Id);
    Dictionary<string, string>? before = savedTarget == null ? null : TryPreview(savedTarget, saved);
    if (before != null && !PreviewsDiffer(before, after))
    {
      return 0;
    }

    return after.Count(pair => TargetSyncService.FileHash(pair.Key) != TargetSyncService.ContentHash(pair.Value));
  }

  private int CountAllFiles(TargetFolder target, McpRegistry registry)
  {
    try
    {
      return exportService.GetConfigFilePaths(target, registry.Settings).Count;
    }
    catch (Exception ex) when (IsPreviewFailure(ex))
    {
      return 1;
    }
  }

  private Dictionary<string, string>? TryPreview(TargetFolder target, McpRegistry registry)
  {
    try
    {
      TargetFolder effective = ProfileResolver.Resolve(target, registry.Profiles);
      return exportService.PreviewConfigs(effective, registry.Servers, registry.Settings);
    }
    catch (Exception ex) when (ex is ExportConflictException || IsPreviewFailure(ex))
    {
      return null;
    }
  }

  private static bool PreviewsDiffer(Dictionary<string, string> before, Dictionary<string, string> after) =>
    before.Count != after.Count
    || after.Any(pair => !before.TryGetValue(pair.Key, out string? content)
      || !string.Equals(content, pair.Value, StringComparison.Ordinal));

  private static bool IsPreviewFailure(Exception ex) =>
    ex is InvalidDataException or JsonException or TomlException or InvalidOperationException or IOException
      or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
