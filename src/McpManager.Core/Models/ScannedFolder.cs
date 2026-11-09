namespace McpManager.Core.Models;

public sealed record ScannedFolder(
  string Path,
  bool IsGlobal,
  TargetClientFlags Clients,
  IReadOnlyList<ExistingTargetServer> Servers,
  IReadOnlyList<string> UnreadableFiles,
  bool HarnessesDiffer);
