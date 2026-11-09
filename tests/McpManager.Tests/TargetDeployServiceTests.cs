using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class TargetDeployServiceTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-manager-deploy-tests-").FullName;

  private static Task<ExportConflictResolution?> NoResolution(ExportConflict conflict) =>
    Task.FromResult<ExportConflictResolution?>(null);

  [Fact]
  public async Task Place_with_claude_code_and_opencode_writes_both_files_and_records_both_hashes()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry();
    DeployResult result = await new TargetDeployService(new ConfigExportService())
      .DeployAsync([target], registry, NoResolution);

    Assert.False(result.Cancelled);
    Assert.True(File.Exists(Path.Combine(_directory, ".mcp.json")));
    Assert.True(File.Exists(Path.Combine(_directory, "opencode.jsonc")));
    Assert.Equal(2, target.AppliedFileHashes.Count);
    Assert.NotNull(target.LastAppliedAt);
  }

  [Fact]
  public async Task External_change_blocks_deploy_unless_overwrite()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry();
    TargetDeployService service = new(new ConfigExportService());
    await service.DeployAsync([target], registry, NoResolution);
    await File.AppendAllTextAsync(Path.Combine(_directory, ".mcp.json"), " ");

    await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeployAsync([target], registry, NoResolution));
    DeployResult result = await service.DeployAsync([target], registry, NoResolution, overwriteExternal: true);
    Assert.False(result.Cancelled);
  }

  [Fact]
  public async Task Cancelled_conflict_writes_nothing()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry();
    string path = Path.Combine(_directory, ".mcp.json");
    await File.WriteAllTextAsync(path, """{"mcpServers":{"managed":{"url":"https://manual.test/mcp"}}}""");
    byte[] original = await File.ReadAllBytesAsync(path);

    DeployResult result = await new TargetDeployService(new ConfigExportService())
      .DeployAsync([target], registry, NoResolution);

    Assert.True(result.Cancelled);
    Assert.Equal(original, await File.ReadAllBytesAsync(path));
    Assert.False(File.Exists(Path.Combine(_directory, "opencode.jsonc")));
    Assert.Null(target.LastAppliedAt);
  }

  [Fact]
  public async Task Target_without_client_is_rejected()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry();
    target.EnabledClients = TargetClientFlags.None;
    await Assert.ThrowsAsync<InvalidOperationException>(() =>
      new TargetDeployService(new ConfigExportService()).DeployAsync([target], registry, NoResolution));
  }

  private (McpRegistry Registry, TargetFolder Target) CreateRegistry()
  {
    McpServer server = new()
    {
      Name = "managed",
      TransportType = McpTransportType.StreamableHttp,
      Url = "https://managed.test/mcp",
    };
    TargetFolder target = new()
    {
      Name = "Place",
      Path = _directory,
      EnabledClients = TargetClientFlags.ClaudeCode | TargetClientFlags.OpenCode,
      EnabledServers = [server.Id],
    };
    return (new McpRegistry { Servers = [server], TargetFolders = [target] }, target);
  }

  public void Dispose() => Directory.Delete(_directory, recursive: true);
}
