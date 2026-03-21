namespace McpManager.Core.Services;

public interface IShellEnvironmentService
{
  string? ResolvedPath { get; }

  Task ResolveAsync();
}
