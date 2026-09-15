using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.ConfigGenerators;
using McpManager.Core.Models;

namespace McpManager.Core.Services;

public class ConfigExportService : IConfigExportService
{
  private readonly ClaudeCodeConfigGenerator _claudeCodeGen = new();
  private readonly ClaudeCodeGlobalConfigGenerator _claudeCodeGlobalGen = new();
  private readonly ClaudeDesktopConfigGenerator _claudeDesktopGen = new();
  private readonly OpenCodeConfigGenerator _openCodeGen = new();
  private readonly CodexConfigGenerator _codexGen = new();
  private readonly CursorConfigGenerator _cursorGen = new();
  private readonly WindsurfConfigGenerator _windsurfGen = new();
  private readonly VsCodeConfigGenerator _vsCodeGen = new();

  public Dictionary<TargetClientFlags, string> GetConfigFilePaths(
    TargetFolder target, GlobalSettings? settings = null)
  {
    Dictionary<TargetClientFlags, IConfigGenerator> generators = new()
    {
      [TargetClientFlags.ClaudeCode] = _claudeCodeGen,
      [TargetClientFlags.ClaudeDesktop] = _claudeDesktopGen,
      [TargetClientFlags.OpenCode] = _openCodeGen,
      [TargetClientFlags.Codex] = _codexGen,
      [TargetClientFlags.Cursor] = _cursorGen,
      [TargetClientFlags.Windsurf] = _windsurfGen,
      [TargetClientFlags.VsCode] = _vsCodeGen,
      [TargetClientFlags.ClaudeCodeGlobal] = _claudeCodeGlobalGen,
    };
    Dictionary<TargetClientFlags, string> paths = new();
    foreach ((TargetClientFlags client, IConfigGenerator generator) in generators)
    {
      if (!target.EnabledClients.HasFlag(client))
      {
        continue;
      }

      paths[client] = client switch
      {
        TargetClientFlags.Codex when target.IsGlobal =>
          settings?.CodexConfigPath ?? RegistryService.GetDefaultCodexConfigPath(),
        TargetClientFlags.Codex => Path.Combine(target.Path, ".codex", "config.toml"),
        TargetClientFlags.ClaudeCodeGlobal => RegistryService.GetDefaultClaudeCodeGlobalConfigPath(),
        _ => GetConfigFilePath(target.Path, generator),
      };
    }

    return paths;
  }

  public async Task ExportAsync(TargetFolder target, IEnumerable<McpServer> allServers, GlobalSettings? settings = null)
  {
    Dictionary<string, string> configs = PreviewConfigs(target, allServers, settings);
    await WriteConfigsAsync(configs);
  }

  public async Task<Dictionary<string, string>?> PrepareExportAsync(
    IEnumerable<TargetFolder> targets,
    IEnumerable<McpServer> allServers,
    GlobalSettings? settings,
    Func<ExportConflict, Task<ExportConflictResolution?>> resolveConflictAsync)
  {
    List<TargetFolder> targetList = targets.ToList();
    List<McpServer> serverList = allServers.ToList();
    Dictionary<ExportConflict, ExportConflictResolution> resolutions = new();

    while (true)
    {
      try
      {
        Dictionary<string, string> configs = new();
        foreach (TargetFolder target in targetList)
        {
          foreach ((string path, string content) in PreviewConfigs(target, serverList, settings, resolutions))
          {
            configs[path] = content;
          }
        }

        return configs;
      }
      catch (ExportConflictException ex)
      {
        ExportConflictResolution? resolution = await resolveConflictAsync(ex.Conflict);
        if (resolution == null)
        {
          return null;
        }

        resolutions[ex.Conflict] = resolution.Value;
      }
    }
  }

  public async Task WriteConfigsAsync(IReadOnlyDictionary<string, string> configs)
  {
    foreach ((string filePath, string content) in configs)
    {
      string? directory = Path.GetDirectoryName(filePath);
      if (!string.IsNullOrEmpty(directory))
      {
        Directory.CreateDirectory(directory);
      }

      await File.WriteAllTextAsync(filePath, content);
    }
  }

