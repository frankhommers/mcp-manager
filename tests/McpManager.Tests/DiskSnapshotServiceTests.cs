using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class DiskSnapshotServiceTests : IDisposable
{
  private readonly string _tmp = Directory.CreateTempSubdirectory("mcp-disk-").FullName;
  private readonly string _root;
  private readonly string _home;

  public DiskSnapshotServiceTests()
  {
    _root = Path.Combine(_tmp, "root");
    _home = Path.Combine(_tmp, "home");
    Directory.CreateDirectory(_root);
    Directory.CreateDirectory(_home);
  }

  [Fact]
  public async Task Load_includes_places_outside_scan_roots()
  {
    string elsewhere = Path.Combine(_tmp, "elsewhere");
    Write(elsewhere, "a");
    McpRegistry registry = Registry();
    registry.TargetFolders.Add(new TargetFolder { Path = elsewhere, EnabledClients = TargetClientFlags.ClaudeCode });

    DiskSnapshot snapshot = await Service().LoadAsync(registry);

    Assert.Contains(snapshot.Folders, f => f.Path == elsewhere);
    Assert.Contains(snapshot.Matches, m => m.Found.Name == "a");
  }

  [Fact]
  public async Task Trailing_separator_place_is_the_same_folder()
  {
    string melvin = Path.Combine(_root, "melvin");
    Write(melvin, "a");
    TargetFolder place = new()
    {
      Path = melvin + Path.DirectorySeparatorChar,
      EnabledClients = TargetClientFlags.ClaudeCode,
    };
    McpRegistry registry = Registry();
    registry.TargetFolders.Add(place);

    DiskSnapshot snapshot = await Service().LoadAsync(registry);

    Assert.Single(snapshot.Folders, f => ProjectFolderRules.PathsEqual(f.Path, melvin));
    Assert.NotNull(snapshot.FolderFor(place));
  }

  [Fact]
  public async Task Folder_whose_match_throws_keeps_other_folders()
  {
    string good = Path.Combine(_root, "good");
    string bad = Path.Combine(_root, "bad");
    Write(good, "a");
    Write(bad, "b");
    DiskSnapshotService service = new(NewScanService(), new ThrowingPlanner());

    DiskSnapshot snapshot = await service.LoadAsync(Registry());

    Assert.Equal(2, snapshot.Folders.Count);
    Assert.All(snapshot.Matches, m => Assert.Equal(good, m.FolderPath));
    Assert.NotEmpty(snapshot.Matches);
    string problem = Assert.Single(snapshot.Problems);
    Assert.StartsWith(bad, problem);
    Assert.Contains("boom", problem);
  }

  [Fact]
  public async Task Rematch_reflects_new_library()
  {
    Write(Path.Combine(_root, "app"), "a");
    McpRegistry registry = Registry();
    DiskSnapshotService service = Service();
    DiskSnapshot snapshot = await service.LoadAsync(registry);
    Assert.Equal(ScanMatchKind.Unknown, Assert.Single(snapshot.Matches).Kind);

    registry.Servers.Add(new McpServer { Name = "a", Command = "node" });
    DiskSnapshot rematched = service.Rematch(snapshot, registry);

    Assert.Equal(ScanMatchKind.Known, Assert.Single(rematched.Matches).Kind);
  }

  [Fact]
  public async Task Read_folder_without_configs_has_no_clients()
  {
    string empty = Path.Combine(_tmp, "empty");
    Directory.CreateDirectory(empty);

    ScannedFolder folder = await NewScanService().ReadFolderAsync(empty, new GlobalSettings());

    Assert.Equal(TargetClientFlags.None, folder.Clients);
    Assert.Empty(folder.Servers);
  }

  [Theory]
  [InlineData("test", true)]
  [InlineData("Tests", true)]
  [InlineData("test-configs", true)]
  [InlineData("my-test", true)]
  [InlineData("IntegrationTests", true)]
  [InlineData("app/unit_tests", true)]
  [InlineData("app", false)]
  [InlineData("latest", false)]
  [InlineData("contest", false)]
  [InlineData("attestation", false)]
  [InlineData("PROTEST", false)]
  public void LooksLikeTest_matches_whole_segments_below_the_root(string folder, bool expected)
  {
    string root = Path.Combine(_tmp, "my-tests", "repos");

    Assert.Equal(expected, ProjectFolderRules.LooksLikeTest(Path.Combine(root, folder), [root]));
  }

  private ConfigScanService NewScanService() => new(new ConfigExportService(), _ => [], homeDirectory: _home);

  private DiskSnapshotService Service() =>
    new(NewScanService(), new ScanImportPlanner(new ConfigImportService(), new ConfigExportService()));

  private McpRegistry Registry()
  {
    McpRegistry registry = new();
    registry.Settings.ScanRoots = [_root];
    return registry;
  }

  private static void Write(string directory, string serverName)
  {
    Directory.CreateDirectory(directory);
    File.WriteAllText(
      Path.Combine(directory, ".mcp.json"),
      $$"""{ "mcpServers": { "{{serverName}}": { "command": "node" } } }""");
  }

  public void Dispose() => Directory.Delete(_tmp, true);

  private sealed class ThrowingPlanner()
    : ScanImportPlanner(new ConfigImportService(), new ConfigExportService())
  {
    public override IReadOnlyList<ScanServerMatch> Match(IEnumerable<ScannedFolder> folders, McpRegistry registry,
      ICollection<string>? skipped = null)
    {
      if (folders.Any(f => f.Path.EndsWith("bad", StringComparison.Ordinal)))
      {
        throw new InvalidOperationException("boom");
      }

      return base.Match(folders, registry, skipped);
    }
  }
}
