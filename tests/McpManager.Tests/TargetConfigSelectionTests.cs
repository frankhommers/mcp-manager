using System.Text.Json.Nodes;
using McpManager.Core.ConfigGenerators;
using McpManager.Core.Models;
using McpManager.Core.Services;
using Tomlyn;
using Tomlyn.Model;

namespace McpManager.Tests;

public class TargetConfigSelectionTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-target-selection-").FullName;
  private readonly ConfigExportService _exportService = new();

  [Fact]
  public async Task Local_codex_export_uses_project_config_and_never_merges_global_settings()
  {
    string globalPath = Path.Combine(_directory, "global.toml");
    string projectPath = Path.Combine(_directory, "project");
    string localPath = Path.Combine(projectPath, ".codex", "config.toml");
    const string globalContent = "model = 'global-model'\n[mcp_servers.global]\ncommand = 'global-command'\n";
    await File.WriteAllTextAsync(globalPath, globalContent);
    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
    await File.WriteAllTextAsync(localPath, "model = 'project-model'\n[mcp_servers.manual]\ncommand = 'local-command'\n");
    McpServer server = CreateServer("local");
    TargetFolder target = new()
    {
      Path = projectPath,
      EnabledClients = TargetClientFlags.Codex | TargetClientFlags.ClaudeCode,
      EnabledServers = [server.Id],
    };
    GlobalSettings settings = new() { CodexConfigPath = globalPath };

    // Reuse the same generator after a global preview to catch stale merge paths.
    _exportService.PreviewConfigs(new TargetFolder
    {
      Path = _directory,
      IsGlobal = true,
      EnabledClients = TargetClientFlags.Codex,
    }, [], settings);
    Dictionary<string, string> preview = _exportService.PreviewConfigs(target, [server], settings);
    Assert.Equal(2, preview.Count);
    Assert.False(preview.ContainsKey(globalPath));
    await _exportService.ExportAsync(target, [server], settings);

    Assert.Equal(globalContent, await File.ReadAllTextAsync(globalPath));
    Assert.Equal(preview[localPath], await File.ReadAllTextAsync(localPath));
    TomlTable root = TomlSerializer.Deserialize<TomlTable>(preview[localPath])!;
    Assert.Equal("project-model", root["model"]);
    TomlTable servers = (TomlTable)root["mcp_servers"]!;
    Assert.Equal(server.Id, ManagedServerIdentity.Read(servers["local"]));
    Assert.True(servers.ContainsKey("manual"));
    Assert.False(servers.ContainsKey("global"));

    server.Name = "renamed";
    await _exportService.ExportAsync(target, [server], settings);
    target.EnabledServers.Clear();
    await _exportService.ExportAsync(target, [server], settings);
    root = TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(localPath))!;
    Assert.Single((TomlTable)root["mcp_servers"]!);
    Assert.Equal(globalContent, await File.ReadAllTextAsync(globalPath));
  }

  [Fact]
  public async Task Missing_local_codex_config_is_created_without_copying_global_config()
  {
    string globalPath = Path.Combine(_directory, "global.toml");
    const string globalContent = "model = 'private-global-model'";
    await File.WriteAllTextAsync(globalPath, globalContent);
    McpServer server = CreateServer("local");
    TargetFolder target = CreateTarget(TargetClientFlags.Codex, [server]);
    target.IsQuickExport = true;
    await _exportService.ExportAsync(target, [server], new GlobalSettings { CodexConfigPath = globalPath });

    string localPath = Path.Combine(_directory, ".codex", "config.toml");
    TomlTable root = TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(localPath))!;
    Assert.False(root.ContainsKey("model"));
    Assert.Equal(globalContent, await File.ReadAllTextAsync(globalPath));
  }

  [Fact]
  public async Task Selection_unions_enabled_ids_across_selected_formats_without_writing_files()
  {
    McpServer first = CreateServer("first");
    McpServer shared = CreateServer("shared");
    McpServer last = CreateServer("last");
    McpServer disabled = CreateServer("disabled");
    await _exportService.ExportAsync(CreateTarget(TargetClientFlags.ClaudeCode, [first, shared]), [first, shared]);
    await _exportService.ExportAsync(CreateTarget(TargetClientFlags.OpenCode, [shared, last]), [shared, last]);
    await _exportService.ExportAsync(CreateTarget(TargetClientFlags.Codex, [shared, disabled]), [shared, disabled]);
    string codexPath = Path.Combine(_directory, ".codex", "config.toml");
    TomlTable root = TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(codexPath))!;
    TomlTable servers = (TomlTable)root["mcp_servers"]!;
    ((TomlTable)servers["shared"]!)["enabled"] = false;
    ((TomlTable)servers["disabled"]!)["enabled"] = false;
    await File.WriteAllTextAsync(codexPath, TomlSerializer.Serialize(root));

    TargetFolder target = CreateTarget(
      TargetClientFlags.ClaudeCode | TargetClientFlags.OpenCode | TargetClientFlags.Codex, []);
    Dictionary<string, string> before = _exportService.GetConfigFilePaths(target)
      .Values.ToDictionary(path => path, File.ReadAllText);
    ExistingTargetServers result = await new TargetConfigSelectionService(_exportService).ReadAsync(target);

    Assert.True(result.EnabledServerIds.SetEquals([first.Id, shared.Id, last.Id]));
    Assert.Equal(3, result.ConfigFileCount);
    Assert.Empty(result.UnreadableFiles);
    foreach ((string path, string content) in before)
    {
      Assert.Equal(content, await File.ReadAllTextAsync(path));
    }
  }

  [Theory]
  [InlineData(TargetClientFlags.ClaudeCode, ".mcp.json", "mcpServers")]
  [InlineData(TargetClientFlags.ClaudeDesktop, "claude_desktop_config.json", "mcpServers")]
  [InlineData(TargetClientFlags.Cursor, ".cursor/mcp.json", "mcpServers")]
  [InlineData(TargetClientFlags.Windsurf, "mcp_config.json", "mcpServers")]
  [InlineData(TargetClientFlags.VsCode, ".vscode/mcp.json", "servers")]
  [InlineData(TargetClientFlags.OpenCode, "opencode.jsonc", "mcp")]
  public async Task Reads_ids_in_json_formats_and_ignores_unmarked_and_disabled_entries(
    TargetClientFlags client, string relativePath, string section)
  {
    McpServer active = CreateServer("old-name");
    McpServer disabled = CreateServer("disabled");
    TargetFolder target = CreateTarget(client, [active, disabled]);
    await _exportService.ExportAsync(target, [active, disabled]);
    string path = Path.Combine(_directory, relativePath);
    JsonNode root = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
    root[section]!["manual"] = new JsonObject { ["command"] = "manual-command" };
    root[section]!["disabled"]!["disabled"] = true;
    await File.WriteAllTextAsync(path, "// Existing configuration\n" + root.ToJsonString());

    ExistingTargetServers result = await new TargetConfigSelectionService(_exportService).ReadAsync(target);
    Assert.Equal(active.Id, Assert.Single(result.EnabledServerIds));
    Assert.Equal(1, result.UnmanagedServerCount);
    Assert.Empty(result.UnreadableFiles);
  }

  [Theory]
  [InlineData(TargetClientFlags.Codex, ".codex/config.toml")]
  [InlineData(TargetClientFlags.OpenCode, "opencode.jsonc")]
  public async Task Invalid_config_reports_its_path_and_keeps_other_formats(
    TargetClientFlags client, string relativePath)
  {
    McpServer server = CreateServer("valid");
    await _exportService.ExportAsync(CreateTarget(TargetClientFlags.ClaudeCode, [server]), [server]);
    string path = Path.Combine(_directory, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    const string invalid = "this is not valid {";
    await File.WriteAllTextAsync(path, invalid);

    ExistingTargetServers result = await new TargetConfigSelectionService(_exportService)
      .ReadAsync(CreateTarget(TargetClientFlags.ClaudeCode | client, []));
    Assert.Equal(server.Id, Assert.Single(result.EnabledServerIds));
    Assert.Equal(path, Assert.Single(result.UnreadableFiles));
    Assert.Equal(invalid, await File.ReadAllTextAsync(path));
  }

  [Fact]
  public async Task Selection_uses_existing_opencode_json_fallback_and_skips_clipboard()
  {
    McpServer server = CreateServer("existing");
    await File.WriteAllTextAsync(Path.Combine(_directory, "opencode.json"),
      new OpenCodeConfigGenerator().GenerateConfig([server]));
    TargetFolder target = CreateTarget(TargetClientFlags.OpenCode | TargetClientFlags.Codex, []);
    TargetConfigSelectionService service = new(_exportService);
    ExistingTargetServers result = await service.ReadAsync(target);
    Assert.Equal(server.Id, Assert.Single(result.EnabledServerIds));
    Assert.Equal(1, result.ConfigFileCount);
    Assert.Empty(result.UnreadableFiles);
    target.IsClipboard = true;
    result = await service.ReadAsync(target);
    Assert.Empty(result.EnabledServerIds);
    Assert.Equal(0, result.ConfigFileCount);
  }

  [Fact]
  public async Task Global_codex_selection_uses_configured_path()
  {
    string path = Path.Combine(_directory, "custom.toml");
    McpServer server = CreateServer("global");
    await File.WriteAllTextAsync(path, new CodexConfigGenerator().GenerateConfig([server]));
    TargetFolder target = CreateTarget(TargetClientFlags.Codex, []);
    target.IsGlobal = true;
    ExistingTargetServers result = await new TargetConfigSelectionService(_exportService)
      .ReadAsync(target, new GlobalSettings { CodexConfigPath = path });
    Assert.Equal(server.Id, Assert.Single(result.EnabledServerIds));
  }

  private TargetFolder CreateTarget(TargetClientFlags clients, List<McpServer> servers) => new()
  {
    Path = _directory,
    EnabledClients = clients,
    EnabledServers = servers.Select(s => s.Id).ToHashSet(),
  };

  private static McpServer CreateServer(string name) => new()
  {
    Name = name,
    TransportType = McpTransportType.Stdio,
    Command = "test-mcp-server",
  };

  public void Dispose() => Directory.Delete(_directory, recursive: true);
}
