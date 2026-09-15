using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;
using Tomlyn;
using Tomlyn.Model;

namespace McpManager.Core.Services;

public partial class ConfigImportService
{
  public McpServer ImportServer(ExistingTargetServer source)
  {
    if (string.IsNullOrWhiteSpace(source.Name))
    {
      throw new InvalidDataException("The server needs a configuration name before it can be imported.");
    }

    JsonObject config;
    if (source.Client == TargetClientFlags.Codex)
    {
      TomlTable root = TomlSerializer.Deserialize<TomlTable>(source.Configuration)
                       ?? throw new InvalidDataException("Expected a server configuration.");
      config = JsonSerializer.SerializeToNode(root["server"]) as JsonObject
               ?? throw new InvalidDataException("Expected a server configuration.");
    }
    else
    {
      config = JsonNode.Parse(source.Configuration) as JsonObject
               ?? throw new InvalidDataException("Expected a server configuration.");
    }

    bool isOpenCode = source.Client == TargetClientFlags.OpenCode;
    bool isCodex = source.Client == TargetClientFlags.Codex;
    string? type = config["type"]?.GetValue<string>()?.ToLowerInvariant();
    string? url = config["url"]?.GetValue<string>() ?? config["serverUrl"]?.GetValue<string>();
    McpServer server = new()
    {
      Name = source.Name,
      DisplayName = FormatDisplayName(source.Name),
      WorkingDirectory = config["cwd"]?.GetValue<string>(),
      EnvironmentVariables = ReadStringValues(config[isOpenCode ? "environment" : "env"]),
      HttpHeaders = ReadStringValues(config[isCodex ? "http_headers" : "headers"]),
      AlwaysAllow = ReadStringList(config[isCodex ? "enabled_tools" : "alwaysAllow"]),
    };

    if (string.IsNullOrWhiteSpace(server.DisplayName))
    {
      server.DisplayName = source.Name;
    }

    if (type is "stdio" or "local" || (type == null && url == null))
    {
      server.TransportType = McpTransportType.Stdio;
      if (isOpenCode)
      {
        List<string> command = ReadStringList(config["command"]);
        server.Command = command.FirstOrDefault();
        server.Args = command.Skip(1).ToList();
      }
      else
      {
        server.Command = config["command"]?.GetValue<string>();
        server.Args = ReadStringList(config["args"]);
      }

      if (string.IsNullOrWhiteSpace(server.Command))
      {
        throw new InvalidDataException($"'{source.Name}' has no local server command to import.");
      }
    }
    else
    {
      server.TransportType = type switch
      {
        "sse" => McpTransportType.Sse,
        "http" => McpTransportType.Http,
        "streamable-http" or "remote" or null => McpTransportType.StreamableHttp,
        _ => throw new InvalidDataException($"'{source.Name}' uses an unsupported server type."),
      };
      server.Url = url;
      if (string.IsNullOrWhiteSpace(server.Url))
      {
        throw new InvalidDataException($"'{source.Name}' has no remote server URL to import.");
      }
    }

    ManagedServerIdentity.RestoreImportedId(server);
    return server;
  }

  private static Dictionary<string, string> ReadStringValues(JsonNode? node)
  {
    Dictionary<string, string> values = new();
    if (node == null)
    {
      return values;
    }

    foreach ((string key, JsonNode? value) in node.AsObject())
    {
      if (value != null)
      {
        values[key] = value.GetValue<string>();
      }
    }

    return values;
  }

  private static List<string> ReadStringList(JsonNode? node) =>
    node?.AsArray().Select(value => value?.GetValue<string>() ?? string.Empty).ToList() ?? [];
}
