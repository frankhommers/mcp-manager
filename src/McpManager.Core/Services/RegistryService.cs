using System.Text.Json;
using McpManager.Core.Models;

namespace McpManager.Core.Services;

public class RegistryService : IRegistryService
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

  public string RegistryFilePath { get; }

  public RegistryService(string? customPath = null)
  {
    RegistryFilePath = customPath ?? GetDefaultRegistryPath();
  }

  public async Task<McpRegistry> LoadAsync()
  {
    if (!File.Exists(RegistryFilePath))
    {
      return CreateDefaultRegistry();
    }

    try
    {
      string json = await File.ReadAllTextAsync(RegistryFilePath);
      return JsonSerializer.Deserialize<McpRegistry>(json, JsonOptions) ?? CreateDefaultRegistry();
    }
    catch (JsonException)
    {
      // If the file is corrupted, return a fresh registry
      return CreateDefaultRegistry();
    }
  }

  public async Task SaveAsync(McpRegistry registry)
  {
    string? directory = Path.GetDirectoryName(RegistryFilePath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }

    string json = JsonSerializer.Serialize(registry, JsonOptions);
    await File.WriteAllTextAsync(RegistryFilePath, json);
  }

  private static string GetDefaultRegistryPath()
  {
    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    return Path.Combine(appData, "McpManager", "mcp-registry.json");
  }

  private static McpRegistry CreateDefaultRegistry()
  {
    return new McpRegistry();
  }

  public static string GetDefaultCodexConfigPath()
  {
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".codex", "config.toml");
  }

  public static string GetDefaultClaudeDesktopConfigPath()
  {
    if (OperatingSystem.IsMacOS())
    {
      string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      return Path.Combine(home, "Library", "Application Support", "Claude", "claude_desktop_config.json");
    }
    else if (OperatingSystem.IsWindows())
    {
      string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
      return Path.Combine(appData, "Claude", "claude_desktop_config.json");
    }
    else
    {
      string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      return Path.Combine(home, ".config", "claude", "claude_desktop_config.json");
    }
  }

  public static string GetDefaultCursorConfigPath()
  {
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return home;
  }

  public static string GetDefaultWindsurfConfigPath()
  {
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".codeium", "windsurf");
  }

  public static string GetDefaultVsCodeConfigPath()
  {
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return home;
  }

  public static string GetDefaultOpenCodeConfigPath()
  {
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".config", "opencode");
  }

  public static string GetDefaultClaudeCodeGlobalConfigPath()
  {
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".claude.json");
  }
}