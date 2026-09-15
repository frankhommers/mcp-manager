namespace McpManager.Core.Models;

public record ExistingTargetServers(
  HashSet<Guid> EnabledServerIds,
  int ConfigFileCount,
  int UnmanagedServerCount,
  List<string> UnreadableFiles)
{
  public IReadOnlyList<ExistingTargetServer> Servers { get; init; } = [];
}
