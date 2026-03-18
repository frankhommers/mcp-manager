namespace McpManager.Core.Models;

public class McpServer
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public string Name { get; set; } = string.Empty;
  public string DisplayName { get; set; } = string.Empty;
  public string? Description { get; set; }

  /// <summary>
  /// Optional group name for organizing servers (e.g., "Development", "Production", "AI Tools")
  /// </summary>
  public string Group { get; set; } = string.Empty;

  // Transport configuration
  public McpTransportType TransportType { get; set; } = McpTransportType.Stdio;

  // For Stdio servers - this is the MCP server command
  public string? Command { get; set; }
  public List<string> Args { get; set; } = [];
  public string? WorkingDirectory { get; set; }

  // For HTTP/SSE/Streamable servers
  public string? Url { get; set; }

  // Startup command - for HTTP servers, runs before the MCP is available
  // E.g., "docker compose up -d" or "open -a 'Autodesk Fusion'"
  public string? StartupCommand { get; set; }
  public string? StartupWorkingDirectory { get; set; }

  // Environment variables
  public Dictionary<string, string> EnvironmentVariables { get; set; } = [];

  // Tools that are always allowed without confirmation (Claude Code feature)
  public List<string> AlwaysAllow { get; set; } = [];

  // Cached list of all known tool names (discovered via Fetch Tools)
  public List<string> KnownTools { get; set; } = [];

  // Indicates if this HTTP server needs a stdio wrapper for Claude Desktop
  public bool RequiresStdioWrapper => TransportType != McpTransportType.Stdio;
}