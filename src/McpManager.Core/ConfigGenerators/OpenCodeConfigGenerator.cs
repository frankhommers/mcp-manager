using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;

namespace McpManager.Core.ConfigGenerators;

/// <summary>
/// Generates .opencode/opencode.json config files for OpenCode.
/// </summary>
public class OpenCodeConfigGenerator : IConfigGenerator
{
  public string ClientName => "OpenCode";
  public string ConfigFileName => "opencode.json";
  public string? ConfigSubFolder => ".opencode";

  public string GenerateConfig(
    IEnumerable<McpServer> servers,
    Dictionary<Guid, Dictionary<string, string>>? envOverrides = null,
    Dictionary<Guid, List<string>>? toolOverrides = null,
    string? bridgeArgs = null)
  {
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

    JsonObject root = new()
    {
      ["$schema"] = "https://opencode.ai/config.json",
      ["mcp"] = mcpSection,
    };

    return JsonSerializer.Serialize(
      root,
      new JsonSerializerOptions
      {
        WriteIndented = true,
      });
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