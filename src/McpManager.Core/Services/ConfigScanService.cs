using McpManager.Core.Models;

namespace McpManager.Core.Services;

public class ConfigScanService(
  IConfigExportService exportService,
  Func<GlobalSettings, IReadOnlyList<(TargetClientFlags Client, string Path)>>? globalTargets = null,
  string? homeDirectory = null)
{
  public const int MaxDepth = 4;

  private static readonly string[] SkippedNames = ["node_modules", "bin", "obj"];

  private static readonly (TargetClientFlags Client, string[] Files)[] ProjectHarnesses =
  [
    (TargetClientFlags.ClaudeCode, [".mcp.json"]),
    (TargetClientFlags.OpenCode, [Path.Combine(".opencode", "opencode.json"), "opencode.jsonc", "opencode.json"]),
    (TargetClientFlags.Codex, [Path.Combine(".codex", "config.toml")]),
    (TargetClientFlags.Cursor, [Path.Combine(".cursor", "mcp.json")]),
    (TargetClientFlags.VsCode, [Path.Combine(".vscode", "mcp.json")]),
  ];

  private readonly TargetConfigSelectionService _selectionService = new(exportService);

  public async Task<IReadOnlyList<ScannedFolder>> ScanAsync(
    IEnumerable<string> roots, GlobalSettings settings, CancellationToken cancellationToken = default)
  {
    Dictionary<string, ScannedFolder> folders = new(StringComparer.Ordinal);
    IReadOnlyList<(TargetClientFlags Client, string Path)> globals = (globalTargets ?? GetDefaultGlobalTargets)(settings);
    HashSet<string> globalFiles = new(
      globals.SelectMany(g => ConfigFiles(new TargetFolder { Path = g.Path, EnabledClients = g.Client, IsGlobal = true }, settings)),
      StringComparer.OrdinalIgnoreCase);
    string home = Path.TrimEndingDirectorySeparator(
      Path.GetFullPath(homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    foreach (string root in roots)
    {
      Stack<(string Directory, int Depth)> pending = new();
      pending.Push((Path.GetFullPath(root), 0));
      while (pending.Count > 0)
      {
        cancellationToken.ThrowIfCancellationRequested();
        (string directory, int depth) = pending.Pop();
        if (!folders.ContainsKey(directory) &&
            !string.Equals(Path.TrimEndingDirectorySeparator(directory), home, StringComparison.OrdinalIgnoreCase))
        {
          TargetClientFlags clients = DetectClients(directory);
          TargetFolder target = new() { Path = directory, EnabledClients = clients };
          if (clients != TargetClientFlags.None && !ConfigFiles(target, settings).Any(globalFiles.Contains))
          {
            folders[directory] = await ReadAsync(target, settings, isGlobal: false).ConfigureAwait(false);
          }
        }

        if (depth < MaxDepth)
        {
          foreach (string child in GetChildDirectories(directory))
          {
            pending.Push((child, depth + 1));
          }
        }
      }
    }

    List<ScannedFolder> results = folders.Values.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
    foreach ((TargetClientFlags client, string path) in globals)
    {
      cancellationToken.ThrowIfCancellationRequested();
      TargetFolder target = new() { Path = path, EnabledClients = client, IsGlobal = true };
      if (exportService.GetConfigFilePaths(target, settings).Values.Any(File.Exists))
      {
        results.Add(await ReadAsync(target, settings, isGlobal: true).ConfigureAwait(false));
      }
    }

    return results;
  }

  public Task<ScannedFolder> ReadFolderAsync(string path, GlobalSettings settings) =>
    ReadAsync(new TargetFolder { Path = path, EnabledClients = DetectClients(path) }, settings, isGlobal: false);

  public Task<ScannedFolder> ReadPlaceAsync(TargetFolder place, GlobalSettings settings) =>
    ReadAsync(place, settings, place.IsGlobal);

  private async Task<ScannedFolder> ReadAsync(TargetFolder target, GlobalSettings settings, bool isGlobal)
  {
    ExistingTargetServers existing = await _selectionService.ReadAsync(target, settings).ConfigureAwait(false);
    List<HashSet<string>> nameSets = exportService.GetConfigFilePaths(target, settings).Values
      .Where(path => File.Exists(path) && !existing.UnreadableFiles.Contains(path))
      .Select(path => existing.Servers.Where(s => s.FilePath == path).Select(s => s.Name).ToHashSet(StringComparer.Ordinal))
      .ToList();
    bool differ = nameSets.Count >= 2 && nameSets.Skip(1).Any(set => !set.SetEquals(nameSets[0]));
    return new ScannedFolder(
      target.Path, isGlobal, target.EnabledClients, existing.Servers, existing.UnreadableFiles, differ);
  }

  private IEnumerable<string> ConfigFiles(TargetFolder target, GlobalSettings settings) =>
    exportService.GetConfigFilePaths(target, settings).Values.Select(Path.GetFullPath);

  private static TargetClientFlags DetectClients(string directory)
  {
    TargetClientFlags clients = TargetClientFlags.None;
    foreach ((TargetClientFlags client, string[] files) in ProjectHarnesses)
    {
      if (files.Any(file => File.Exists(Path.Combine(directory, file))))
      {
        clients |= client;
      }
    }

    return clients;
  }

  private static List<string> GetChildDirectories(string directory)
  {
    List<string> children = [];
    try
    {
      foreach (string child in Directory.EnumerateDirectories(directory))
      {
        string name = Path.GetFileName(child);
        if (name.StartsWith('.') || SkippedNames.Contains(name, StringComparer.Ordinal))
        {
          continue;
        }

        if (new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
          continue;
        }

        children.Add(child);
      }
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
    {
    }

    return children;
  }

  private static IReadOnlyList<(TargetClientFlags Client, string Path)> GetDefaultGlobalTargets(
    GlobalSettings settings) =>
  [
    (TargetClientFlags.ClaudeCodeGlobal,
      Path.GetDirectoryName(RegistryService.GetDefaultClaudeCodeGlobalConfigPath()) ?? ""),
    (TargetClientFlags.Codex,
      Path.GetDirectoryName(settings.CodexConfigPath ?? RegistryService.GetDefaultCodexConfigPath()) ?? ""),
    (TargetClientFlags.OpenCode, RegistryService.GetDefaultOpenCodeConfigPath()),
    (TargetClientFlags.ClaudeDesktop,
      Path.GetDirectoryName(settings.ClaudeDesktopConfigPath ?? RegistryService.GetDefaultClaudeDesktopConfigPath())
      ?? ""),
  ];
}
