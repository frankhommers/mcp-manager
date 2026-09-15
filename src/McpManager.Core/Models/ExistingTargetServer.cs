namespace McpManager.Core.Models;

public sealed record ExistingTargetServer(
  string Name,
  Guid? ManagedId,
  bool IsEnabled,
  TargetClientFlags Client,
  string FilePath,
  string Configuration);
