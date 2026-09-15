using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Core.ConfigGenerators;

/// <summary>
/// Generates claude_desktop_config.json for Claude Desktop.
/// HTTP servers are wrapped using a bridge (mcp-remote/mcp-proxy) to convert to stdio.
/// </summary>
public class ClaudeDesktopConfigGenerator : IConfigGenerator
{
  public virtual string ClientName => "Claude Desktop";
  public virtual string ConfigFileName => "claude_desktop_config.json";
  public virtual string? ConfigSubFolder => null;

  /// <summary>
  /// Bridge command for wrapping HTTP servers. Supports {url}, {args}, and {headerArgs} placeholders.
  /// </summary>
  public string BridgeCommandHttp { get; set; } = "mcp-proxy {args} {headerArgs} {url}";

  /// <summary>
  /// Bridge command for wrapping SSE servers. Supports {url}, {args}, and {headerArgs} placeholders.
  /// </summary>
  public string BridgeCommandSse { get; set; } = "mcp-proxy {args} {headerArgs} {url}";

  /// <summary>
  /// Bridge command for wrapping Streamable HTTP servers. Supports {url}, {args}, and {headerArgs} placeholders.
  /// </summary>
  public string BridgeCommandStreamableHttp { get; set; } =
    "mcp-proxy {args} {headerArgs} --transport streamablehttp {url}";

  /// <summary>
  /// Argument pattern repeated for every HTTP header. Supports {key} and {value} placeholders.
  /// </summary>
  public string BridgeHeaderArgumentTemplate { get; set; } = "--headers {key} {value}";

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
          // Direct stdio - pass through as-is
          if (!string.IsNullOrEmpty(server.Command))
          {
            serverConfig["command"] = server.Command;
            if (server.Args.Count > 0)
            {
              serverConfig["args"] = new JsonArray(server.Args.Select(a => JsonValue.Create(a)).ToArray());
            }
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

          break;

        case McpTransportType.Http:
        case McpTransportType.Sse:
        case McpTransportType.StreamableHttp:
          // Select bridge command based on transport type
          string bridgeCommand = server.TransportType switch
          {
            McpTransportType.Http => BridgeCommandHttp,
            McpTransportType.Sse => BridgeCommandSse,
            McpTransportType.StreamableHttp => BridgeCommandStreamableHttp,
            _ => BridgeCommandHttp,
          };

          ResolvedCommand resolvedCommand = BridgeCommandResolver.Resolve(
            bridgeCommand,
            server.Url ?? string.Empty,
            bridgeArgs,
            BridgeHeaderArgumentTemplate,
            server.HttpHeaders);

          if (!string.IsNullOrWhiteSpace(resolvedCommand.Command))
          {
            serverConfig["command"] = resolvedCommand.Command;
            if (resolvedCommand.Arguments.Count > 0)
            {
              serverConfig["args"] = new JsonArray(
                resolvedCommand.Arguments.Select(a => JsonValue.Create(a)).ToArray());
            }
          }

          break;
      }

      List<string> allowedTools = GetEffectiveToolList(server, toolOverrides);
      if (allowedTools.Count > 0)
      {
        serverConfig["alwaysAllow"] = new JsonArray(allowedTools.Select(a => JsonValue.Create(a)).ToArray());
      }

      ManagedServerIdentity.Stamp(serverConfig, server, local: true);
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
