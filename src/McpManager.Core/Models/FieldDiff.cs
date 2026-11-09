namespace McpManager.Core.Models;

public sealed record FieldDiff(
  string Field,
  DiffKind Kind,
  bool IsSecret,
  IReadOnlyList<DiffToken> Left,
  IReadOnlyList<DiffToken> Right);
