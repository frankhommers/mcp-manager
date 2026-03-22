namespace McpManager.Core.Models;

/// <summary>
/// Root container for all MCP Manager data, persisted as JSON.
/// </summary>
public class McpRegistry
{
  public List<McpServer> Servers { get; set; } = [];
  public List<TargetFolder> TargetFolders { get; set; } = [];
  public GlobalSettings Settings { get; set; } = new();
}

/// <summary>
/// A target folder where MCP configs can be exported.
/// </summary>
public class TargetFolder
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public string Name { get; set; } = string.Empty;
  public string Path { get; set; } = string.Empty;

  /// <summary>
  /// Which client configs to generate for this folder.
  /// </summary>
  public TargetClientFlags EnabledClients { get; set; } = TargetClientFlags.ClaudeCode;

  /// <summary>
  /// MCPs enabled at this folder level (by server ID).
  /// </summary>
  public HashSet<Guid> EnabledServers { get; set; } = [];

  /// <summary>
  /// MCPs explicitly disabled at this level (overrides inheritance).
  /// </summary>
  public HashSet<Guid> DisabledServers { get; set; } = [];

  /// <summary>
  /// Per-server environment variable overrides for this folder.
  /// Key: Server ID, Value: env var overrides.
  /// </summary>
  public Dictionary<Guid, Dictionary<string, string>> ServerEnvOverrides { get; set; } = [];

  /// <summary>
  /// Is this a global target (e.g., Claude Desktop config)?
  /// </summary>
  public bool IsGlobal { get; set; }

  /// <summary>
  /// Is this a clipboard target (copies config to clipboard instead of file)?
  /// </summary>
  public bool IsClipboard { get; set; }

  /// <summary>
  /// Extra bridge arguments for this target. Used with {args} placeholder in bridge commands.
  /// Example: "--timeout 30 --verbose"
  /// </summary>
  public string BridgeArgs { get; set; } = string.Empty;

  /// <summary>
  /// Per-server tool permission overrides. Key: Server ID, Value: list of allowed tool names.
  /// When absent for a server, the server's default AlwaysAllow is used.
  /// </summary>
  public Dictionary<Guid, List<string>> ServerToolOverrides { get; set; } = [];
}

[Flags]
public enum TargetClientFlags
{
  None = 0,
  ClaudeCode = 1,
  ClaudeDesktop = 2,
  OpenCode = 4,
  Codex = 8,
  All = ClaudeCode | ClaudeDesktop | OpenCode | Codex,
}

public class GlobalSettings
{
  public string? ClaudeDesktopConfigPath { get; set; }
  public string? CodexConfigPath { get; set; }
  public string? DefaultProjectsRoot { get; set; }
  public bool AutoSyncOnChange { get; set; }

  /// <summary>
  /// Bridge command for wrapping HTTP servers for Claude Desktop.
  /// Placeholders: {url} = server URL, {args} = extra args per target.
  /// </summary>
  public string BridgeCommandHttp { get; set; } = "mcp-proxy {args} {url}";

  /// <summary>
  /// Bridge command for wrapping SSE servers for Claude Desktop.
  /// Placeholders: {url} = server URL, {args} = extra args per target.
  /// </summary>
  public string BridgeCommandSse { get; set; } = "mcp-proxy {args} {url}";

  /// <summary>
  /// Bridge command for wrapping Streamable HTTP servers for Claude Desktop.
  /// Placeholders: {url} = server URL, {args} = extra args per target.
  /// </summary>
  public string BridgeCommandStreamableHttp { get; set; } = "mcp-proxy {args} --transport streamablehttp {url}";
}