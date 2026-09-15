using McpManager.Core.Models;

namespace McpManager.Core.Services;

public interface IConfigExportService
{
  Dictionary<TargetClientFlags, string> GetConfigFilePaths(TargetFolder target, GlobalSettings? settings = null);

  /// <summary>
  /// Export configs for a target folder to disk.
  /// </summary>
  Task ExportAsync(TargetFolder target, IEnumerable<McpServer> allServers, GlobalSettings? settings = null);

  /// <summary>
  /// Resolve conflicts and prepare every target before writing. Returns null when cancelled.
  /// </summary>
  Task<Dictionary<string, string>?> PrepareExportAsync(
    IEnumerable<TargetFolder> targets,
    IEnumerable<McpServer> allServers,
    GlobalSettings? settings,
    Func<ExportConflict, Task<ExportConflictResolution?>> resolveConflictAsync);

  Task WriteConfigsAsync(IReadOnlyDictionary<string, string> configs);

  /// <summary>
  /// Get the effective list of servers for a target folder (considering inheritance).
  /// </summary>
  IEnumerable<McpServer> GetEffectiveServers(
    TargetFolder target,
    IEnumerable<McpServer> allServers,
    IEnumerable<TargetFolder> allTargets);

  /// <summary>
  /// Preview what would be generated without writing files.
  /// </summary>
  Dictionary<string, string> PreviewConfigs(
    TargetFolder target,
    IEnumerable<McpServer> servers,
    GlobalSettings? settings = null,
    IReadOnlyDictionary<ExportConflict, ExportConflictResolution>? resolutions = null);
}
