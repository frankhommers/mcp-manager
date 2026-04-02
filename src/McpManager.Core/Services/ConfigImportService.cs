using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;
using Tomlyn;
using Tomlyn.Model;

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

        // HTTP headers
        JsonObject? headers = serverNode["headers"]?.AsObject();
        if (headers != null)
        {
          foreach ((string hKey, JsonNode? hValue) in headers)
          {
            if (hValue != null)
            {
              server.HttpHeaders[hKey] = hValue.GetValue<string>();
            }
          }
        }
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

  public async Task<List<McpServer>> ImportFromCodexAsync(string filePath)
  {
    string toml = await File.ReadAllTextAsync(filePath);
    TomlTable? root = TomlSerializer.Deserialize<TomlTable>(toml);
    List<McpServer> servers = [];

    if (root == null || !root.TryGetValue("mcp_servers", out object? mcpServersObj) || mcpServersObj is not TomlTable mcpServers)
    {
      return servers;
    }

    foreach ((string name, object? serverObj) in mcpServers)
    {
      if (serverObj is not TomlTable serverTable)
      {
        continue;
      }

      McpServer server = new()
      {
        Name = name,
        DisplayName = FormatDisplayName(name),
      };

      // Determine if stdio or HTTP based on presence of "command" vs "url"
      if (serverTable.TryGetValue("url", out object? urlObj) && urlObj is string url)
      {
        server.TransportType = McpTransportType.StreamableHttp;
        server.Url = url;

        // Import http_headers
        if (serverTable.TryGetValue("http_headers", out object? headersObj) && headersObj is TomlTable headers)
        {
          foreach ((string key, object? value) in headers)
          {
            if (value is string headerValue)
            {
              server.HttpHeaders[key] = headerValue;
            }
          }
        }
      }
      else if (serverTable.TryGetValue("command", out object? commandObj) && commandObj is string command)
      {
        server.TransportType = McpTransportType.Stdio;
        server.Command = command;

        if (serverTable.TryGetValue("args", out object? argsObj) && argsObj is TomlArray args)
        {
          server.Args = args.OfType<string>().ToList();
        }

        if (serverTable.TryGetValue("cwd", out object? cwdObj) && cwdObj is string cwd)
        {
          server.WorkingDirectory = cwd;
        }
      }

      // Environment variables
      if (serverTable.TryGetValue("env", out object? envObj) && envObj is TomlTable env)
      {
        foreach ((string key, object? value) in env)
        {
          if (value is string envValue)
          {
            server.EnvironmentVariables[key] = envValue;
          }
        }
      }

      // Tool lists
      if (serverTable.TryGetValue("enabled_tools", out object? toolsObj) && toolsObj is TomlArray tools)
      {
        server.AlwaysAllow = tools.OfType<string>().ToList();
      }

      servers.Add(server);
    }

    return servers;
  }

  public async Task<List<McpServer>> ImportAutoDetectAsync(string filePath)
  {
    string fileName = Path.GetFileName(filePath);

    if (fileName == "config.toml" || filePath.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
    {
      return await ImportFromCodexAsync(filePath);
    }

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