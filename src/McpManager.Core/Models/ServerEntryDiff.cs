namespace McpManager.Core.Models;

public sealed record ServerEntryDiff(string ServerName, DiffKind Kind, ServerDiff Details);
