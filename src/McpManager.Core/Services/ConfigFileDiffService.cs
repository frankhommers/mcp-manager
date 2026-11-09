using McpManager.Core.Models;

namespace McpManager.Core.Services;

public class ConfigFileDiffService(IConfigImportService importService)
{
  public ConfigFileDiff Compare(string filePath, TargetClientFlags client, string? leftContent, string rightContent)
  {
    Dictionary<string, McpServer> left = leftContent == null ? [] : ParseServers(leftContent, client, filePath);
    Dictionary<string, McpServer> right = ParseServers(rightContent, client, filePath);
    List<ServerEntryDiff> entries = left.Keys.Union(right.Keys, StringComparer.Ordinal)
      .Order(StringComparer.Ordinal)
      .Select(name =>
      {
        bool hasLeft = left.TryGetValue(name, out McpServer? leftServer);
        bool hasRight = right.TryGetValue(name, out McpServer? rightServer);
        ServerDiff details = ServerDiffService.Compare(
          leftServer ?? new McpServer { Name = name },
          rightServer ?? new McpServer { Name = name });
        DiffKind kind = (hasLeft, hasRight) switch
        {
          (false, _) => DiffKind.Added,
          (_, false) => DiffKind.Removed,
          _ => details.HasDifferences ? DiffKind.Changed : DiffKind.Same,
        };
        return new ServerEntryDiff(name, kind, details);
      })
      .ToList();
    return new ConfigFileDiff(filePath, client, entries);
  }

  public ServerDiff CompareConflict(ExportConflict conflict) => ServerDiffService.Compare(
    ImportConfiguration(conflict.ServerName, conflict.ExistingConfiguration, conflict.Client, conflict.FilePath),
    ImportConfiguration(conflict.ServerName, conflict.ProposedConfiguration, conflict.Client, conflict.FilePath));

  private McpServer ImportConfiguration(string name, string configuration, TargetClientFlags client, string path)
  {
    McpServer server = importService.ImportServer(new ExistingTargetServer(name, null, true, client, path, configuration));
    ManagedServerIdentity.RestoreImportedId(server);
    server.Name = name;
    return server;
  }

  private Dictionary<string, McpServer> ParseServers(string content, TargetClientFlags client, string path) =>
    TargetConfigSelectionService.Parse(content, client, path).ToDictionary(
      entry => entry.Name,
      entry => ImportConfiguration(entry.Name, entry.Configuration, client, path),
      StringComparer.Ordinal);
}
