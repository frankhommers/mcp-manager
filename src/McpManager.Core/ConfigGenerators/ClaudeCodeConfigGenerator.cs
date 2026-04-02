using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;

namespace McpManager.Core.ConfigGenerators;

/// <summary>
/// Generates .mcp.json config files for Claude Code.
/// </summary>
public class ClaudeCodeConfigGenerator : IConfigGenerator
{
  public virtual string ClientName => "Claude Code";
  public virtual string ConfigFileName => ".mcp.json";
  public virtual string? ConfigSubFolder => null;

  public virtual string GenerateConfig(
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
        case McpTransportType.Sse:
        case McpTransportType.StreamableHttp:
          serverConfig["type"] = server.TransportType switch
          {
            McpTransportType.Http => "http",
            McpTransportType.Sse => "sse",
            McpTransportType.StreamableHttp => "streamable-http",
            _ => "http",
          };
          if (!string.IsNullOrEmpty(server.Url))
          {
            serverConfig["url"] = server.Url;
          }

          if (server.HttpHeaders.Count > 0)
          {
            JsonObject headersObj = new();
            foreach ((string key, string value) in server.HttpHeaders)
            {
              headersObj[key] = value;
            }

            serverConfig["headers"] = headersObj;
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

      List<string> allowedTools = GetEffectiveToolList(server, toolOverrides);
      if (allowedTools.Count > 0)
      {
        serverConfig["alwaysAllow"] = new JsonArray(allowedTools.Select(a => JsonValue.Create(a)).ToArray());
      }

      mcpServers[server.Name] = serverConfig;
    }

    JsonObject root = new()
    {
      ["mcpServers"] = mcpServers,
    };

    return JsonSerializer.Serialize(
      root,
      new JsonSerializerOptions
      {
        WriteIndented = true,
      });
  }

  protected static List<string> GetEffectiveToolList(
    McpServer server,
    Dictionary<Guid, List<string>>? toolOverrides)
  {
    if (toolOverrides?.TryGetValue(server.Id, out List<string>? overrides) == true)
    {
      return overrides;
    }

    return server.AlwaysAllow;
  }

  protected static Dictionary<string, string> GetMergedEnvVars(
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