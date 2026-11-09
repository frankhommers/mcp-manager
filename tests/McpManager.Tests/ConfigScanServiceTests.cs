using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class ConfigScanServiceTests : IDisposable
{
  private readonly string _root = Directory.CreateTempSubdirectory("mcp-scan-").FullName;
  private readonly ConfigScanService _service = new(new ConfigExportService(), (_) => []);

  [Fact]
  public async Task Finds_folders_with_harnesses()
  {
    Write("a/.mcp.json", ClaudeJson("one"));
    Write("a/opencode.jsonc", OpenCodeJson("one"));
    Write("b/readme.txt", "nothing");

    IReadOnlyList<ScannedFolder> result = await ScanAsync();

    ScannedFolder folder = Assert.Single(result);
    Assert.Equal(Path.Combine(_root, "a"), folder.Path);
    Assert.False(folder.IsGlobal);
    Assert.Equal(TargetClientFlags.ClaudeCode | TargetClientFlags.OpenCode, folder.Clients);
    Assert.Equal(2, folder.Servers.Count);
    Assert.False(folder.HarnessesDiffer);
  }

  [Fact]
  public async Task Skips_node_modules_hidden_and_too_deep()
  {
    Write("node_modules/x/.mcp.json", ClaudeJson("one"));
    Write(".hidden/.mcp.json", ClaudeJson("one"));
    Write("bin/.mcp.json", ClaudeJson("one"));
    Write("obj/.mcp.json", ClaudeJson("one"));
    Write("d1/d2/d3/d4/.mcp.json", ClaudeJson("one"));
    Write("d1/d2/d3/d4/d5/.mcp.json", ClaudeJson("one"));

    IReadOnlyList<ScannedFolder> result = await ScanAsync();

    ScannedFolder folder = Assert.Single(result);
    Assert.Equal(Path.Combine(_root, "d1", "d2", "d3", "d4"), folder.Path);
  }

  [Fact]
  public async Task Does_not_follow_symlinked_directories()
  {
    Write("real/.mcp.json", ClaudeJson("one"));
    try
    {
      Directory.CreateSymbolicLink(Path.Combine(_root, "real", "loop"), _root);
      Directory.CreateSymbolicLink(Path.Combine(_root, "alias"), Path.Combine(_root, "real"));
    }
    catch (Exception ex) when (OperatingSystem.IsWindows() &&
                               ex is IOException or UnauthorizedAccessException)
    {
      return;
    }

    IReadOnlyList<ScannedFolder> result = await ScanAsync();

    ScannedFolder folder = Assert.Single(result);
    Assert.Equal(Path.Combine(_root, "real"), folder.Path);
  }

  [Fact]
  public async Task Malformed_file_is_reported_unreadable_and_scan_continues()
  {
    Write("bad/.mcp.json", "{ not json");
    Write("good/.mcp.json", ClaudeJson("one"));

    IReadOnlyList<ScannedFolder> result = await ScanAsync();

    Assert.Equal(2, result.Count);
    ScannedFolder bad = result[0];
    Assert.EndsWith("bad", bad.Path);
    Assert.Equal(Path.Combine(_root, "bad", ".mcp.json"), Assert.Single(bad.UnreadableFiles));
    Assert.Empty(bad.Servers);
    Assert.False(bad.HarnessesDiffer);
    Assert.Single(result[1].Servers);
  }

  [Fact]
  public async Task Harnesses_differ_flag()
  {
    Write("same/.mcp.json", ClaudeJson("one"));
    Write("same/opencode.jsonc", OpenCodeJson("one"));
    Write("diff/.mcp.json", ClaudeJson("one"));
    Write("diff/opencode.jsonc", OpenCodeJson("two"));
    Write("single/.mcp.json", ClaudeJson("one"));

    IReadOnlyList<ScannedFolder> result = await ScanAsync();

    Assert.True(result.Single(f => f.Path.EndsWith("diff")).HarnessesDiffer);
    Assert.False(result.Single(f => f.Path.EndsWith("same")).HarnessesDiffer);
    Assert.False(result.Single(f => f.Path.EndsWith("single")).HarnessesDiffer);
  }

  [Fact]
  public async Task Scan_writes_nothing()
  {
    Write("a/.mcp.json", ClaudeJson("one"));
    Write("a/.codex/config.toml", "[mcp_servers.x]\ncommand = 'c'\n");
    Write("b/.mcp.json", "{ bad");
    Dictionary<string, (byte[] Bytes, DateTime Time)> before = Snapshot();

    await ScanAsync();

    Dictionary<string, (byte[] Bytes, DateTime Time)> after = Snapshot();
    Assert.Equal(before.Keys.Order(), after.Keys.Order());
    foreach (string key in before.Keys)
    {
      Assert.Equal(before[key].Bytes, after[key].Bytes);
      Assert.Equal(before[key].Time, after[key].Time);
    }
  }

  [Fact]
  public async Task Overlapping_roots_report_folder_once_and_globals_are_appended()
  {
    string globalDir = Path.Combine(_root, "global");
    Write("global/.codex/config.toml", "[mcp_servers.g]\ncommand = 'c'\n");
    Write("a/.mcp.json", ClaudeJson("one"));
    ConfigScanService service = new(new ConfigExportService(), (_) =>
    [
      (TargetClientFlags.Codex, Path.Combine(globalDir, ".codex")),
      (TargetClientFlags.OpenCode, Path.Combine(_root, "missing")),
    ]);

    IReadOnlyList<ScannedFolder> result = await service.ScanAsync(
      [_root, Path.Combine(_root, "a")],
      new GlobalSettings { CodexConfigPath = Path.Combine(globalDir, ".codex", "config.toml") });

    Assert.Equal(1, result.Count(f => !f.IsGlobal && f.Path.EndsWith("a")));
    ScannedFolder global = result[^1];
    Assert.True(global.IsGlobal);
    Assert.Equal(TargetClientFlags.Codex, global.Clients);
    Assert.Single(global.Servers);
    Assert.Single(result.Where(f => f.IsGlobal));
  }

  private async Task<IReadOnlyList<ScannedFolder>> ScanAsync() =>
    await _service.ScanAsync([_root], new GlobalSettings());

  private Dictionary<string, (byte[] Bytes, DateTime Time)> Snapshot() =>
    Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(
      path => path, path => (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)));

  private void Write(string relativePath, string content)
  {
    string path = Path.Combine(_root, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
  }

  private static string ClaudeJson(string name) =>
    $$"""{ "mcpServers": { "{{name}}": { "command": "node" } } }""";

  private static string OpenCodeJson(string name) =>
    $$"""{ "mcp": { "{{name}}": { "type": "local", "command": ["node"] } } }""";

  public void Dispose() => Directory.Delete(_root, true);
}
