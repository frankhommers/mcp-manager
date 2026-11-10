using System.Text;
using System.Text.Json;
using McpManager.Core.Models;
using Tomlyn;

namespace McpManager.Core.Services;

public class ScanImportPlanner(IConfigImportService importService, IConfigExportService exportService)
{
  public virtual IReadOnlyList<ScanServerMatch> Match(IEnumerable<ScannedFolder> folders, McpRegistry registry,
    ICollection<string>? skipped = null)
  {
    List<ScanServerMatch> matches = [];
    Dictionary<string, McpServer> firstUnknown = new(StringComparer.Ordinal);

    foreach (ScannedFolder folder in folders)
    {
      foreach (ExistingTargetServer found in folder.Servers.OrderBy(s => (int)s.Client))
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

        McpServer? known = FindInLibrary(registry.Servers, found) ?? firstUnknown.GetValueOrDefault(found.Name);
        if (known == null)
        {
          firstUnknown[found.Name] = imported;
          matches.Add(new ScanServerMatch(folder.Path, found, imported, null, ScanMatchKind.Unknown, null));
          continue;
        }

        ServerDiff diff = ServerDiffService.Compare(Render(registry, folder, found, known) ?? known, imported);
        ScanMatchKind kind = diff.HasDifferences ? ScanMatchKind.Differs : ScanMatchKind.Known;
        bool bridged = found.Client == TargetClientFlags.ClaudeDesktop && known.TransportType != McpTransportType.Stdio;
        matches.Add(new ScanServerMatch(folder.Path, found, imported, known, kind, diff, !bridged));
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

    Dictionary<McpServer, McpServer> replacements = ValidateConflicts(registry, plan);

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
        if (replacements.Remove(entry.Target, out McpServer? replacement))
        {
          CopyDefinition(replacement, server);
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

  private McpServer? Render(McpRegistry registry, ScannedFolder folder, ExistingTargetServer found, McpServer server)
  {
    try
    {
      TargetFolder? place = FindPlace(registry, folder);
      TargetFolder? effective = place == null ? null : ProfileResolver.Resolve(place, registry.Profiles);
      TargetFolder target = new()
      {
        Path = folder.Path,
        IsGlobal = folder.IsGlobal,
        EnabledClients = found.Client,
        EnabledServers = [server.Id],
        ServerEnvOverrides = effective?.ServerEnvOverrides ?? [],
        ServerToolOverrides = effective?.ServerToolOverrides ?? [],
        BridgeArgs = effective?.BridgeArgs ?? string.Empty,
        AdoptedServerNames = [server.Name],
      };
      string path = exportService.GetConfigFilePaths(target, registry.Settings)[found.Client];
      string content = exportService.PreviewConfigs(target, [server], registry.Settings)[path];
      return importService.ImportServer(
        TargetConfigSelectionService.Parse(content, found.Client, path).Single(s => s.Name == server.Name));
    }
    catch (Exception ex) when (ex is InvalidDataException or JsonException or TomlException
                                 or InvalidOperationException or KeyNotFoundException or ArgumentException
                                 or IOException or UnauthorizedAccessException)
    {
      return null;
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

      if (choice == ScanChoice.UseFoundVersion && !match.CanUseFoundVersion)
      {
        throw new ArgumentException(
          $"Server '{match.Found.Name}' in '{folder.Path}' is wrapped by a bridge for Claude Desktop. " +
          "Keep the library version or import it as a separate server.", nameof(choices));
      }
    }

    McpServer target = choice == ScanChoice.ImportSeparately ? match.Imported : match.Library;
    return new PlannedServer(folder, match, choice, target);
  }

  private Dictionary<McpServer, McpServer> ValidateConflicts(McpRegistry registry, List<PlannedServer> plan)
  {
    Dictionary<McpServer, McpServer> replacements = [];
    foreach (IGrouping<McpServer, PlannedServer> group in plan
               .Where(p => p.Choice != ScanChoice.ImportSeparately)
               .GroupBy(p => p.Target))
    {
      List<PlannedServer> overwrites = group.Where(p => p.Choice == ScanChoice.UseFoundVersion).ToList();
      if (overwrites.Count == 0)
      {
        continue;
      }

      string name = group.Key.Name;
      McpServer replacement = WithFoundChanges(group.Key, overwrites[0].Match);
      foreach (PlannedServer other in overwrites.Skip(1))
      {
        if (!SameDefinition(replacement, WithFoundChanges(group.Key, other.Match)))
        {
          throw new ArgumentException(
            $"'{Describe(overwrites[0])}' and '{Describe(other)}' both want to replace server " +
            $"'{name}' with different definitions.", nameof(plan));
        }
      }

      foreach (PlannedServer adopter in group)
      {
        McpServer rendered = Render(registry, adopter.Folder, adopter.Match.Found, replacement) ?? replacement;
        if (ServerDiffService.Compare(rendered, adopter.Match.Imported).Fields
            .Any(f => f.Kind != DiffKind.Same && f.Field != ServerDiffService.ToolsField))
        {
          throw new ArgumentException(
            $"Server '{name}' in '{Describe(adopter)}' would be changed by the version from " +
            $"'{Describe(overwrites[0])}'.", nameof(plan));
        }
      }

      replacements[group.Key] = replacement;
    }

    return replacements;
  }

  private static string Describe(PlannedServer entry) =>
    $"{entry.Folder.Path} ({Path.GetFileName(entry.Match.Found.FilePath)})";

  private static McpServer WithFoundChanges(McpServer library, ScanServerMatch match)
  {
    McpServer result = new() { Name = library.Name, AlwaysAllow = library.AlwaysAllow };
    CopyDefinition(library, result);
    McpServer found = match.Imported;
    foreach (FieldDiff field in match.Diff!.Fields.Where(f => f.Kind != DiffKind.Same))
    {
      switch (field.Field)
      {
        case ServerDiffService.TypeField:
          result.TransportType = found.TransportType;
          break;
        case ServerDiffService.CommandField:
          result.Command = found.Command;
          break;
        case ServerDiffService.ArgsField:
          result.Args = [.. found.Args];
          break;
        case ServerDiffService.UrlField:
          result.Url = found.Url;
          break;
        case ServerDiffService.CwdField:
          result.WorkingDirectory = found.WorkingDirectory;
          break;
        case string env when env.StartsWith(ServerDiffService.EnvPrefix, StringComparison.Ordinal):
          CopyValue(found.EnvironmentVariables, result.EnvironmentVariables,
            env[ServerDiffService.EnvPrefix.Length..], StringComparer.Ordinal);
          break;
        case string header when header.StartsWith(ServerDiffService.HeaderPrefix, StringComparison.Ordinal):
          CopyValue(found.HttpHeaders, result.HttpHeaders,
            header[ServerDiffService.HeaderPrefix.Length..], StringComparer.OrdinalIgnoreCase);
          break;
      }
    }

    return result;
  }

  private static void CopyValue(
    Dictionary<string, string> from, Dictionary<string, string> to, string key, StringComparer comparer)
  {
    foreach (string existing in to.Keys.Where(k => comparer.Equals(k, key)).ToList())
    {
      to.Remove(existing);
    }

    foreach ((string foundKey, string value) in from.Where(p => comparer.Equals(p.Key, key)))
    {
      to[foundKey] = value;
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
      : !t.IsGlobal && ProjectFolderRules.PathsEqual(t.Path, folder.Path));

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
