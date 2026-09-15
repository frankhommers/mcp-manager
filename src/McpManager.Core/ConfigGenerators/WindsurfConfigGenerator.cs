using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Core.ConfigGenerators;

/// <summary>
/// Generates ~/.codeium/windsurf/mcp_config.json using Windsurf's native remote transport support.
/// </summary>
public class WindsurfConfigGenerator : IConfigGenerator
{
  public string ClientName => "Windsurf";
  public string ConfigFileName => "mcp_config.json";
  public string? ConfigSubFolder => null;

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

      if (server.TransportType == McpTransportType.Stdio)
      {
        if (!string.IsNullOrEmpty(server.Command))
        {
          serverConfig["command"] = server.Command;
          if (server.Args.Count > 0)
          {
            serverConfig["args"] = new JsonArray(server.Args.Select(a => JsonValue.Create(a)).ToArray());
          }
        }

        Dictionary<string, string> envVars = GetMergedEnvVars(server, envOverrides);
        if (envVars.Count > 0)
        {
          JsonObject envObj = new();
          foreach ((string key, string value) in envVars)
          {
            envObj[key] = value;
          }

          serverConfig["env"] = envObj;
        }
      }
      else
      {
        if (!string.IsNullOrEmpty(server.Url))
        {
          serverConfig["serverUrl"] = server.Url;
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
      }

      List<string> allowedTools = GetEffectiveToolList(server, toolOverrides);
      if (allowedTools.Count > 0)
      {
        serverConfig["alwaysAllow"] = new JsonArray(allowedTools.Select(a => JsonValue.Create(a)).ToArray());
      }

      ManagedServerIdentity.Stamp(serverConfig, server, server.TransportType == McpTransportType.Stdio);
      mcpServers[server.Name] = serverConfig;
    }

    JsonObject root = new()
    {
      ["mcpServers"] = mcpServers,
    };

    return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
  }

  private static List<string> GetEffectiveToolList(
    McpServer server,
    Dictionary<Guid, List<string>>? toolOverrides)
  {
    if (toolOverrides?.TryGetValue(server.Id, out List<string>? overrides) == true)
    {
      return overrides;
    }

    return server.AlwaysAllow;
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
