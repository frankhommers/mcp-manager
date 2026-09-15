using System.Text.Json.Nodes;
using McpManager.Core.Models;
using Tomlyn.Model;

namespace McpManager.Core.Services;

public static class ManagedServerIdentity
{
  public const string EnvironmentKey = "MCP_MANAGER_ID";
  public const string HeaderKey = "X-MCP-Manager-Id";

  public static void Stamp(JsonObject config, McpServer server, bool local, string environmentField = "env")
  {
    string field = local ? environmentField : "headers";
    string key = local ? EnvironmentKey : HeaderKey;
    JsonObject values = config[field]?.AsObject() ?? new JsonObject();
    RemoveMarker(values, key, !local);
    values[key] = server.Id.ToString("D");
    config[field] = values;
  }

  public static void Stamp(TomlTable config, McpServer server)
  {
    bool local = server.TransportType == McpTransportType.Stdio;
    string field = local ? "env" : "http_headers";
    string key = local ? EnvironmentKey : HeaderKey;
    TomlTable values = config.TryGetValue(field, out object? existing) && existing is TomlTable table
      ? table
      : new TomlTable();
    foreach (string candidate in values.Keys.Where(k => Matches(k, key, !local)).ToList())
    {
      values.Remove(candidate);
    }

    values[key] = server.Id.ToString("D");
    config[field] = values;
  }

  public static Guid? Read(JsonNode? config)
  {
    if (config is not JsonObject server)
    {
      return null;
    }

    return Read(server["env"] as JsonObject, EnvironmentKey, false)
           ?? Read(server["environment"] as JsonObject, EnvironmentKey, false)
           ?? Read(server["headers"] as JsonObject, HeaderKey, true);
  }

  public static Guid? Read(object? config)
  {
    if (config is not TomlTable server)
    {
      return null;
    }

    foreach ((string field, string key) in new[] { ("env", EnvironmentKey), ("http_headers", HeaderKey) })
    {
      if (!server.TryGetValue(field, out object? value) || value is not TomlTable values)
      {
        continue;
      }

      foreach ((string candidate, object? marker) in values)
      {
        if (Matches(candidate, key, field == "http_headers") && marker is string text &&
            Guid.TryParse(text, out Guid id) && id != Guid.Empty)
        {
          return id;
        }
      }
    }

    return null;
  }

  public static void RestoreImportedId(McpServer server)
  {
    string? marker = server.EnvironmentVariables.GetValueOrDefault(EnvironmentKey)
                     ?? server.HttpHeaders.FirstOrDefault(h => Matches(h.Key, HeaderKey, true)).Value;
    if (Guid.TryParse(marker, out Guid id) && id != Guid.Empty)
    {
      server.Id = id;
      server.EnvironmentVariables.Remove(EnvironmentKey);
      foreach (string key in server.HttpHeaders.Keys.Where(k => Matches(k, HeaderKey, true)).ToList())
      {
        server.HttpHeaders.Remove(key);
      }
    }
  }

  private static Guid? Read(JsonObject? values, string key, bool ignoreCase)
  {
    if (values == null)
    {
      return null;
    }

    foreach ((string candidate, JsonNode? marker) in values)
    {
      if (Matches(candidate, key, ignoreCase) && marker is JsonValue value &&
          value.TryGetValue(out string? text) && Guid.TryParse(text, out Guid id) && id != Guid.Empty)
      {
        return id;
      }
    }

    return null;
  }

  private static void RemoveMarker(JsonObject values, string key, bool ignoreCase)
  {
    foreach (string candidate in values.Select(v => v.Key).Where(k => Matches(k, key, ignoreCase)).ToList())
    {
      values.Remove(candidate);
    }
  }

  private static bool Matches(string candidate, string key, bool ignoreCase) =>
    candidate.Equals(key, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
