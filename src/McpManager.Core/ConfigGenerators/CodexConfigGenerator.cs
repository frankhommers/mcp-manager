using McpManager.Core.Models;
using Tomlyn;
using Tomlyn.Model;

namespace McpManager.Core.ConfigGenerators;

/// <summary>
/// Generates TOML config for Codex CLI (~/.codex/config.toml).
/// Only manages the [mcp_servers.*] sections; preserves all other config.
/// </summary>
public class CodexConfigGenerator : IConfigGenerator
{
  public string ClientName => "Codex";
  public string ConfigFileName => "config.toml";
  public string? ConfigSubFolder => null;

  /// <summary>
  /// Path to the existing config.toml to merge with.
  /// When set, existing non-MCP config is preserved.
  /// </summary>
  public string? ExistingConfigPath { get; set; }

  public string GenerateConfig(
    IEnumerable<McpServer> servers,
    Dictionary<Guid, Dictionary<string, string>>? envOverrides = null,
    Dictionary<Guid, List<string>>? toolOverrides = null,
    string? bridgeArgs = null)
  {
    // Load existing config or start fresh
    TomlTable root = LoadExistingConfig();

    // Remove all existing mcp_servers entries
    root.Remove("mcp_servers");

    // Build new mcp_servers section
    TomlTable mcpServers = new();

    foreach (McpServer server in servers)
    {
      TomlTable serverConfig = new();
      Dictionary<string, string> envVars = GetMergedEnvVars(server, envOverrides);

      switch (server.TransportType)
      {
        case McpTransportType.Stdio:
          if (!string.IsNullOrEmpty(server.Command))
          {
            serverConfig["command"] = server.Command;
            if (server.Args.Count > 0)
            {
              TomlArray args = [];
              foreach (string arg in server.Args)
              {
                args.Add(arg);
              }

              serverConfig["args"] = args;
            }

            if (!string.IsNullOrEmpty(server.WorkingDirectory))
            {
              serverConfig["cwd"] = server.WorkingDirectory;
            }
          }

          if (envVars.Count > 0)
          {
            TomlTable envTable = new();
            foreach ((string key, string value) in envVars)
            {
              envTable[key] = value;
            }

            serverConfig["env"] = envTable;
          }

          break;

        case McpTransportType.Http:
        case McpTransportType.Sse:
        case McpTransportType.StreamableHttp:
          if (!string.IsNullOrEmpty(server.Url))
          {
            serverConfig["url"] = server.Url;
          }

          // Emit http_headers for HTTP servers
          Dictionary<string, string> headers = server.HttpHeaders;
          if (headers.Count > 0)
          {
            TomlTable headersTable = new();
            foreach ((string key, string value) in headers)
            {
              headersTable[key] = value;
            }

            serverConfig["http_headers"] = headersTable;
          }

          break;
      }

      // Tool allow/deny lists
      List<string> allowedTools = GetEffectiveToolList(server, toolOverrides);
      if (allowedTools.Count > 0)
      {
        TomlArray enabledTools = [];
        foreach (string tool in allowedTools)
        {
          enabledTools.Add(tool);
        }

        serverConfig["enabled_tools"] = enabledTools;
      }

      mcpServers[server.Name] = serverConfig;
    }

    if (mcpServers.Count > 0)
    {
      root["mcp_servers"] = mcpServers;
    }

    return TomlSerializer.Serialize(root);
  }

  private TomlTable LoadExistingConfig()
  {
    if (!string.IsNullOrEmpty(ExistingConfigPath) && File.Exists(ExistingConfigPath))
    {
      try
      {
        string existingToml = File.ReadAllText(ExistingConfigPath);
        TomlTable? table = TomlSerializer.Deserialize<TomlTable>(existingToml);
        return table ?? new TomlTable();
      }
      catch
      {
        return new TomlTable();
      }
    }

    return new TomlTable();
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
