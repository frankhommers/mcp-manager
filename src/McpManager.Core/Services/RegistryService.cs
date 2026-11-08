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

  private const int CurrentFormatVersion = 2;
  private const string LegacyRegistryFileName = "mcp-registry.json";

  public string RegistryFilePath { get; }

  public RegistryService(string? customPath = null)
  {
    RegistryFilePath = customPath ?? GetDefaultRegistryPath();
  }

  public async Task<McpRegistry> LoadAsync()
  {
    if (!File.Exists(RegistryFilePath))
    {
      return await MigrateLegacyRegistryAsync().ConfigureAwait(false) ?? CreateDefaultRegistry();
    }

    string json = await File.ReadAllTextAsync(RegistryFilePath);
    using JsonDocument document = JsonDocument.Parse(json);
    if (!document.RootElement.TryGetProperty("formatVersion", out JsonElement version) || version.GetInt32() != CurrentFormatVersion)
    {
      throw new InvalidDataException("Unsupported registry format. Configure a new workspace.");
    }
    return JsonSerializer.Deserialize<McpRegistry>(json, JsonOptions)
      ?? throw new InvalidDataException("The registry is empty.");
  }

  public async Task SaveAsync(McpRegistry registry)
  {
    string? directory = Path.GetDirectoryName(RegistryFilePath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }

    string json = JsonSerializer.Serialize(registry, JsonOptions);
    string temporaryPath = RegistryFilePath + ".tmp";
    await File.WriteAllTextAsync(temporaryPath, json);
    if (!OperatingSystem.IsWindows())
      File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    File.Move(temporaryPath, RegistryFilePath, true);
  }

  private async Task<McpRegistry?> MigrateLegacyRegistryAsync()
  {
    string legacyPath = Path.Combine(Path.GetDirectoryName(RegistryFilePath) ?? string.Empty, LegacyRegistryFileName);
    if (!File.Exists(legacyPath))
    {
      return null;
    }

    string json = await File.ReadAllTextAsync(legacyPath).ConfigureAwait(false);
    McpRegistry registry = JsonSerializer.Deserialize<McpRegistry>(json, JsonOptions)
      ?? throw new InvalidDataException("The legacy registry is empty.");
    registry.FormatVersion = CurrentFormatVersion;
    BridgeCommandMigration.Apply(registry.Settings);
    await SaveAsync(registry).ConfigureAwait(false);
    return registry;
  }

  private static string GetDefaultRegistryPath()
  {
    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    return Path.Combine(appData, "McpManager", "workspace.json");
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