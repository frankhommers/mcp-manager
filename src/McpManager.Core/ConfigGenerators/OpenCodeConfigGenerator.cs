using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;

namespace McpManager.Core.ConfigGenerators;

/// <summary>
/// Generates opencode.jsonc config for OpenCode (~/.config/opencode/).
/// Only manages the "mcp" section; preserves all other config.
/// Supports JSONC (comments and trailing commas).
/// </summary>
public class OpenCodeConfigGenerator : IConfigGenerator
{
  public string ClientName => "OpenCode";
  public string ConfigFileName => "opencode.jsonc";
  public string? ConfigSubFolder => null;

  /// <summary>
  /// Path to the existing config file to merge with.
  /// When set, existing non-MCP config is preserved.
  /// </summary>
  public string? ExistingConfigPath { get; set; }

  private static readonly JsonSerializerOptions WriteOptions = new()
  {
    WriteIndented = true,
  };

  private static readonly JsonDocumentOptions ReadOptions = new()
  {
    CommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
  };

  public string GenerateConfig(
    IEnumerable<McpServer> servers,
    Dictionary<Guid, Dictionary<string, string>>? envOverrides = null,
    Dictionary<Guid, List<string>>? toolOverrides = null,
    string? bridgeArgs = null)
  {
    // Load existing config or start fresh
    JsonObject root = LoadExistingConfig();

    // Build new mcp section
    JsonObject mcpSection = new();

    foreach (McpServer server in servers)
    {
      JsonObject serverConfig = new()
      {
        ["enabled"] = true,
      };
      Dictionary<string, string> envVars = GetMergedEnvVars(server, envOverrides);

      switch (server.TransportType)
      {
        case McpTransportType.Stdio:
          serverConfig["type"] = "local";
          if (!string.IsNullOrEmpty(server.Command))
          {
            JsonArray commandArray = new() {JsonValue.Create(server.Command)};
            foreach (string arg in server.Args)
            {
              commandArray.Add(JsonValue.Create(arg));
            }

            serverConfig["command"] = commandArray;
          }

          break;

        case McpTransportType.Http:
        case McpTransportType.Sse:
        case McpTransportType.StreamableHttp:
          serverConfig["type"] = "remote";
          if (!string.IsNullOrEmpty(server.Url))
          {
            serverConfig["url"] = server.Url;
          }

          break;
      }

      if (envVars.Count > 0)
      {
        JsonObject envObj = new();
        foreach ((string key, string value) in envVars)
        {
          envObj[key] = value;
        }

        serverConfig["environment"] = envObj;
      }

      mcpSection[server.Name] = serverConfig;
    }

    // Replace only the mcp section
    root["mcp"] = mcpSection;

    // Ensure $schema is present
    if (!root.ContainsKey("$schema"))
    {
      // Insert at beginning by rebuilding
      JsonObject ordered = new() {["$schema"] = "https://opencode.ai/config.json"};
      foreach ((string key, JsonNode? value) in root)
      {
        if (key != "$schema")
        {
          ordered[key] = value?.DeepClone();
        }
      }

      return JsonSerializer.Serialize(ordered, WriteOptions);
    }

    return JsonSerializer.Serialize(root, WriteOptions);
  }

  private JsonObject LoadExistingConfig()
  {
    if (string.IsNullOrEmpty(ExistingConfigPath))
    {
      return new JsonObject();
    }

    // Try .jsonc first, then .json
    string? filePath = null;
    if (File.Exists(ExistingConfigPath))
    {
      filePath = ExistingConfigPath;
    }
    else
    {
      // Try the other extension
      string alt = ExistingConfigPath.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase)
        ? ExistingConfigPath[..^1] // .jsonc -> .json
        : ExistingConfigPath + "c"; // .json -> .jsonc

      if (File.Exists(alt))
      {
        filePath = alt;
      }
    }

    if (filePath == null)
    {
      return new JsonObject();
    }

    try
    {
      string content = File.ReadAllText(filePath);
      JsonNode? node = JsonNode.Parse(content, documentOptions: ReadOptions);
      return node as JsonObject ?? new JsonObject();
    }
    catch
    {
      return new JsonObject();
    }
  }

  private static Dictionary<string, string> GetMergedEnvVars(
    McpServer server,
    Dictionary<Guid, Dictionary<string, string>>? envOverrides)
  {
    Dictionary<string, string> result = new(server.EnvironmentVariables);

    if (envOverrides?.TryGetValue(server.Id, out Dictionary<string, string>? overrides) == true)
    {
      foreach ((string key, string value) in overrides)
      {
        result[key] = value;
      }
    }

    return result;
  }
}
