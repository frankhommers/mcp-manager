using System.Text.Json.Nodes;
using McpManager.Core.ConfigGenerators;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class ScanImportPlannerTests
{
  private readonly ScanImportPlanner _planner = new(new ConfigImportService(), new ConfigExportService());

  private static ExistingTargetServer Found(
    string name,
    string command,
    string? secret = null,
    Guid? managedId = null,
    bool enabled = true,
    TargetClientFlags client = TargetClientFlags.ClaudeCode)
  {
    if (client == TargetClientFlags.Codex)
    {
      string toml = $"[server]\ncommand = \"{command}\"\n";
      return new ExistingTargetServer(name, managedId, enabled, client, "/x/config.toml", toml);
    }

    JsonObject config = new() { ["command"] = command };
    if (secret != null)
    {
      config["env"] = new JsonObject { ["TOKEN"] = secret };
    }

    return new ExistingTargetServer(name, managedId, enabled, client, "/x/.mcp.json", config.ToJsonString());
  }

  private static ScannedFolder Folder(
    string path,
    TargetClientFlags clients,
    bool isGlobal,
    params ExistingTargetServer[] servers) =>
    new(path, isGlobal, clients, servers, [], false);

  private static McpServer Library(string name, string command, string? secret = null)
  {
    McpServer server = new() { Name = name, DisplayName = name, Command = command };
    if (secret != null)
    {
      server.EnvironmentVariables["TOKEN"] = secret;
    }

    return server;
  }

  private static ExistingTargetServer Deployed(
    TargetClientFlags client,
    McpServer server,
    string folder = "/p/a",
    Dictionary<Guid, Dictionary<string, string>>? envOverrides = null)
  {
    (IConfigGenerator generator, string file) = client switch
    {
      TargetClientFlags.OpenCode => ((IConfigGenerator)new OpenCodeConfigGenerator(), "opencode.json"),
      TargetClientFlags.Codex => (new CodexConfigGenerator(), Path.Combine(".codex", "config.toml")),
      TargetClientFlags.ClaudeDesktop => (new ClaudeDesktopConfigGenerator(), "claude_desktop_config.json"),
      _ => (new ClaudeCodeConfigGenerator(), ".mcp.json"),
    };
    string path = Path.Combine(folder, file);
    return TargetConfigSelectionService.Parse(generator.GenerateConfig([server], envOverrides), client, path).Single();
  }

  private static McpServer Http(string token) => new()
  {
    Name = "api",
    TransportType = McpTransportType.Http,
    Url = "https://example.com/mcp",
    HttpHeaders = { ["Authorization"] = $"Bearer {token}" },
  };

  [Theory]
  [InlineData(TargetClientFlags.ClaudeCode)]
  [InlineData(TargetClientFlags.OpenCode)]
  [InlineData(TargetClientFlags.Codex)]
  [InlineData(TargetClientFlags.ClaudeDesktop)]
  public void Own_output_for_http_server_is_known(TargetClientFlags client)
  {
    McpServer library = Http("abc");
    McpRegistry registry = new() { Servers = [library] };
    ScannedFolder folder = Folder("/p/a", client, client == TargetClientFlags.ClaudeDesktop, Deployed(client, library));

    ScanServerMatch match = Assert.Single(_planner.Match([folder], registry));

    Assert.Equal(ScanMatchKind.Known, match.Kind);
    Assert.False(match.Diff!.HasDifferences);
  }

  [Fact]
  public void Env_override_of_existing_place_is_part_of_the_comparison()
  {
    McpServer library = Library("tool", "run", "library-token");
    TargetFolder place = new()
    {
      Name = "a",
      Path = "/p/a",
      EnabledServers = [library.Id],
      ServerEnvOverrides = { [library.Id] = new Dictionary<string, string> { ["TOKEN"] = "place-token" } },
    };
    McpRegistry registry = new() { Servers = [library], TargetFolders = [place] };
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.ClaudeCode, false,
      Deployed(TargetClientFlags.ClaudeCode, library, envOverrides: place.ServerEnvOverrides));

    Assert.Equal(ScanMatchKind.Known, Assert.Single(_planner.Match([folder], registry)).Kind);
  }

  [Fact]
  public void Found_version_from_opencode_changes_only_what_differs()
  {
    McpServer library = Http("abc");
    McpRegistry registry = new() { Servers = [library] };
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.OpenCode, false, Deployed(TargetClientFlags.OpenCode, Http("xyz")));

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], registry);
    Assert.Equal(["header:Authorization"],
      matches[0].Diff!.Fields.Where(f => f.Kind != DiffKind.Same).Select(f => f.Field));
    _planner.Apply(registry, [folder], matches, new Dictionary<ScanServerMatch, ScanChoice>
    {
      [matches[0]] = ScanChoice.UseFoundVersion,
    });

    Assert.Equal(McpTransportType.Http, library.TransportType);
    Assert.Equal("Bearer xyz", library.HttpHeaders["Authorization"]);
  }

  [Fact]
  public void Bridged_claude_desktop_entry_cannot_replace_the_library()
  {
    McpServer library = Http("abc");
    McpRegistry registry = new() { Servers = [library] };
    ScannedFolder folder = Folder("/desktop", TargetClientFlags.ClaudeDesktop, true,
      Deployed(TargetClientFlags.ClaudeDesktop, Http("xyz"), "/desktop"));

    ScanServerMatch match = Assert.Single(_planner.Match([folder], registry));

    Assert.Equal(ScanMatchKind.Differs, match.Kind);
    Assert.False(match.CanUseFoundVersion);
    Assert.Throws<ArgumentException>(() => _planner.Apply(registry, [folder], [match],
      new Dictionary<ScanServerMatch, ScanChoice> { [match] = ScanChoice.UseFoundVersion }));
    Assert.Empty(registry.TargetFolders);
    Assert.Equal("Bearer abc", library.HttpHeaders["Authorization"]);

    _planner.Apply(registry, [folder], [match],
      new Dictionary<ScanServerMatch, ScanChoice> { [match] = ScanChoice.ImportSeparately });
    McpServer separate = registry.Servers[1];
    Assert.Equal("api-claude-desktop", separate.Name);
    Assert.Equal(McpTransportType.Stdio, separate.TransportType);
    Assert.Contains("Bearer xyz", separate.Args);
  }

  [Fact]
  public void Every_harness_entry_of_a_name_is_compared()
  {
    McpServer library = Library("tool", "run", "A");
    McpRegistry registry = new() { Servers = [library] };
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.ClaudeCode | TargetClientFlags.OpenCode, false,
      Deployed(TargetClientFlags.ClaudeCode, Library("tool", "run", "A")),
      Deployed(TargetClientFlags.OpenCode, Library("tool", "run", "B")));

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], registry);

    Assert.Equal([ScanMatchKind.Known, ScanMatchKind.Differs], matches.Select(m => m.Kind));
    Assert.EndsWith("opencode.json", matches[1].Found.FilePath);
    Assert.Throws<ArgumentException>(() =>
      _planner.Apply(registry, [folder], matches, new Dictionary<ScanServerMatch, ScanChoice>()));
    ArgumentException conflict = Assert.Throws<ArgumentException>(() => _planner.Apply(registry, [folder], matches,
      new Dictionary<ScanServerMatch, ScanChoice> { [matches[1]] = ScanChoice.UseFoundVersion }));
    Assert.Contains(".mcp.json", conflict.Message);
    Assert.Empty(registry.TargetFolders);

    _planner.Apply(registry, [folder], matches,
      new Dictionary<ScanServerMatch, ScanChoice> { [matches[1]] = ScanChoice.KeepLibrary });
    Assert.Equal("A", library.EnvironmentVariables["TOKEN"]);
    Assert.Contains(library.Id, Assert.Single(registry.TargetFolders).EnabledServers);
  }

  [Fact]
  public void Second_harness_entry_of_an_unknown_name_is_compared_with_the_first()
  {
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.ClaudeCode | TargetClientFlags.OpenCode, false,
      Deployed(TargetClientFlags.ClaudeCode, Library("tool", "run", "A")),
      Deployed(TargetClientFlags.OpenCode, Library("tool", "run", "B")));

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], new McpRegistry());

    Assert.Equal([ScanMatchKind.Unknown, ScanMatchKind.Differs], matches.Select(m => m.Kind));
  }

  [Fact]
  public void Unknown_server_is_imported_once_across_folders()
  {
    ScannedFolder a = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "A"));
    ScannedFolder b = Folder("/p/b", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "A"));
    McpRegistry registry = new();

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([a, b], registry);
    Assert.Equal(ScanMatchKind.Unknown, matches[0].Kind);
    Assert.Equal(ScanMatchKind.Known, matches[1].Kind);

    _planner.Apply(registry, [a, b], matches, new Dictionary<ScanServerMatch, ScanChoice>());

    McpServer server = Assert.Single(registry.Servers);
    Assert.Equal(2, registry.TargetFolders.Count);
    Assert.All(registry.TargetFolders, t => Assert.Contains(server.Id, t.EnabledServers));
  }

  [Fact]
  public void Later_unknown_with_other_credentials_differs_from_first_occurrence()
  {
    ScannedFolder a = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "A"));
    ScannedFolder b = Folder("/p/b", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "B"));
    McpRegistry registry = new();

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([a, b], registry);

    Assert.Equal(ScanMatchKind.Differs, matches[1].Kind);
    Assert.Same(matches[0].Imported, matches[1].Library);
    _planner.Apply(registry, [a, b], matches, new Dictionary<ScanServerMatch, ScanChoice>
    {
      [matches[1]] = ScanChoice.KeepLibrary,
    });
    Assert.Equal("A", Assert.Single(registry.Servers).EnvironmentVariables["TOKEN"]);
  }

  [Fact]
  public void Differs_requires_a_choice_and_leaves_registry_untouched()
  {
    McpRegistry registry = new();
    registry.Servers.Add(Library("tool", "run", "A"));
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "B"));

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], registry);

    Assert.Equal(ScanMatchKind.Differs, matches[0].Kind);
    ArgumentException ex = Assert.Throws<ArgumentException>(() =>
      _planner.Apply(registry, [folder], matches, new Dictionary<ScanServerMatch, ScanChoice>()));
    Assert.Contains("tool", ex.Message);
    Assert.Contains("/p/a", ex.Message);
    Assert.Empty(registry.TargetFolders);
    Assert.Equal("A", registry.Servers[0].EnvironmentVariables["TOKEN"]);
  }

  [Fact]
  public void Matches_by_managed_id_before_name()
  {
    McpServer library = Library("renamed", "run");
    Guid id = library.Id;
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("old-name", "run", managedId: id));

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], new McpRegistry { Servers = [library] });

    Assert.Same(library, matches[0].Library);
    Assert.Equal(ScanMatchKind.Known, matches[0].Kind);
  }

  [Fact]
  public void Use_found_version_updates_library_keeping_id()
  {
    McpServer library = Library("tool", "old", "A");
    library.DisplayName = "Nice";
    Guid id = library.Id;
    McpRegistry registry = new();
    registry.Servers.Add(library);
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("tool", "new", "B"));

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], registry);
    _planner.Apply(registry, [folder], matches, new Dictionary<ScanServerMatch, ScanChoice>
    {
      [matches[0]] = ScanChoice.UseFoundVersion,
    });

    Assert.Equal(id, library.Id);
    Assert.Equal("new", library.Command);
    Assert.Equal("B", library.EnvironmentVariables["TOKEN"]);
    Assert.Equal("Nice", library.DisplayName);
    Assert.NotSame(matches[0].Imported.EnvironmentVariables, library.EnvironmentVariables);
    Assert.Contains(id, registry.TargetFolders[0].EnabledServers);
  }

  [Fact]
  public void Keep_library_leaves_library_untouched_and_enables_it()
  {
    McpServer library = Library("tool", "old", "A");
    McpRegistry registry = new();
    registry.Servers.Add(library);
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("tool", "new", "B"));

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], registry);
    _planner.Apply(registry, [folder], matches, new Dictionary<ScanServerMatch, ScanChoice>
    {
      [matches[0]] = ScanChoice.KeepLibrary,
    });

    Assert.Equal("old", library.Command);
    Assert.Contains(library.Id, registry.TargetFolders[0].EnabledServers);
  }

  [Fact]
  public void Import_separately_creates_unique_name()
  {
    McpRegistry registry = new();
    registry.Servers.Add(Library("tool", "old", "A"));
    registry.Servers.Add(Library("tool-api", "x"));
    ScannedFolder folder = Folder("/p/api/", TargetClientFlags.ClaudeCode, false, Found("tool", "new", "B"));

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], registry);
    _planner.Apply(registry, [folder], matches, new Dictionary<ScanServerMatch, ScanChoice>
    {
      [matches[0]] = ScanChoice.ImportSeparately,
    });

    Assert.Equal(3, registry.Servers.Count);
    McpServer added = registry.Servers[2];
    Assert.Equal("tool-api-2", added.Name);
    Assert.Contains("tool", registry.TargetFolders[0].AdoptedServerNames);
    Assert.Equal("A", registry.Servers[0].EnvironmentVariables["TOKEN"]);
    Assert.Equal(3, registry.Servers.Select(s => s.Id).Distinct().Count());
    Assert.Contains(added.Id, registry.TargetFolders[0].EnabledServers);
    Assert.DoesNotContain(registry.Servers[0].Id, registry.TargetFolders[0].EnabledServers);
    Assert.Equal("api", registry.TargetFolders[0].Name);
  }

  [Fact]
  public void Union_of_harness_servers_and_adopted_names()
  {
    ScannedFolder folder = Folder(
      "/p/a",
      TargetClientFlags.ClaudeCode | TargetClientFlags.Codex,
      false,
      Found("one", "run"),
      Found("one", "run", client: TargetClientFlags.Codex),
      Found("two", "run", client: TargetClientFlags.Codex),
      Found("off", "run", enabled: false));
    McpRegistry registry = new();

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], registry);
    Assert.Equal(4, matches.Count);
    Assert.Equal(ScanMatchKind.Known, matches.Single(m => m.Found.Client == TargetClientFlags.Codex && m.Found.Name == "one").Kind);
    _planner.Apply(registry, [folder], matches, new Dictionary<ScanServerMatch, ScanChoice>());

    TargetFolder place = Assert.Single(registry.TargetFolders);
    Assert.Equal(TargetClientFlags.ClaudeCode | TargetClientFlags.Codex, place.EnabledClients);
    Assert.Equal(3, registry.Servers.Count);
    Assert.Equal(2, place.EnabledServers.Count);
    Assert.Equal(new[] { "one", "two" }.Order(), place.AdoptedServerNames.Order());
  }

  [Fact]
  public void Rescan_updates_existing_place_instead_of_duplicating()
  {
    McpServer old = Library("old", "run");
    McpServer fresh = Library("fresh", "run");
    Guid profile = Guid.NewGuid();
    DateTimeOffset applied = DateTimeOffset.UtcNow;
    TargetFolder existing = new()
    {
      Name = "custom",
      Path = "/P/A",
      EnabledClients = TargetClientFlags.ClaudeCode,
      ProfileId = profile,
      LastAppliedAt = applied,
      AppliedFileHashes = { ["f"] = "h" },
      EnabledServers = { old.Id },
      DisabledServers = { fresh.Id },
    };
    McpRegistry registry = new();
    registry.Servers.AddRange([old, fresh]);
    registry.TargetFolders.Add(existing);
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.Codex, false, Found("fresh", "run", client: TargetClientFlags.Codex));

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], registry);
    _planner.Apply(registry, [folder], matches, new Dictionary<ScanServerMatch, ScanChoice>());

    Assert.Same(existing, Assert.Single(registry.TargetFolders));
    Assert.Equal(TargetClientFlags.ClaudeCode | TargetClientFlags.Codex, existing.EnabledClients);
    Assert.Contains(old.Id, existing.EnabledServers);
    Assert.Contains(fresh.Id, existing.EnabledServers);
    Assert.DoesNotContain(fresh.Id, existing.DisabledServers);
    Assert.Contains("fresh", existing.AdoptedServerNames);
    Assert.Equal(profile, existing.ProfileId);
    Assert.Equal(applied, existing.LastAppliedAt);
    Assert.Equal("h", existing.AppliedFileHashes["f"]);
    Assert.Equal("custom", existing.Name);
  }

  [Fact]
  public void Global_places_are_named_after_client_and_reused_per_client()
  {
    ScannedFolder global = Folder(
      "/home/.claude.json",
      TargetClientFlags.ClaudeCodeGlobal,
      true,
      Found("one", "run", client: TargetClientFlags.ClaudeCodeGlobal));
    McpRegistry registry = new();

    for (int i = 0; i < 2; i++)
    {
      IReadOnlyList<ScanServerMatch> matches = _planner.Match([global], registry);
      _planner.Apply(registry, [global], matches, new Dictionary<ScanServerMatch, ScanChoice>());
    }

    TargetFolder place = Assert.Single(registry.TargetFolders);
    Assert.True(place.IsGlobal);
    Assert.Equal("Claude Code", place.Name);
  }

  [Fact]
  public void Imported_managed_id_colliding_with_other_server_gets_new_id()
  {
    McpServer library = Library("taken", "run");
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("other", "different", managedId: library.Id));
    McpRegistry registry = new();
    registry.Servers.Add(library);

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], registry);
    Assert.Same(library, matches[0].Library);
    _planner.Apply(registry, [folder], matches, new Dictionary<ScanServerMatch, ScanChoice>
    {
      [matches[0]] = ScanChoice.ImportSeparately,
    });

    Assert.Equal(2, registry.Servers.Select(s => s.Id).Distinct().Count());
  }

  [Fact]
  public void Import_separately_slugs_global_place_name()
  {
    McpRegistry registry = new();
    registry.Servers.Add(Library("tool", "old", "A"));
    ScannedFolder global = Folder(
      "/home/.claude.json", TargetClientFlags.ClaudeCodeGlobal, true,
      Found("tool", "new", "B", client: TargetClientFlags.ClaudeCodeGlobal));

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([global], registry);
    _planner.Apply(registry, [global], matches, new Dictionary<ScanServerMatch, ScanChoice>
    {
      [matches[0]] = ScanChoice.ImportSeparately,
    });

    Assert.Equal("tool-claude-code", registry.Servers[1].Name);
  }

  [Fact]
  public void Unparseable_entries_are_skipped_and_reported()
  {
    ExistingTargetServer bad = new("bad", null, true, TargetClientFlags.ClaudeCode, "/x/.mcp.json", "{ not json");
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.ClaudeCode, false, bad, Found("good", "run"));
    List<string> skipped = [];

    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], new McpRegistry(), skipped);

    Assert.Equal("good", Assert.Single(matches).Found.Name);
    string message = Assert.Single(skipped);
    Assert.StartsWith("/p/a: bad: ", message);
  }

  [Fact]
  public void Undefined_choice_is_rejected()
  {
    McpRegistry registry = new();
    registry.Servers.Add(Library("tool", "run", "A"));
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "B"));
    IReadOnlyList<ScanServerMatch> matches = _planner.Match([folder], registry);

    Assert.Throws<ArgumentException>(() => _planner.Apply(registry, [folder], matches,
      new Dictionary<ScanServerMatch, ScanChoice> { [matches[0]] = (ScanChoice)99 }));
    Assert.Empty(registry.TargetFolders);
  }

  [Fact]
  public void Two_different_found_versions_cannot_both_replace_the_library()
  {
    McpRegistry registry = new();
    McpServer library = Library("tool", "run", "A");
    registry.Servers.Add(library);
    ScannedFolder a = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "B"));
    ScannedFolder b = Folder("/p/b", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "C"));
    IReadOnlyList<ScanServerMatch> matches = _planner.Match([a, b], registry);

    ArgumentException ex = Assert.Throws<ArgumentException>(() => _planner.Apply(registry, [a, b], matches,
      matches.ToDictionary(m => m, _ => ScanChoice.UseFoundVersion)));

    Assert.Contains("tool", ex.Message);
    Assert.Contains("/p/a", ex.Message);
    Assert.Contains("/p/b", ex.Message);
    Assert.Empty(registry.TargetFolders);
    Assert.Equal("A", library.EnvironmentVariables["TOKEN"]);
  }

  [Theory]
  [InlineData(ScanChoice.KeepLibrary)]
  [InlineData(ScanChoice.ImportSeparately)]
  public void Replacing_the_library_conflicts_with_a_folder_that_keeps_a_different_version(ScanChoice other)
  {
    McpRegistry registry = new();
    registry.Servers.Add(Library("tool", "run", "A"));
    ScannedFolder a = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "B"));
    ScannedFolder b = Folder("/p/b", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "C"));
    IReadOnlyList<ScanServerMatch> matches = _planner.Match([a, b], registry);
    Dictionary<ScanServerMatch, ScanChoice> choices = new()
    {
      [matches[0]] = other,
      [matches[1]] = ScanChoice.UseFoundVersion,
    };

    if (other == ScanChoice.KeepLibrary)
    {
      Assert.Throws<ArgumentException>(() => _planner.Apply(registry, [a, b], matches, choices));
      Assert.Empty(registry.TargetFolders);
    }
    else
    {
      _planner.Apply(registry, [a, b], matches, choices);
      Assert.Equal("C", registry.Servers[0].EnvironmentVariables["TOKEN"]);
    }
  }

  [Fact]
  public void Replacing_the_library_conflicts_with_a_known_folder()
  {
    McpRegistry registry = new();
    registry.Servers.Add(Library("tool", "run", "A"));
    ScannedFolder a = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "A"));
    ScannedFolder b = Folder("/p/b", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "C"));
    IReadOnlyList<ScanServerMatch> matches = _planner.Match([a, b], registry);

    Assert.Throws<ArgumentException>(() => _planner.Apply(registry, [a, b], matches,
      new Dictionary<ScanServerMatch, ScanChoice> { [matches[1]] = ScanChoice.UseFoundVersion }));
    Assert.Empty(registry.TargetFolders);
  }

  [Fact]
  public void Replacing_a_first_occurrence_conflicts_unless_that_folder_is_unselected()
  {
    ScannedFolder a = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "A"));
    ScannedFolder b = Folder("/p/b", TargetClientFlags.ClaudeCode, false, Found("tool", "run", "C"));
    McpRegistry registry = new();
    IReadOnlyList<ScanServerMatch> matches = _planner.Match([a, b], registry);
    Dictionary<ScanServerMatch, ScanChoice> choices = new() { [matches[1]] = ScanChoice.UseFoundVersion };

    Assert.Throws<ArgumentException>(() => _planner.Apply(registry, [a, b], matches, choices));
    Assert.Empty(registry.Servers);

    _planner.Apply(registry, [b], matches, choices);
    Assert.Equal("C", Assert.Single(registry.Servers).EnvironmentVariables["TOKEN"]);
  }

  [Fact]
  public void Promote_adopts_only_known_library_servers()
  {
    McpRegistry registry = new();
    registry.Servers.Add(Library("known", "run", "tok-a"));
    registry.Servers.Add(Library("differs", "run", "tok-a"));
    ScannedFolder folder = Folder("/p/a", TargetClientFlags.ClaudeCode, false,
      Found("known", "run", "tok-a"), Found("differs", "run", "tok-b"), Found("unknown", "run", "tok-a"));

    TargetFolder place = _planner.PromoteFolder(registry, folder);

    Assert.Equal([registry.Servers[0].Id], place.EnabledServers);
    Assert.Equal(["known"], place.AdoptedServerNames);
    Assert.Equal(2, registry.Servers.Count);
    Assert.Same(place, Assert.Single(registry.TargetFolders));
  }

  [Fact]
  public void Promote_folder_without_configs_creates_empty_place()
  {
    McpRegistry registry = new();
    ScannedFolder folder = Folder("/p/empty", TargetClientFlags.None, false);

    TargetFolder place = _planner.PromoteFolder(registry, folder);

    Assert.Equal(TargetClientFlags.None, place.EnabledClients);
    Assert.Equal("empty", place.Name);
    Assert.Same(place, Assert.Single(registry.TargetFolders));
  }

  [Fact]
  public void Add_to_library_adopts_identical_occurrences_only()
  {
    McpRegistry registry = new();
    TargetFolder p1 = new() { Name = "a", Path = "/p/a", EnabledClients = TargetClientFlags.ClaudeCode };
    TargetFolder p2 = new() { Name = "b", Path = "/p/b", EnabledClients = TargetClientFlags.ClaudeCode };
    registry.TargetFolders.AddRange([p1, p2]);
    ScannedFolder a = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("jira", "run", "tok-a"));
    ScannedFolder b = Folder("/p/b", TargetClientFlags.ClaudeCode, false, Found("jira", "run", "tok-b"));
    ScannedFolder c = Folder("/p/c", TargetClientFlags.ClaudeCode, false, Found("jira", "run", "tok-a"));

    McpServer server = _planner.AddToLibrary(registry, [a, b, c], "jira");

    Assert.Same(server, Assert.Single(registry.Servers));
    Assert.Equal("tok-a", server.EnvironmentVariables["TOKEN"]);
    Assert.Equal([server.Id], p1.EnabledServers);
    Assert.Equal(["jira"], p1.AdoptedServerNames);
    Assert.Empty(p2.EnabledServers);
    Assert.Empty(p2.AdoptedServerNames);
    Assert.Equal(2, registry.TargetFolders.Count);
  }

  [Fact]
  public void Add_to_library_unknown_name_throws()
  {
    ScannedFolder a = Folder("/p/a", TargetClientFlags.ClaudeCode, false, Found("jira", "run", "tok-a"));

    Assert.Throws<ArgumentException>(() => _planner.AddToLibrary(new McpRegistry(), [a], "nope"));
  }
}
