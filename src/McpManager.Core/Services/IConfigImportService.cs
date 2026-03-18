using McpManager.Core.Models;

namespace McpManager.Core.Services;

public interface IConfigImportService
{
  /// <summary>
  /// Import MCP servers from a Claude Code .mcp.json file.
  /// </summary>
  Task<List<McpServer>> ImportFromClaudeCodeAsync(string filePath);

  /// <summary>
  /// Import MCP servers from an OpenCode opencode.json file.
  /// </summary>
  Task<List<McpServer>> ImportFromOpenCodeAsync(string filePath);

  /// <summary>
  /// Auto-detect format and import.
  /// </summary>
  Task<List<McpServer>> ImportAutoDetectAsync(string filePath);
}