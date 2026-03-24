namespace McpManager.Core.ConfigGenerators;

/// <summary>
/// Generates mcp_config.json for Windsurf.
/// Same JSON format as Claude Desktop (mcpServers with bridge wrapping), different file path.
/// Target path should point to ~/.codeium/windsurf/.
/// </summary>
public class WindsurfConfigGenerator : ClaudeDesktopConfigGenerator
{
  public override string ClientName => "Windsurf";
  public override string ConfigFileName => "mcp_config.json";
  public override string? ConfigSubFolder => null;
}
