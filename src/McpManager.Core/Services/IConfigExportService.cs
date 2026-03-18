using McpManager.Core.Models;

namespace McpManager.Core.Services;

public interface IConfigExportService
{
  /// <summary>
  /// Export configs for a target folder to disk.
  /// </summary>
  Task ExportAsync(TargetFolder target, IEnumerable<McpServer> allServers, GlobalSettings? settings = null);

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
    GlobalSettings? settings = null);
}