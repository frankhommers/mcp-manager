namespace McpManager.Core.Models;

public record ParsedCommand(
  string Command,
  IReadOnlyList<string> Args,
  IReadOnlyDictionary<string, string> EnvironmentVariables,
  McpTransportType SuggestedTransport,
  string? SuggestedUrl,
  string? StartupCommand,
  string OriginalCommandLine);
