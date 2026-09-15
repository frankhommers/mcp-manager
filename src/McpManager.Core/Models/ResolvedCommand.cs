namespace McpManager.Core.Models;

public record ResolvedCommand(
  string Command,
  IReadOnlyList<string> Arguments,
  IReadOnlySet<int> SensitiveArgumentIndexes);
