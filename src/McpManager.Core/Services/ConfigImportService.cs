using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;

namespace McpManager.Core.Services;

public class ConfigImportService : IConfigImportService
{
  public async Task<List<McpServer>> ImportFromClaudeCodeAsync(string filePath)
  {
    string json = await File.ReadAllTextAsync(filePath);
    JsonNode? root = JsonNode.Parse(json);
    List<McpServer> servers = new();

    JsonObject? mcpServers = root?["mcpServers"]?.AsObject();
    if (mcpServers == null)
    {
      return servers;
    }

    foreach ((string name, JsonNode? serverNode) in mcpServers)
    {
      if (serverNode == null)
      {
        continue;
      }

      McpServer server = new()
      {
        Name = name,
        DisplayName = FormatDisplayName(name),
      };

      // Determine transport type
      string? type = serverNode["type"]?.GetValue<string>();
      if (type != null)
      {
        server.TransportType = type.ToLowerInvariant() switch
        {
          "http" => McpTransportType.Http,
          "sse" => McpTransportType.Sse,
          "streamable-http" => McpTransportType.StreamableHttp,
          _ => McpTransportType.Http,
        };
        server.Url = serverNode["url"]?.GetValue<string>();
      }
      else
      {
        // Stdio server
        server.TransportType = McpTransportType.Stdio;
        server.Command = serverNode["command"]?.GetValue<string>();

        JsonArray? args = serverNode["args"]?.AsArray();
        if (args != null)
        {
          server.Args = args.Select(a => a?.GetValue<string>() ?? "").ToList();
        }
      }

      // Environment variables
      JsonObject? env = serverNode["env"]?.AsObject();
      if (env != null)
      {
        foreach ((string key, JsonNode? value) in env)
        {
          if (value != null)
          {
            server.EnvironmentVariables[key] = value.GetValue<string>();
          }
        }
      }

      // Always-allow tools
      JsonArray? alwaysAllow = serverNode["alwaysAllow"]?.AsArray();
      if (alwaysAllow != null)
      {
        server.AlwaysAllow = alwaysAllow
          .Select(a => a?.GetValue<string>() ?? "")
          .Where(a => !string.IsNullOrEmpty(a))
          .ToList();
      }

      servers.Add(server);
    }

    return servers;
  }

  public async Task<List<McpServer>> ImportFromOpenCodeAsync(string filePath)
  {
    string json = await File.ReadAllTextAsync(filePath);
    JsonNode? root = JsonNode.Parse(json);
    List<McpServer> servers = new();

    JsonObject? mcpSection = root?["mcp"]?.AsObject();
    if (mcpSection == null)
    {
      return servers;
    }

    foreach ((string name, JsonNode? serverNode) in mcpSection)
    {
      if (serverNode == null)
      {
        continue;
      }

      McpServer server = new()
      {
        Name = name,
        DisplayName = FormatDisplayName(name),
      };

      string? type = serverNode["type"]?.GetValue<string>();
      if (type == "remote")
      {
        server.TransportType = McpTransportType.Http;
        server.Url = serverNode["url"]?.GetValue<string>();
      }
      else
      {
        // local = stdio
        server.TransportType = McpTransportType.Stdio;

        JsonArray? command = serverNode["command"]?.AsArray();
        if (command != null && command.Count > 0)
        {
          server.Command = command[0]?.GetValue<string>();
          server.Args = command.Skip(1).Select(a => a?.GetValue<string>() ?? "").ToList();
        }
      }

      // Environment variables
      JsonObject? env = serverNode["environment"]?.AsObject();
      if (env != null)
      {
        foreach ((string key, JsonNode? value) in env)
        {
          if (value != null)
          {
            server.EnvironmentVariables[key] = value.GetValue<string>();
          }
        }
      }

      servers.Add(server);
    }

    return servers;
  }

  public async Task<List<McpServer>> ImportAutoDetectAsync(string filePath)
  {
    string fileName = Path.GetFileName(filePath);

    if (fileName == ".mcp.json" || fileName == "claude_desktop_config.json")
    {
      return await ImportFromClaudeCodeAsync(filePath);
    }

    if (fileName == "opencode.json")
    {
      return await ImportFromOpenCodeAsync(filePath);
    }

    // Try to detect from content
    string json = await File.ReadAllTextAsync(filePath);
    JsonNode? root = JsonNode.Parse(json);

    if (root?["mcpServers"] != null)
    {
      return await ImportFromClaudeCodeAsync(filePath);
    }

    if (root?["mcp"] != null && root?["$schema"]?.GetValue<string>()?.Contains("opencode") == true)
    {
      return await ImportFromOpenCodeAsync(filePath);
    }

    // Default to Claude Code format
    return await ImportFromClaudeCodeAsync(filePath);
  }

  private static string FormatDisplayName(string name)
  {
    // Convert "home-assistant" to "Home Assistant"
    return string.Join(
      " ",
      name.Split('-', '_')
        .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
  }
}