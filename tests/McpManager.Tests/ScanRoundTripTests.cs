using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class ScanRoundTripTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-scan-round-trip-").FullName;

  [Fact]
  public async Task Scanning_deployed_places_recognizes_every_server()
  {
    McpServer http = new()
    {
      Name = "remote-http",
      TransportType = McpTransportType.Http,
      Url = "https://example.com/mcp",
      HttpHeaders = { ["Authorization"] = "Bearer abcdef123456", ["X-Api-Key"] = "k-987654" },
    };
    McpServer sse = new() { Name = "remote-sse", TransportType = McpTransportType.Sse, Url = "https://example.com/sse" };
    McpServer streamable = new()
    {
      Name = "remote-streamable", TransportType = McpTransportType.StreamableHttp, Url = "https://example.com/stream",
    };
    McpServer local = new()
    {
      Name = "local",
      Command = "npx",
      Args = ["-y", "tool"],
      WorkingDirectory = "/tmp",
      EnvironmentVariables = { ["TOKEN"] = "library-token" },
      AlwaysAllow = ["read"],
    };
    List<McpServer> servers = [http, sse, streamable, local];
    HashSet<Guid> all = [.. servers.Select(s => s.Id)];
    TargetFolder project = new()
    {
      Name = "project",
      Path = Path.Combine(_directory, "repos", "project"),
      EnabledClients = TargetClientFlags.ClaudeCode | TargetClientFlags.OpenCode | TargetClientFlags.Codex,
      EnabledServers = [.. all],
    };
    TargetFolder overridden = new()
    {
      Name = "overridden",
      Path = Path.Combine(_directory, "repos", "overridden"),
      EnabledClients = TargetClientFlags.ClaudeCode,
      EnabledServers = [.. all],
      ServerEnvOverrides = { [local.Id] = new Dictionary<string, string> { ["TOKEN"] = "place-token" } },
      ServerToolOverrides = { [local.Id] = ["write"] },
    };
    string desktopFolder = Path.Combine(_directory, "desktop");
    TargetFolder desktop = new()
    {
      Name = "Claude Desktop",
      Path = desktopFolder,
      IsGlobal = true,
      EnabledClients = TargetClientFlags.ClaudeDesktop,
      EnabledServers = [.. all],
      BridgeArgs = "--debug",
    };
    McpRegistry registry = new()
    {
      Servers = servers,
      TargetFolders = [project, overridden, desktop],
      Settings = new GlobalSettings { CodexConfigPath = Path.Combine(_directory, "global.toml") },
    };
    Directory.CreateDirectory(project.Path);
    Directory.CreateDirectory(overridden.Path);
    Directory.CreateDirectory(desktopFolder);
    ConfigExportService exporter = new();
    DeployResult deployed = await new TargetDeployService(exporter).DeployAsync(
      [project, overridden, desktop], registry, _ => Task.FromResult<ExportConflictResolution?>(null));
    Assert.False(deployed.Cancelled);

    ConfigScanService scanner = new(
      exporter, _ => [(TargetClientFlags.ClaudeDesktop, desktopFolder)], Path.Combine(_directory, "home"));
    IReadOnlyList<ScannedFolder> folders = await scanner.ScanAsync(
      [Path.Combine(_directory, "repos")], registry.Settings);
    IReadOnlyList<ScanServerMatch> matches = new ScanImportPlanner(new ConfigImportService(), exporter)
      .Match(folders, registry);

    Assert.Equal(3, folders.Count);
    Assert.Equal(4 + 4 + 4, matches.Count);
    Assert.All(matches, m => Assert.True(m.Kind == ScanMatchKind.Known,
      $"{m.Found.Name} in {m.Found.FilePath}: " +
      string.Join(", ", m.Diff?.Fields.Where(f => f.Kind != DiffKind.Same).Select(f => f.Field) ?? [])));
  }

  public void Dispose() => Directory.Delete(_directory, recursive: true);
}
