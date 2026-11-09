namespace McpManager.Core.Models;

public sealed record DeployResult(bool Cancelled, IReadOnlyList<string> WrittenFiles);
