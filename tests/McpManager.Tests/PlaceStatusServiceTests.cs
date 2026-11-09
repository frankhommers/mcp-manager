using System.Text.Json;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class PlaceStatusServiceTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-manager-status-tests-").FullName;
  private readonly ConfigExportService _export = new();

  private static Task<ExportConflictResolution?> NoResolution(ExportConflict conflict) =>
    Task.FromResult<ExportConflictResolution?>(null);

  [Fact]
  public void Never_deployed_is_not_behind()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry(_directory);
    Assert.Equal(PlaceStatus.NotDeployedYet, new PlaceStatusService(_export).GetStatus(target, registry));
  }

  [Fact]
  public async Task Deployed_place_is_up_to_date()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry(_directory);
    await new TargetDeployService(_export).DeployAsync([target], registry, NoResolution);
    Assert.Equal(PlaceStatus.UpToDate, new PlaceStatusService(_export).GetStatus(target, registry));
  }

  [Fact]
  public async Task Saved_change_after_deploy_is_behind()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry(_directory);
    await new TargetDeployService(_export).DeployAsync([target], registry, NoResolution);
    registry.Servers[0].Url = "https://changed.test/mcp";
    Assert.Equal(PlaceStatus.Behind, new PlaceStatusService(_export).GetStatus(target, registry));
  }

  [Fact]
  public async Task External_edit_is_externally_changed()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry(_directory);
    await new TargetDeployService(_export).DeployAsync([target], registry, NoResolution);
    await File.AppendAllTextAsync(Path.Combine(_directory, ".mcp.json"), " ");
    Assert.Equal(PlaceStatus.ExternallyChanged, new PlaceStatusService(_export).GetStatus(target, registry));
  }

  [Fact]
  public async Task Never_deployed_with_unmanaged_same_name_entry_does_not_throw()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry(_directory);
    await File.WriteAllTextAsync(
      Path.Combine(_directory, ".mcp.json"), """{"mcpServers":{"managed":{"url":"https://manual.test/mcp"}}}""");
    Assert.Equal(PlaceStatus.NotDeployedYet, new PlaceStatusService(_export).GetStatus(target, registry));
  }

  [Fact]
  public async Task Deployed_with_unmanaged_same_name_entry_needs_attention()
  {
    (McpRegistry registry, TargetFolder target) = CreateRegistry(_directory);
    string path = Path.Combine(_directory, ".mcp.json");
    await File.WriteAllTextAsync(path, """{"mcpServers":{"managed":{"url":"https://manual.test/mcp"}}}""");
    target.LastAppliedAt = DateTimeOffset.UtcNow;
    target.AppliedFileHashes[path] = TargetSyncService.FileHash(path);
    Assert.Equal(PlaceStatus.NeedsAttention, new PlaceStatusService(_export).GetStatus(target, registry));
  }

  [Fact]
  public void Count_changed_files_only_for_affected_places()
  {
    string otherDirectory = Directory.CreateTempSubdirectory("mcp-manager-status-other-").FullName;
    try
    {
      (McpRegistry current, TargetFolder first) = CreateRegistry(_directory);
      TargetFolder second = new()
      {
        Name = "Other",
        Path = otherDirectory,
        EnabledClients = TargetClientFlags.ClaudeCode,
      };
      current.TargetFolders.Add(second);
      first.EnabledServers.Clear();
      McpRegistry saved = JsonSerializer.Deserialize<McpRegistry>(JsonSerializer.Serialize(current))!;
      first.EnabledServers.Add(current.Servers[0].Id);

      Assert.Equal(2, new PlaceStatusService(_export).CountChangedFiles(saved, current));
    }
    finally
    {
      Directory.Delete(otherDirectory, recursive: true);
    }
  }

  [Fact]
  public void Changed_places_are_the_places_with_changed_files()
  {
    string otherDirectory = Directory.CreateTempSubdirectory("mcp-manager-status-other-").FullName;
    try
    {
      (McpRegistry current, TargetFolder first) = CreateRegistry(_directory);
      TargetFolder second = new() { Name = "Other", Path = otherDirectory, EnabledServers = [current.Servers[0].Id] };
      current.TargetFolders.Add(second);
      current.TargetFolders.Add(new TargetFolder { Name = "Clipboard", IsClipboard = true });
      second.EnabledServers.Clear();
      McpRegistry saved = JsonSerializer.Deserialize<McpRegistry>(JsonSerializer.Serialize(current))!;
      second.EnabledServers.Add(current.Servers[0].Id);

      Assert.Equal([second.Id], new PlaceStatusService(_export).GetChangedPlaces(saved, current).Select(t => t.Id));
      Assert.Empty(new PlaceStatusService(_export).GetChangedPlaces(current, current));
    }
    finally
    {
      Directory.Delete(otherDirectory, recursive: true);
    }
  }

  [Fact]
  public void Unchanged_registry_counts_no_files()
  {
    (McpRegistry current, _) = CreateRegistry(_directory);
    McpRegistry saved = JsonSerializer.Deserialize<McpRegistry>(JsonSerializer.Serialize(current))!;
    Assert.Equal(0, new PlaceStatusService(_export).CountChangedFiles(saved, current));
  }

  [Fact]
  public void Count_includes_all_files_when_preview_hits_unmanaged_conflict()
  {
    (McpRegistry current, _) = CreateRegistry(_directory);
    McpRegistry saved = JsonSerializer.Deserialize<McpRegistry>(JsonSerializer.Serialize(current))!;
    File.WriteAllText(
      Path.Combine(_directory, ".mcp.json"), """{"mcpServers":{"managed":{"url":"https://manual.test/mcp"}}}""");
    Assert.Equal(2, new PlaceStatusService(_export).CountChangedFiles(saved, current));
  }

  [Fact]
  public void Count_includes_all_files_when_preview_fails_on_invalid_file()
  {
    (McpRegistry current, _) = CreateRegistry(_directory);
    McpRegistry saved = JsonSerializer.Deserialize<McpRegistry>(JsonSerializer.Serialize(current))!;
    File.WriteAllText(Path.Combine(_directory, ".mcp.json"), "{ not json");
    Assert.Equal(2, new PlaceStatusService(_export).CountChangedFiles(saved, current));
  }

  [Fact]
  public void Count_includes_all_files_of_place_missing_from_saved()
  {
    (McpRegistry current, _) = CreateRegistry(_directory);
    Assert.Equal(2, new PlaceStatusService(_export).CountChangedFiles(new McpRegistry(), current));
  }

  [Fact]
  public async Task Unreadable_file_never_throws()
  {
    if (OperatingSystem.IsWindows())
    {
      return;
    }

    (McpRegistry current, TargetFolder target) = CreateRegistry(_directory);
    await new TargetDeployService(_export).DeployAsync([target], current, NoResolution);
    McpRegistry saved = JsonSerializer.Deserialize<McpRegistry>(JsonSerializer.Serialize(current))!;
    string path = Path.Combine(_directory, ".mcp.json");
    File.SetUnixFileMode(path, UnixFileMode.None);
    try
    {
      PlaceStatusService service = new(_export);
      Assert.Equal(PlaceStatus.NeedsAttention, service.GetStatus(target, current));
      Assert.True(service.CountChangedFiles(saved, current) >= 0);
    }
    finally
    {
      File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
  }

  private static (McpRegistry Registry, TargetFolder Target) CreateRegistry(string directory)
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
      Path = directory,
      EnabledClients = TargetClientFlags.ClaudeCode | TargetClientFlags.OpenCode,
      EnabledServers = [server.Id],
    };
    return (new McpRegistry { Servers = [server], TargetFolders = [target] }, target);
  }

  public void Dispose() => Directory.Delete(_directory, recursive: true);
}
