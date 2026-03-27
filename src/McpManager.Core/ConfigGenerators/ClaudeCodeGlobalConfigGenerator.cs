using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;

namespace McpManager.Core.ConfigGenerators;

/// <summary>
/// Generates the mcpServers section in ~/.claude.json for Claude Code global config.
/// Merges into existing file, preserving all non-mcpServers data.
/// </summary>
public class ClaudeCodeGlobalConfigGenerator : ClaudeCodeConfigGenerator
{
  public override string ClientName => "Claude Code (Global)";
  public override string ConfigFileName => ".claude.json";

  /// <summary>
  /// Path to the existing ~/.claude.json file to merge with.
  /// </summary>
  public string? ExistingConfigPath { get; set; }

  public override string GenerateConfig(
    IEnumerable<McpServer> servers,
    Dictionary<Guid, Dictionary<string, string>>? envOverrides = null,
    Dictionary<Guid, List<string>>? toolOverrides = null,
    string? bridgeArgs = null)
  {
    // Generate the mcpServers JSON using parent logic
    string baseJson = base.GenerateConfig(servers, envOverrides, toolOverrides, bridgeArgs);
    JsonObject generated = JsonNode.Parse(baseJson)!.AsObject();
    JsonNode? mcpServers = generated["mcpServers"]?.DeepClone();

    // Load existing file or start fresh
    JsonObject root = LoadExistingConfig();

    // Replace only mcpServers key
    root["mcpServers"] = mcpServers;

    return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
  }

  private JsonObject LoadExistingConfig()
  {
    if (string.IsNullOrEmpty(ExistingConfigPath) || !File.Exists(ExistingConfigPath))
    {
      return new JsonObject();
    }

    try
    {
      string content = File.ReadAllText(ExistingConfigPath);
      JsonNode? node = JsonNode.Parse(content);
      return node as JsonObject ?? new JsonObject();
    }
    catch
    {
      return new JsonObject();
    }
  }
}
