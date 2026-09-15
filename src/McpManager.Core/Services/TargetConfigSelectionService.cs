using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;
using Tomlyn;
using Tomlyn.Model;

namespace McpManager.Core.Services;

public class TargetConfigSelectionService(IConfigExportService exportService)
{
  public async Task<ExistingTargetServers> ReadAsync(TargetFolder target, GlobalSettings? settings = null)
  {
    HashSet<Guid> enabledIds = [];
    List<string> unreadableFiles = [];
    List<ExistingTargetServer> existingServers = [];
    int fileCount = 0;
    int unmanagedCount = 0;
    if (target.IsClipboard || string.IsNullOrWhiteSpace(target.Path))
    {
      return new ExistingTargetServers(enabledIds, fileCount, unmanagedCount, unreadableFiles);
    }

    foreach ((TargetClientFlags client, string path) in exportService.GetConfigFilePaths(target, settings))
    {
      try
      {
        string content = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        List<ExistingTargetServer> servers = client == TargetClientFlags.Codex
          ? ReadToml(content, path)
          : ReadJson(content, client, path);
        fileCount++;
        existingServers.AddRange(servers);
        foreach (ExistingTargetServer server in servers)
        {
          if (server.ManagedId is not Guid serverId)
          {
            unmanagedCount++;
          }
          else if (server.IsEnabled)
          {
            enabledIds.Add(serverId);
          }
        }
      }
      catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
      {
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                or TomlException or InvalidOperationException or ArgumentException)
      {
        unreadableFiles.Add(path);
      }
    }

    return new ExistingTargetServers(enabledIds, fileCount, unmanagedCount, unreadableFiles)
    {
      Servers = existingServers,
    };
  }

  private static List<ExistingTargetServer> ReadJson(string content, TargetClientFlags client, string path)
  {
    JsonObject root = JsonNode.Parse(content, documentOptions: new JsonDocumentOptions
    {
      CommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
    }) as JsonObject ?? throw new InvalidDataException("Expected a configuration object.");
    string section = client switch
    {
      TargetClientFlags.OpenCode => "mcp",
      TargetClientFlags.VsCode => "servers",
      _ => "mcpServers",
    };
    JsonObject servers = root[section]?.AsObject() ?? new JsonObject();
    return servers.Select(entry =>
    {
      JsonObject server = entry.Value as JsonObject
                          ?? throw new InvalidDataException("Expected a server object.");
      bool enabled = server["disabled"]?.GetValue<bool>() != true &&
                     server["enabled"]?.GetValue<bool>() != false;
      return new ExistingTargetServer(entry.Key, ManagedServerIdentity.Read(server), enabled, client, path,
        server.ToJsonString());
    }).ToList();
  }

  private static List<ExistingTargetServer> ReadToml(string content, string path)
  {
    TomlTable root = TomlSerializer.Deserialize<TomlTable>(content)
                     ?? throw new InvalidDataException("Expected a configuration table.");
    if (!root.TryGetValue("mcp_servers", out object? section))
    {
      return [];
    }

    TomlTable servers = section as TomlTable ?? throw new InvalidDataException("Expected an mcp_servers table.");
    return servers.Select(entry =>
    {
      TomlTable server = entry.Value as TomlTable ?? throw new InvalidDataException("Expected a server table.");
      bool enabled = !server.TryGetValue("enabled", out object? value) || value is not false;
      return new ExistingTargetServer(entry.Key, ManagedServerIdentity.Read(server), enabled,
        TargetClientFlags.Codex, path, TomlSerializer.Serialize(new TomlTable { ["server"] = server }));
    }).ToList();
  }
}
