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
    McpRegistry registry = new();

    // Set default Claude Desktop config path based on OS
    string claudeDesktopConfigPath = GetDefaultClaudeDesktopConfigPath();
    registry.Settings.ClaudeDesktopConfigPath = claudeDesktopConfigPath;

    // Add Claude Desktop as a default target
    string claudeDesktopPath = Path.GetDirectoryName(claudeDesktopConfigPath) ?? string.Empty;
    registry.TargetFolders.Add(
      new TargetFolder
      {
        Name = "Claude Desktop",
        Path = claudeDesktopPath,
        EnabledClients = TargetClientFlags.ClaudeDesktop,
        IsGlobal = true,
      });

    // Set default Codex config path and add as global target
    string codexConfigPath = GetDefaultCodexConfigPath();
    registry.Settings.CodexConfigPath = codexConfigPath;
    string codexPath = Path.GetDirectoryName(codexConfigPath) ?? string.Empty;
    registry.TargetFolders.Add(
      new TargetFolder
      {
        Name = "Codex CLI",
        Path = codexPath,
        EnabledClients = TargetClientFlags.Codex,
        IsGlobal = true,
      });

    return registry;
  }

  private static string GetDefaultCodexConfigPath()
  {
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".codex", "config.toml");
  }

  private static string GetDefaultClaudeDesktopConfigPath()
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
      // Linux
      string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      return Path.Combine(home, ".config", "claude", "claude_desktop_config.json");
    }
  }
}