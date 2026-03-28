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

  public async Task ExportAsync(TargetFolder target, IEnumerable<McpServer> allServers, GlobalSettings? settings = null)
  {
    List<McpServer> servers = allServers
      .Where(s => target.EnabledServers.Contains(s.Id) && !target.DisabledServers.Contains(s.Id)).ToList();
    Dictionary<Guid, Dictionary<string, string>> envOverrides = target.ServerEnvOverrides;
    Dictionary<Guid, List<string>> toolOverrides = target.ServerToolOverrides;
    string bridgeArgs = target.BridgeArgs;

    // Apply bridge commands from settings
    if (settings != null)
    {
      _claudeDesktopGen.BridgeCommandHttp = settings.BridgeCommandHttp;
      _claudeDesktopGen.BridgeCommandSse = settings.BridgeCommandSse;
      _claudeDesktopGen.BridgeCommandStreamableHttp = settings.BridgeCommandStreamableHttp;
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.ClaudeCode))
    {
      await ExportConfigAsync(target.Path, _claudeCodeGen, servers, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.ClaudeDesktop))
    {
      await ExportConfigAsync(target.Path, _claudeDesktopGen, servers, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.OpenCode))
    {
      // OpenCode config merges into existing opencode.jsonc
      string openCodeConfigPath = Path.Combine(target.Path, "opencode.jsonc");
      _openCodeGen.ExistingConfigPath = openCodeConfigPath;
      await ExportConfigAsync(target.Path, _openCodeGen, servers, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.Codex))
    {
      // Codex config merges into existing config.toml
      string codexConfigPath = settings?.CodexConfigPath ?? GetDefaultCodexConfigPath();
      _codexGen.ExistingConfigPath = codexConfigPath;
      string codexBasePath = Path.GetDirectoryName(codexConfigPath) ?? codexConfigPath;
      await ExportConfigAsync(codexBasePath, _codexGen, servers, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.Cursor))
    {
      await ExportConfigAsync(target.Path, _cursorGen, servers, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.Windsurf))
    {
      // Windsurf uses bridge commands like Claude Desktop
      if (settings != null)
      {
        _windsurfGen.BridgeCommandHttp = settings.BridgeCommandHttp;
        _windsurfGen.BridgeCommandSse = settings.BridgeCommandSse;
        _windsurfGen.BridgeCommandStreamableHttp = settings.BridgeCommandStreamableHttp;
      }

      await ExportConfigAsync(target.Path, _windsurfGen, servers, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.VsCode))
    {
      await ExportConfigAsync(target.Path, _vsCodeGen, servers, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.ClaudeCodeGlobal))
    {
      string claudeCodeGlobalPath = RegistryService.GetDefaultClaudeCodeGlobalConfigPath();
      _claudeCodeGlobalGen.ExistingConfigPath = claudeCodeGlobalPath;
      string basePath = Path.GetDirectoryName(claudeCodeGlobalPath) ?? claudeCodeGlobalPath;
      await ExportConfigAsync(basePath, _claudeCodeGlobalGen, servers, envOverrides, toolOverrides, bridgeArgs);

      await WriteClaudeCodePermissionsAsync(servers, toolOverrides);
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
    GlobalSettings? settings = null)
  {
    Dictionary<string, string> result = new();
    List<McpServer> serverList = allServers
      .Where(s => target.EnabledServers.Contains(s.Id) && !target.DisabledServers.Contains(s.Id)).ToList();
    Dictionary<Guid, Dictionary<string, string>> envOverrides = target.ServerEnvOverrides;
    Dictionary<Guid, List<string>> toolOverrides = target.ServerToolOverrides;
    string bridgeArgs = target.BridgeArgs;

    // Apply bridge commands from settings
    if (settings != null)
    {
      _claudeDesktopGen.BridgeCommandHttp = settings.BridgeCommandHttp;
      _claudeDesktopGen.BridgeCommandSse = settings.BridgeCommandSse;
      _claudeDesktopGen.BridgeCommandStreamableHttp = settings.BridgeCommandStreamableHttp;
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.ClaudeCode))
    {
      string path = GetConfigFilePath(target.Path, _claudeCodeGen);
      result[path] = _claudeCodeGen.GenerateConfig(serverList, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.ClaudeDesktop))
    {
      string path = GetConfigFilePath(target.Path, _claudeDesktopGen);
      result[path] = _claudeDesktopGen.GenerateConfig(serverList, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.OpenCode))
    {
      string openCodeConfigPath = Path.Combine(target.Path, "opencode.jsonc");
      _openCodeGen.ExistingConfigPath = openCodeConfigPath;
      string path = GetConfigFilePath(target.Path, _openCodeGen);
      result[path] = _openCodeGen.GenerateConfig(serverList, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.Codex))
    {
      string codexConfigPath = settings?.CodexConfigPath ?? GetDefaultCodexConfigPath();
      _codexGen.ExistingConfigPath = codexConfigPath;
      result[codexConfigPath] = _codexGen.GenerateConfig(serverList, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.Cursor))
    {
      string path = GetConfigFilePath(target.Path, _cursorGen);
      result[path] = _cursorGen.GenerateConfig(serverList, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.Windsurf))
    {
      if (settings != null)
      {
        _windsurfGen.BridgeCommandHttp = settings.BridgeCommandHttp;
        _windsurfGen.BridgeCommandSse = settings.BridgeCommandSse;
        _windsurfGen.BridgeCommandStreamableHttp = settings.BridgeCommandStreamableHttp;
      }

      string path = GetConfigFilePath(target.Path, _windsurfGen);
      result[path] = _windsurfGen.GenerateConfig(serverList, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.VsCode))
    {
      string path = GetConfigFilePath(target.Path, _vsCodeGen);
      result[path] = _vsCodeGen.GenerateConfig(serverList, envOverrides, toolOverrides, bridgeArgs);
    }

    if (target.EnabledClients.HasFlag(TargetClientFlags.ClaudeCodeGlobal))
    {
      string claudeCodeGlobalPath = RegistryService.GetDefaultClaudeCodeGlobalConfigPath();
      _claudeCodeGlobalGen.ExistingConfigPath = claudeCodeGlobalPath;
      result[claudeCodeGlobalPath] = _claudeCodeGlobalGen.GenerateConfig(serverList, envOverrides, toolOverrides, bridgeArgs);

      string settingsPath = GetClaudeCodeSettingsPath();
      result[settingsPath] = GenerateClaudeCodePermissionsPreview(serverList, toolOverrides);
    }

    return result;
  }

  private static async Task ExportConfigAsync(
    string basePath,
    IConfigGenerator generator,
    IEnumerable<McpServer> servers,
    Dictionary<Guid, Dictionary<string, string>>? envOverrides,
    Dictionary<Guid, List<string>>? toolOverrides = null,
    string? bridgeArgs = null)
  {
    string filePath = GetConfigFilePath(basePath, generator);
    string? directory = Path.GetDirectoryName(filePath);

    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }

    string content = generator.GenerateConfig(servers, envOverrides, toolOverrides, bridgeArgs);
    await File.WriteAllTextAsync(filePath, content);
  }

  private static string GetConfigFilePath(string basePath, IConfigGenerator generator)
  {
    if (string.IsNullOrEmpty(generator.ConfigSubFolder))
    {
      return Path.Combine(basePath, generator.ConfigFileName);
    }

    return Path.Combine(basePath, generator.ConfigSubFolder, generator.ConfigFileName);
  }

  private static string GetDefaultCodexConfigPath()
  {
    string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(homeDir, ".codex", "config.toml");
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

  private static async Task WriteClaudeCodePermissionsAsync(
    IEnumerable<McpServer> servers,
    Dictionary<Guid, List<string>>? toolOverrides)
  {
    string settingsPath = GetClaudeCodeSettingsPath();

    JsonObject root;
    if (File.Exists(settingsPath))
    {
      string existingContent = await File.ReadAllTextAsync(settingsPath);
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

    string directory = Path.GetDirectoryName(settingsPath) ?? settingsPath;
    if (!Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }

    string outputContent = JsonSerializer.Serialize(
      root,
      new JsonSerializerOptions
      {
        WriteIndented = true,
      });

    await File.WriteAllTextAsync(settingsPath, outputContent);
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