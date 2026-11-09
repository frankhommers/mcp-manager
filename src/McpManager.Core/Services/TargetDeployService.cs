using McpManager.Core.Models;

namespace McpManager.Core.Services;

public class TargetDeployService(IConfigExportService exportService)
{
  private static string ClaudePermissionsPath => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

  public async Task<DeployResult> DeployAsync(
    IReadOnlyList<TargetFolder> targets,
    McpRegistry registry,
    Func<ExportConflict, Task<ExportConflictResolution?>> resolveConflictAsync,
    bool overwriteExternal = false)
  {
    if (!overwriteExternal && targets.Any(TargetSyncService.HasExternalChanges))
    {
      throw new InvalidOperationException(
        "External changes detected. Inspect the target, then explicitly reapply central settings.");
    }

    TargetConfigSelectionService selectionService = new(exportService);
    foreach (TargetFolder target in targets.Where(t => t.LastAppliedAt == null))
    {
      ExistingTargetServers existing = await selectionService.ReadAsync(target, registry.Settings).ConfigureAwait(false);
      if (existing.Servers.Any(s => s.ManagedId is Guid id && registry.Servers.All(server => server.Id != id)))
      {
        throw new InvalidOperationException(
          $"Import the existing managed servers from '{target.Name}' before its first update.");
      }

      HashSet<Guid> inherited = ProfileResolver.Resolve(target, registry.Profiles).EnabledServers;
      foreach (Guid id in existing.Servers.Select(s => s.ManagedId).OfType<Guid>().ToHashSet())
      {
        if (registry.Servers.Any(s => s.Id == id) && !inherited.Contains(id) && !target.DisabledServers.Contains(id))
        {
          target.EnabledServers.Add(id);
        }
      }
    }

    List<TargetFolder> effective = targets.Select(t => ProfileResolver.Resolve(t, registry.Profiles)).ToList();
    foreach (TargetFolder target in effective)
    {
      if (target.EnabledClients == TargetClientFlags.None || !Path.IsPathFullyQualified(target.Path))
      {
        throw new InvalidOperationException($"'{target.Name}' needs a client and an absolute destination path.");
      }
    }

    Dictionary<string, string> before = [];
    foreach (TargetFolder target in effective)
    {
      foreach (string path in exportService.GetConfigFilePaths(target, registry.Settings).Values)
      {
        before[Path.GetFullPath(path)] = TargetSyncService.FileHash(path);
      }
    }

    if (effective.Any(t => t.EnabledClients == TargetClientFlags.ClaudeCodeGlobal))
    {
      before[ClaudePermissionsPath] = TargetSyncService.FileHash(ClaudePermissionsPath);
    }

    Dictionary<string, string>? configs = await exportService.PrepareExportAsync(
      effective, registry.Servers, registry.Settings, resolveConflictAsync).ConfigureAwait(false);
    if (configs == null)
    {
      return new DeployResult(true, []);
    }

    if (before.Any(p => TargetSyncService.FileHash(p.Key) != p.Value))
    {
      throw new InvalidOperationException("A configuration changed during preparation. Check targets and try again.");
    }

    await exportService.WriteConfigsAsync(configs).ConfigureAwait(false);
    foreach (TargetFolder target in targets)
    {
      HashSet<string> paths = exportService.GetConfigFilePaths(target, registry.Settings)
        .Values.Select(Path.GetFullPath).ToHashSet();
      if (target.EnabledClients == TargetClientFlags.ClaudeCodeGlobal)
      {
        paths.Add(ClaudePermissionsPath);
      }

      TargetSyncService.RecordApplied(target, configs.Where(p => paths.Contains(p.Key))
        .ToDictionary(p => p.Key, p => p.Value));
    }

    return new DeployResult(false, configs.Keys.ToList());
  }
}
