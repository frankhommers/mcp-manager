namespace McpManager.Core.Models;

public sealed record ServerDiff(string ServerName, IReadOnlyList<FieldDiff> Fields)
{
  public bool HasDifferences => Fields.Any(f => f.Kind != DiffKind.Same);
}
