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
  public async Task Successful_deploy_clears_adopted_names()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry();
    target.AdoptedServerNames = ["managed"];
    await new TargetDeployService(new ConfigExportService()).DeployAsync([target], registry, NoResolution);
    Assert.Empty(target.AdoptedServerNames);
  }

  [Fact]
  public async Task Cancelled_deploy_keeps_adopted_names()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry();
    await File.WriteAllTextAsync(
      Path.Combine(_directory, ".mcp.json"), """{"mcpServers":{"managed":{"url":"https://manual.test/mcp"}}}""");
    target.AdoptedServerNames = ["other"];
    DeployResult result = await new TargetDeployService(new ConfigExportService())
      .DeployAsync([target], registry, NoResolution);
    Assert.True(result.Cancelled);
    Assert.Contains("other", target.AdoptedServerNames);
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
    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
      new TargetDeployService(new ConfigExportService()).DeployAsync([target], registry, NoResolution));
    Assert.Equal("Place 'Place' needs at least one harness and an absolute folder path.", ex.Message);
  }

  [Fact]
  public async Task First_deploy_with_servers_from_another_library_is_rejected()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry();
    await File.WriteAllTextAsync(Path.Combine(_directory, ".mcp.json"),
      """{"mcpServers":{"other":{"command":"x","env":{"MCP_MANAGER_ID":"@id"}}}}""".Replace("@id", Guid.NewGuid().ToString()));

    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
      new TargetDeployService(new ConfigExportService()).DeployAsync([target], registry, NoResolution));

    Assert.Equal(
      "Place 'Place' has servers deployed by MCP Manager that are not in your library. " +
      "Scan or import them before the first deploy.", ex.Message);
  }

  [Fact]
  public void Missing_shared_settings_are_named_for_the_place()
  {
    TargetFolder target = new() { Name = "Place", ProfileId = Guid.NewGuid() };

    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => ProfileResolver.Resolve(target, []));

    Assert.Equal("The shared settings for place 'Place' are missing.", ex.Message);
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
