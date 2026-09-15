namespace McpManager.Core.Models;

public sealed record ExportConflict(
  string FilePath,
  string ServerName,
  string ExistingConfiguration,
  string ProposedConfiguration);
