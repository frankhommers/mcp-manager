namespace McpManager.Core.Models;

public sealed record ScanServerMatch(
  string FolderPath,
  ExistingTargetServer Found,
  McpServer Imported,
  McpServer? Library,
  ScanMatchKind Kind,
  ServerDiff? Diff);
