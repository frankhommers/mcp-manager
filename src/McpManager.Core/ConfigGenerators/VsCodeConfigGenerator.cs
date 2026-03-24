using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;

namespace McpManager.Core.ConfigGenerators;

/// <summary>
/// Generates .vscode/mcp.json config files for VS Code (GitHub Copilot).
/// Format: { "mcp": { "servers": { "name": { "type": "stdio", "command": "...", "args": [] } } } }
/// </summary>
public class VsCodeConfigGenerator : IConfigGenerator
{
  public string ClientName => "VS Code";
  public string ConfigFileName => "mcp.json";
  public string? ConfigSubFolder => ".vscode";

  public string GenerateConfig(
    IEnumerable<McpServer> servers,
    Dictionary<Guid, Dictionary<string, string>>? envOverrides = null,
    Dictionary<Guid, List<string>>? toolOverrides = null,
    string? bridgeArgs = null)
  {
    JsonObject mcpServers = new();

    foreach (McpServer server in servers)
    {
      JsonObject serverConfig = new();
      Dictionary<string, string> envVars = GetMergedEnvVars(server, envOverrides);

      switch (server.TransportType)
      {
        case McpTransportType.Stdio:
          serverConfig["type"] = "stdio";
          if (!string.IsNullOrEmpty(server.Command))
          {
            serverConfig["command"] = server.Command;
            if (server.Args.Count > 0)
            {
              serverConfig["args"] = new JsonArray(server.Args.Select(a => JsonValue.Create(a)).ToArray());
            }
          }

          break;

        case McpTransportType.Http:
        case McpTransportType.StreamableHttp:
          serverConfig["type"] = "http";
          if (!string.IsNullOrEmpty(server.Url))
          {
            serverConfig["url"] = server.Url;
          }

          break;

        case McpTransportType.Sse:
          serverConfig["type"] = "sse";
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

        serverConfig["env"] = envObj;
      }

      mcpServers[server.Name] = serverConfig;
    }

    JsonObject root = new()
    {
      ["mcp"] = new JsonObject
      {
        ["servers"] = mcpServers,
      },
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
