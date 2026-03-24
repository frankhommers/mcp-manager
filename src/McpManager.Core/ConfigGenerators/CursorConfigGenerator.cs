namespace McpManager.Core.ConfigGenerators;

/// <summary>
/// Generates .cursor/mcp.json config files for Cursor.
/// Same JSON format as Claude Code (mcpServers), different file path.
/// </summary>
public class CursorConfigGenerator : ClaudeCodeConfigGenerator
{
  public override string ClientName => "Cursor";
  public override string ConfigFileName => "mcp.json";
  public override string? ConfigSubFolder => ".cursor";
}
