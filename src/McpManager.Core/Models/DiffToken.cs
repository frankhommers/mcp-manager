namespace McpManager.Core.Models;

public sealed record DiffToken(string Text, DiffKind Kind, bool IsSecret);
