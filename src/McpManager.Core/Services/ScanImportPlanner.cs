using McpManager.Core.Models;

namespace McpManager.Core.Services;

public class ScanImportPlanner(IConfigImportService importService)
{
  public IReadOnlyList<ScanServerMatch> Match(IEnumerable<ScannedFolder> folders, IReadOnlyList<McpServer> library)
  {
    List<ScanServerMatch> matches = [];
    Dictionary<string, McpServer> firstUnknown = new(StringComparer.Ordinal);

    foreach (ScannedFolder folder in folders)
    {
      IEnumerable<ExistingTargetServer> distinct = folder.Servers
        .OrderBy(s => (int)s.Client)
        .GroupBy(s => s.Name, StringComparer.Ordinal)
        .Select(g => g.First());

      foreach (ExistingTargetServer found in distinct)
      {
        McpServer imported = importService.ImportServer(found);
        McpServer? known = FindInLibrary(library, found);
        if (known == null && firstUnknown.TryGetValue(found.Name, out McpServer? first))
        {
          known = first;
        }

        if (known == null)
        {
          firstUnknown[found.Name] = imported;
          matches.Add(new ScanServerMatch(folder.Path, found, imported, null, ScanMatchKind.Unknown, null));
          continue;
        }

        ServerDiff diff = ServerDiffService.Compare(known, imported);
        ScanMatchKind kind = diff.HasDifferences ? ScanMatchKind.Differs : ScanMatchKind.Known;
        matches.Add(new ScanServerMatch(folder.Path, found, imported, known, kind, diff));
      }
    }

    return matches;
  }

  public void Apply(
    McpRegistry registry,
    IEnumerable<ScannedFolder> selected,
    IReadOnlyList<ScanServerMatch> matches,
    IReadOnlyDictionary<ScanServerMatch, ScanChoice> choices)
  {
    List<ScannedFolder> folders = selected.ToList();
    foreach (ScannedFolder folder in folders)
    {
      foreach (ScanServerMatch match in FolderMatches(folder, matches))
      {
        if (match.Kind == ScanMatchKind.Differs && !choices.ContainsKey(match))
        {
          throw new ArgumentException(
            $"No choice was made for server '{match.Found.Name}' in '{folder.Path}'.", nameof(choices));
        }
      }
    }

    foreach (ScannedFolder folder in folders)
    {
      string placeName = PlaceName(folder);
      TargetFolder place = FindPlace(registry, folder) ?? AddPlace(registry, folder, placeName);
      place.EnabledClients |= folder.Clients;

      foreach (ScanServerMatch match in FolderMatches(folder, matches))
      {
        McpServer server = Resolve(registry, match, choices, placeName);
        if (!match.Found.IsEnabled)
        {
          continue;
        }

        place.EnabledServers.Add(server.Id);
        place.DisabledServers.Remove(server.Id);
        place.AdoptedServerNames.Add(match.Found.Name);
      }
    }
  }

  private static IEnumerable<ScanServerMatch> FolderMatches(ScannedFolder folder, IReadOnlyList<ScanServerMatch> matches) =>
    matches.Where(m => string.Equals(m.FolderPath, folder.Path, StringComparison.Ordinal));

  private static McpServer? FindInLibrary(IReadOnlyList<McpServer> library, ExistingTargetServer found)
  {
    if (found.ManagedId is Guid id)
    {
      McpServer? byId = library.FirstOrDefault(s => s.Id == id);
      if (byId != null)
      {
        return byId;
      }
    }

    return library.FirstOrDefault(s => string.Equals(s.Name, found.Name, StringComparison.Ordinal));
  }

  private McpServer Resolve(
    McpRegistry registry,
    ScanServerMatch match,
    IReadOnlyDictionary<ScanServerMatch, ScanChoice> choices,
    string placeName)
  {
    if (match.Library == null)
    {
      return EnsureInLibrary(registry, match.Imported);
    }

    ScanChoice choice = match.Kind == ScanMatchKind.Differs ? choices[match] : ScanChoice.KeepLibrary;
    switch (choice)
    {
      case ScanChoice.ImportSeparately:
        match.Imported.Name = $"{match.Found.Name}-{placeName}";
        return EnsureInLibrary(registry, match.Imported);
      case ScanChoice.UseFoundVersion:
        McpServer target = EnsureInLibrary(registry, match.Library);
        CopyDefinition(match.Imported, target);
        return target;
      default:
        return EnsureInLibrary(registry, match.Library);
    }
  }

  private static McpServer EnsureInLibrary(McpRegistry registry, McpServer server)
  {
    if (registry.Servers.Any(s => ReferenceEquals(s, server)))
    {
      return server;
    }

    McpServer? sameId = registry.Servers.FirstOrDefault(s => s.Id == server.Id);
    if (sameId != null && string.Equals(sameId.Name, server.Name, StringComparison.Ordinal))
    {
      return sameId;
    }

    if (server.Id == Guid.Empty || sameId != null)
    {
      server.Id = Guid.NewGuid();
    }

    string baseName = server.Name;
    int suffix = 2;
    while (registry.Servers.Any(s => string.Equals(s.Name, server.Name, StringComparison.Ordinal)))
    {
      server.Name = $"{baseName}-{suffix++}";
    }

    registry.Servers.Add(server);
    return server;
  }

  private static void CopyDefinition(McpServer from, McpServer to)
  {
    to.TransportType = from.TransportType;
    to.Command = from.Command;
    to.Args = [.. from.Args];
    to.WorkingDirectory = from.WorkingDirectory;
    to.Url = from.Url;
    to.EnvironmentVariables = new Dictionary<string, string>(from.EnvironmentVariables);
    to.HttpHeaders = new Dictionary<string, string>(from.HttpHeaders);
  }

  private static TargetFolder? FindPlace(McpRegistry registry, ScannedFolder folder) =>
    registry.TargetFolders.FirstOrDefault(t => folder.IsGlobal
      ? t.IsGlobal && t.EnabledClients == folder.Clients
      : !t.IsGlobal && string.Equals(t.Path, folder.Path, StringComparison.OrdinalIgnoreCase));

  private static TargetFolder AddPlace(McpRegistry registry, ScannedFolder folder, string name)
  {
    TargetFolder place = new() { Name = name, Path = folder.Path, IsGlobal = folder.IsGlobal, EnabledClients = TargetClientFlags.None };
    registry.TargetFolders.Add(place);
    return place;
  }

  private static string PlaceName(ScannedFolder folder)
  {
    if (folder.IsGlobal)
    {
      return folder.Clients switch
      {
        TargetClientFlags.ClaudeCode or TargetClientFlags.ClaudeCodeGlobal => "Claude Code",
        TargetClientFlags.ClaudeDesktop => "Claude Desktop",
        TargetClientFlags.Codex => "Codex",
        TargetClientFlags.OpenCode => "OpenCode",
        TargetClientFlags.Cursor => "Cursor",
        TargetClientFlags.Windsurf => "Windsurf",
        TargetClientFlags.VsCode => "VS Code",
        _ => folder.Clients.ToString(),
      };
    }

    string trimmed = folder.Path.TrimEnd('/', '\\');
    string name = trimmed[(trimmed.LastIndexOfAny(['/', '\\']) + 1)..];
    return name.Length > 0 ? name : folder.Path;
  }
}
