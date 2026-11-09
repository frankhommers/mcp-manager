using System.Text.Json;
using McpManager.Core.Models;
using Tomlyn;

namespace McpManager.Core.Services;

public class PlaceStatusService(IConfigExportService exportService)
{
  public PlaceStatus GetStatus(TargetFolder target, McpRegistry registry)
  {
    if (TargetSyncService.HasExternalChanges(target))
    {
      return PlaceStatus.ExternallyChanged;
    }

    bool neverDeployed = target.LastAppliedAt == null;
    try
    {
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

  public int CountChangedFiles(McpRegistry saved, McpRegistry current)
  {
    int count = 0;
    foreach (TargetFolder target in current.TargetFolders.Where(t => !t.IsClipboard && !t.IsQuickExport))
    {
      Dictionary<string, string>? after = TryPreview(target, current);
      if (after == null)
      {
        count += exportService.GetConfigFilePaths(target, current.Settings).Count;
        continue;
      }

      TargetFolder? savedTarget = saved.TargetFolders.FirstOrDefault(t => t.Id == target.Id);
      Dictionary<string, string>? before = savedTarget == null ? null : TryPreview(savedTarget, saved);
      if (before != null && !PreviewsDiffer(before, after))
      {
        continue;
      }

      count += after.Count(pair => TargetSyncService.FileHash(pair.Key) != TargetSyncService.ContentHash(pair.Value));
    }

    return count;
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
    ex is InvalidDataException or JsonException or TomlException or InvalidOperationException or IOException;
}
