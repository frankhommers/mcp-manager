using System.Text;
using System.Text.Json;
using McpManager.Core.Models;
using Tomlyn;

namespace McpManager.Core.Services;

public class ScanImportPlanner(IConfigImportService importService)
{
  public IReadOnlyList<ScanServerMatch> Match(IEnumerable<ScannedFolder> folders, IReadOnlyList<McpServer> library,
    ICollection<string>? skipped = null)
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
        McpServer imported;
        try
        {
          imported = importService.ImportServer(found);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or TomlException)
        {
          skipped?.Add($"{folder.Path}: {found.Name}: {ex.Message}");
          continue;
        }

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
    List<PlannedServer> plan = [];
    foreach (ScannedFolder folder in folders)
    {
      foreach (ScanServerMatch match in FolderMatches(folder, matches))
      {
        plan.Add(Plan(folder, match, choices));
      }
    }

    ValidateConflicts(plan);

    foreach (ScannedFolder folder in folders)
    {
      string placeName = PlaceName(folder);
      TargetFolder place = FindPlace(registry, folder) ?? AddPlace(registry, folder, placeName);
      place.EnabledClients |= folder.Clients;

      foreach (PlannedServer entry in plan.Where(p => ReferenceEquals(p.Folder, folder)))
      {
        if (entry.Choice == ScanChoice.ImportSeparately)
        {
          entry.Target.Name = $"{entry.Match.Found.Name}-{Slug(placeName)}";
        }

        McpServer server = EnsureInLibrary(registry, entry.Target);
        if (entry.Choice == ScanChoice.UseFoundVersion)
        {
          CopyDefinition(entry.Match.Imported, server);
        }

        if (!entry.Match.Found.IsEnabled)
        {
          continue;
        }

        place.EnabledServers.Add(server.Id);
        place.DisabledServers.Remove(server.Id);
        place.AdoptedServerNames.Add(entry.Match.Found.Name);
      }
    }
  }

  private static PlannedServer Plan(
    ScannedFolder folder,
    ScanServerMatch match,
    IReadOnlyDictionary<ScanServerMatch, ScanChoice> choices)
  {
    if (match.Library == null)
    {
      return new PlannedServer(folder, match, ScanChoice.KeepLibrary, match.Imported);
    }

    ScanChoice choice = ScanChoice.KeepLibrary;
    if (match.Kind == ScanMatchKind.Differs)
    {
      if (!choices.TryGetValue(match, out choice))
      {
        throw new ArgumentException(
          $"No choice was made for server '{match.Found.Name}' in '{folder.Path}'.", nameof(choices));
      }

      if (!Enum.IsDefined(choice))
      {
        throw new ArgumentException(
          $"Invalid choice for server '{match.Found.Name}' in '{folder.Path}'.", nameof(choices));
      }
    }

    McpServer target = choice == ScanChoice.ImportSeparately ? match.Imported : match.Library;
    return new PlannedServer(folder, match, choice, target);
  }

  private static void ValidateConflicts(List<PlannedServer> plan)
  {
    foreach (IGrouping<McpServer, PlannedServer> group in plan
               .Where(p => p.Choice != ScanChoice.ImportSeparately)
               .GroupBy(p => p.Target))
    {
      List<PlannedServer> overwrites = group.Where(p => p.Choice == ScanChoice.UseFoundVersion).ToList();
      string name = group.First().Match.Found.Name;
      foreach (PlannedServer other in overwrites.Skip(1))
      {
        if (!SameDefinition(overwrites[0].Match.Imported, other.Match.Imported))
        {
          throw new ArgumentException(
            $"Folders '{overwrites[0].Folder.Path}' and '{other.Folder.Path}' both want to replace server " +
            $"'{name}' with different definitions.", nameof(plan));
        }
      }

      if (overwrites.Count == 0)
      {
        continue;
      }

      McpServer final = overwrites[0].Match.Imported;
      foreach (PlannedServer adopter in group)
      {
        if (!SameDefinition(final, adopter.Match.Imported))
        {
          throw new ArgumentException(
            $"Server '{name}' in '{adopter.Folder.Path}' would be changed by the version from '{overwrites[0].Folder.Path}'.",
            nameof(plan));
        }
      }
    }
  }

  private static bool SameDefinition(McpServer left, McpServer right)
  {
    McpServer normalized = new()
    {
      Name = right.Name,
      AlwaysAllow = left.AlwaysAllow,
    };
    CopyDefinition(right, normalized);
    return !ServerDiffService.Compare(left, normalized).HasDifferences;
  }

  private static string Slug(string value)
  {
    StringBuilder builder = new();
    foreach (char c in value.ToLowerInvariant())
    {
      char mapped = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' ? c : '-';
      if (mapped != '-' || (builder.Length > 0 && builder[^1] != '-'))
      {
        builder.Append(mapped);
      }
    }

    return builder.ToString().Trim('-');
  }

  private sealed record PlannedServer(ScannedFolder Folder, ScanServerMatch Match, ScanChoice Choice, McpServer Target);

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

  public static TargetFolder? FindPlace(McpRegistry registry, ScannedFolder folder) =>
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
