namespace McpManager.Core.Models;

public sealed record ConfigFileDiff(string FilePath, TargetClientFlags Client, IReadOnlyList<ServerEntryDiff> Servers);
