using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.ConfigGenerators;
using McpManager.Core.Models;
using Tomlyn;
using Tomlyn.Model;

namespace McpManager.Core.Services;

public static class ManagedConfigMerger
{
  private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

  public static string Merge(
    string filePath,
    IConfigGenerator generator,
    string generated,
    IReadOnlyDictionary<ExportConflict, ExportConflictResolution>? resolutions = null)
  {
    if (!File.Exists(filePath))
    {
      return generated;
    }

    string existing = File.ReadAllText(filePath);
    if (generator is CodexConfigGenerator)
    {
      return MergeToml(existing, generated, filePath, resolutions);
    }

    string section = generator switch
    {
      OpenCodeConfigGenerator => "mcp",
      VsCodeConfigGenerator => "servers",
      _ => "mcpServers",
    };
    return MergeJson(existing, generated, section, filePath, resolutions);
  }

  public static string MergeJson(
    string existing,
    string generated,
    string section,
    string filePath,
    IReadOnlyDictionary<ExportConflict, ExportConflictResolution>? resolutions = null)
  {
    JsonDocumentOptions options = new()
    {
      CommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
    };
    JsonObject root = JsonNode.Parse(existing, documentOptions: options) as JsonObject
                      ?? throw new InvalidDataException($"Expected a configuration object in '{filePath}'.");
    JsonObject desiredRoot = JsonNode.Parse(generated)!.AsObject();
    JsonObject desired = desiredRoot[section]?.AsObject() ?? new JsonObject();
    JsonObject current = root[section]?.AsObject() ?? new JsonObject();

    HashSet<string> keepExisting = ResolveNames(
      current, desired, ManagedServerIdentity.Read,
      node => node?.ToJsonString(WriteOptions) ?? "null", filePath, resolutions);

    foreach (string name in current.Where(s => ManagedServerIdentity.Read(s.Value) != null)
               .Select(s => s.Key).ToList())
    {
      current.Remove(name);
    }

    foreach ((string name, JsonNode? server) in desired)
    {
      if (!keepExisting.Contains(name))
      {
        current[name] = server?.DeepClone();
      }
    }

    root[section] = current;
    return JsonSerializer.Serialize(root, WriteOptions);
  }

  public static string MergeToml(
    string existing,
    string generated,
    string filePath,
    IReadOnlyDictionary<ExportConflict, ExportConflictResolution>? resolutions = null)
  {
    TomlTable root = TomlSerializer.Deserialize<TomlTable>(existing)
                     ?? throw new InvalidDataException($"Expected a configuration table in '{filePath}'.");
    TomlTable desiredRoot = TomlSerializer.Deserialize<TomlTable>(generated)!;
    TomlTable desired = ReadServers(desiredRoot, filePath);
    TomlTable current = ReadServers(root, filePath);

    HashSet<string> keepExisting = ResolveNames(
      current, desired, ManagedServerIdentity.Read,
      server => TomlSerializer.Serialize(new TomlTable { ["server"] = server }), filePath, resolutions);

    foreach (string name in current.Where(s => ManagedServerIdentity.Read(s.Value) != null)
               .Select(s => s.Key).ToList())
    {
      current.Remove(name);
    }

    foreach ((string name, object? server) in desired)
    {
      if (!keepExisting.Contains(name))
      {
        current[name] = server;
      }
    }

    if (current.Count > 0)
    {
      root["mcp_servers"] = current;
    }
    else
    {
      root.Remove("mcp_servers");
    }

    return TomlSerializer.Serialize(root);
  }

  private static TomlTable ReadServers(TomlTable root, string filePath)
  {
    if (!root.TryGetValue("mcp_servers", out object? servers))
    {
      return new TomlTable();
    }

    return servers as TomlTable
           ?? throw new InvalidDataException($"Expected an mcp_servers table in '{filePath}'.");
  }

  private static HashSet<string> ResolveNames<T>(
    IDictionary<string, T> current,
    IDictionary<string, T> desired,
    Func<T, Guid?> readId,
    Func<T, string> serialize,
    string filePath,
    IReadOnlyDictionary<ExportConflict, ExportConflictResolution>? resolutions)
  {
    HashSet<string> keepExisting = new(StringComparer.Ordinal);
    foreach ((string name, T proposed) in desired)
    {
      if (current.TryGetValue(name, out T? existing) && readId(existing) == null)
      {
        ExportConflict conflict = new(filePath, name, serialize(existing), serialize(proposed));
        if (resolutions == null || !resolutions.TryGetValue(conflict, out ExportConflictResolution resolution))
        {
          throw new ExportConflictException(conflict);
        }

        switch (resolution)
        {
          case ExportConflictResolution.KeepExisting:
            keepExisting.Add(name);
            break;
          case ExportConflictResolution.Replace:
            break;
          default:
            throw new ArgumentOutOfRangeException(nameof(resolutions));
        }
      }
    }

    return keepExisting;
  }
}