  public IEnumerable<McpServer> GetEffectiveServers(
    TargetFolder target,
    IEnumerable<McpServer> allServers,
    IEnumerable<TargetFolder> allTargets)
  {
    // Start with explicitly enabled servers
    HashSet<Guid> enabledIds = new(target.EnabledServers);

    // For now, simple implementation - just return enabled minus disabled
    // TODO: Add inheritance from global targets
    HashSet<Guid> disabledIds = target.DisabledServers;

    return allServers.Where(s => enabledIds.Contains(s.Id) && !disabledIds.Contains(s.Id));
  }

  public Dictionary<string, string> PreviewConfigs(
    TargetFolder target,
    IEnumerable<McpServer> allServers,
    GlobalSettings? settings = null,
    IReadOnlyDictionary<ExportConflict, ExportConflictResolution>? resolutions = null)
  {
    Dictionary<string, string> result = new();
    List<McpServer> serverList = allServers
      .Where(s => target.EnabledServers.Contains(s.Id) && !target.DisabledServers.Contains(s.Id)).ToList();
    HashSet<string> names = new(StringComparer.Ordinal);
    HashSet<Guid> ids = [];
    foreach (McpServer server in serverList)
    {
      if (server.Id == Guid.Empty || !ids.Add(server.Id) ||
          string.IsNullOrWhiteSpace(server.Name) || !names.Add(server.Name))
      {
        throw new InvalidOperationException("Export requires unique server names and non-empty, unique server IDs.");
      }
    }

    Dictionary<Guid, Dictionary<string, string>> envOverrides = target.ServerEnvOverrides;
    Dictionary<Guid, List<string>> toolOverrides = target.ServerToolOverrides;
    string bridgeArgs = target.BridgeArgs;
    Dictionary<TargetClientFlags, string> paths = GetConfigFilePaths(target, settings);

    // Apply bridge commands from settings
    if (settings != null)
    {
      _claudeDesktopGen.BridgeCommandHttp = settings.BridgeCommandHttp;
      _claudeDesktopGen.BridgeCommandSse = settings.BridgeCommandSse;
      _claudeDesktopGen.BridgeCommandStreamableHttp = settings.BridgeCommandStreamableHttp;
      _claudeDesktopGen.BridgeHeaderArgumentTemplate = settings.BridgeHeaderArgumentTemplate;
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.ClaudeCode))
    {
      string path = paths[TargetClientFlags.ClaudeCode];
      result[path] = GenerateMergedConfig(
        path, _claudeCodeGen, serverList, envOverrides, toolOverrides, bridgeArgs, resolutions);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.ClaudeDesktop))
    {
      string path = paths[TargetClientFlags.ClaudeDesktop];
      result[path] = GenerateMergedConfig(
        path, _claudeDesktopGen, serverList, envOverrides, toolOverrides, bridgeArgs, resolutions);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.OpenCode))
    {
      string path = paths[TargetClientFlags.OpenCode];
      _openCodeGen.ExistingConfigPath = path;
      result[path] = GenerateMergedConfig(
        path, _openCodeGen, serverList, envOverrides, toolOverrides, bridgeArgs, resolutions);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.Codex))
    {
      string codexConfigPath = paths[TargetClientFlags.Codex];
      _codexGen.ExistingConfigPath = codexConfigPath;
      result[codexConfigPath] = GenerateMergedConfig(
        codexConfigPath, _codexGen, serverList, envOverrides, toolOverrides, bridgeArgs, resolutions);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.Cursor))
    {
      string path = paths[TargetClientFlags.Cursor];
      result[path] = GenerateMergedConfig(
        path, _cursorGen, serverList, envOverrides, toolOverrides, bridgeArgs, resolutions);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.Windsurf))
    {
      string path = paths[TargetClientFlags.Windsurf];
      result[path] = GenerateMergedConfig(
        path, _windsurfGen, serverList, envOverrides, toolOverrides, bridgeArgs, resolutions);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.VsCode))
    {
      string path = paths[TargetClientFlags.VsCode];
      result[path] = GenerateMergedConfig(
        path, _vsCodeGen, serverList, envOverrides, toolOverrides, bridgeArgs, resolutions);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.ClaudeCodeGlobal))
    {
      string claudeCodeGlobalPath = paths[TargetClientFlags.ClaudeCodeGlobal];
      _claudeCodeGlobalGen.ExistingConfigPath = claudeCodeGlobalPath;
      result[claudeCodeGlobalPath] = GenerateMergedConfig(
        claudeCodeGlobalPath, _claudeCodeGlobalGen, serverList, envOverrides, toolOverrides, bridgeArgs, resolutions);

      string settingsPath = GetClaudeCodeSettingsPath();
      JsonNode exportedServers = JsonNode.Parse(result[claudeCodeGlobalPath])!["mcpServers"]!;
      result[settingsPath] = GenerateClaudeCodePermissionsPreview(
        serverList.Where(s => ManagedServerIdentity.Read(exportedServers[s.Name]) == s.Id), toolOverrides);
    }

    return result;
  }

  private static string GenerateMergedConfig(
    string filePath,
    IConfigGenerator generator,
    IEnumerable<McpServer> servers,
    Dictionary<Guid, Dictionary<string, string>>? envOverrides,
    Dictionary<Guid, List<string>>? toolOverrides,
    string? bridgeArgs,
    IReadOnlyDictionary<ExportConflict, ExportConflictResolution>? resolutions)
  {
    string generated = generator.GenerateConfig(servers, envOverrides, toolOverrides, bridgeArgs);
    return ManagedConfigMerger.Merge(filePath, generator, generated, resolutions);
  }

  private static string GetConfigFilePath(string basePath, IConfigGenerator generator)
  {
    if (generator is OpenCodeConfigGenerator && !File.Exists(Path.Combine(basePath, "opencode.jsonc")) &&
        File.Exists(Path.Combine(basePath, "opencode.json")))
    {
      return Path.Combine(basePath, "opencode.json");
    }

    if (string.IsNullOrEmpty(generator.ConfigSubFolder))
    {
      return Path.Combine(basePath, generator.ConfigFileName);
    }

    return Path.Combine(basePath, generator.ConfigSubFolder, generator.ConfigFileName);
  }

  private static string GetClaudeCodeSettingsPath()
  {
    string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(homeDir, ".claude", "settings.json");
  }

  private static string GenerateClaudeCodePermissionsPreview(
    IEnumerable<McpServer> servers,
    Dictionary<Guid, List<string>>? toolOverrides)
  {
    string settingsPath = GetClaudeCodeSettingsPath();

    JsonObject root;
    if (File.Exists(settingsPath))
    {
      string existingContent = File.ReadAllText(settingsPath);
      root = JsonNode.Parse(existingContent)?.AsObject() ?? new JsonObject();
    }
    else
    {
      root = new JsonObject();
    }

    if (!root.ContainsKey("permissions"))
    {
      root["permissions"] = new JsonObject();
    }

    JsonObject permissions = root["permissions"]!.AsObject();

    if (!permissions.ContainsKey("allow"))
    {
      permissions["allow"] = new JsonArray();
    }

    JsonArray allowArray = permissions["allow"]!.AsArray();

    HashSet<string> managedServerNames = servers.Select(s => s.Name).ToHashSet();
    List<JsonNode?> nodesToRemove = [];

    foreach (JsonNode? node in allowArray)
    {
      if (node is JsonValue value && value.TryGetValue<string>(out string? str) && str.StartsWith("mcp__"))
      {
        string[] parts = str.Split("__", 3);
        if (parts.Length >= 2)
        {
          string serverName = parts[1];
          if (managedServerNames.Contains(serverName))
          {
            nodesToRemove.Add(node);
          }
        }
      }
    }

    foreach (JsonNode? node in nodesToRemove)
    {
      allowArray.Remove(node);
    }

    foreach (McpServer server in servers)
    {
      List<string> effectiveTools = GetEffectiveToolList(server, toolOverrides);

      if (effectiveTools.Count > 0)
      {
        foreach (string tool in effectiveTools)
        {
          string permissionEntry = $"mcp__{server.Name}__{tool}";
          allowArray.Add(permissionEntry);
        }
      }
      else
      {
        string permissionEntry = $"mcp__{server.Name}__*";
        allowArray.Add(permissionEntry);
      }
    }

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
}
