using System.Text.Json.Nodes;
using McpManager.Core.ConfigGenerators;
using McpManager.Core.Models;
using McpManager.Core.Services;
using Tomlyn;
using Tomlyn.Model;

namespace McpManager.Tests;

public class ManagedExportTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-manager-export-tests-").FullName;

  [Theory]
  [InlineData(TargetClientFlags.ClaudeCode, ".mcp.json", "mcpServers")]
  [InlineData(TargetClientFlags.ClaudeDesktop, "claude_desktop_config.json", "mcpServers")]
  [InlineData(TargetClientFlags.Cursor, ".cursor/mcp.json", "mcpServers")]
  [InlineData(TargetClientFlags.Windsurf, "mcp_config.json", "mcpServers")]
  [InlineData(TargetClientFlags.VsCode, ".vscode/mcp.json", "servers")]
  [InlineData(TargetClientFlags.OpenCode, "opencode.jsonc", "mcp")]
  public async Task Json_export_preserves_unmanaged_config_and_reconciles_owned_servers(
    TargetClientFlags client, string relativePath, string section)
  {
    string path = Path.Combine(_directory, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    JsonObject unmanaged = new() { ["url"] = "https://manual.test/mcp", ["customOption"] = 123 };
    JsonObject original = new()
    {
      ["clientPreferences"] = new JsonObject { ["theme"] = "dark" },
      [section] = new JsonObject { ["manual"] = unmanaged.DeepClone() },
    };
    await File.WriteAllTextAsync(path, original.ToJsonString());

    McpServer server = CreateServer();
    TargetFolder target = CreateTarget(client, server);
    ConfigExportService service = new();
    string preview = service.PreviewConfigs(target, [server])[path];
    await service.ExportAsync(target, [server]);
    Assert.Equal(preview, await File.ReadAllTextAsync(path));

    JsonObject root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
    Assert.True(JsonNode.DeepEquals(unmanaged, root[section]!["manual"]));
    Assert.Equal("dark", root["clientPreferences"]!["theme"]!.GetValue<string>());
    Assert.Equal(server.Id, ManagedServerIdentity.Read(root[section]![server.Name]));

    // A renamed server must replace its previous entry, even after local edits.
    root[section]![server.Name]!["manuallyAdded"] = true;
    await File.WriteAllTextAsync(path, root.ToJsonString());
    server.Name = "renamed";
    server.Url = "https://changed.test/mcp";
    await service.ExportAsync(target, [server]);
    root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
    Assert.Null(root[section]!["managed"]);
    Assert.Null(root[section]!["renamed"]!["manuallyAdded"]);
    Assert.Equal(server.Id, ManagedServerIdentity.Read(root[section]!["renamed"]));

    target.EnabledServers.Clear();
    await service.ExportAsync(target, [server]);
    root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
    Assert.Single(root[section]!.AsObject());
    Assert.True(JsonNode.DeepEquals(unmanaged, root[section]!["manual"]));
  }

  [Fact]
  public async Task Codex_export_preserves_other_servers_and_settings()
  {
    string path = Path.Combine(_directory, "config.toml");
    await File.WriteAllTextAsync(path, "model = 'existing-model'\n[mcp_servers.manual]\nurl = 'https://manual.test/mcp'\n");
    McpServer server = CreateServer();
    TargetFolder target = CreateTarget(TargetClientFlags.Codex, server);
    GlobalSettings settings = new() { CodexConfigPath = path };
    ConfigExportService service = new();
    string preview = service.PreviewConfigs(target, [server], settings)[path];
    await service.ExportAsync(target, [server], settings);
    Assert.Equal(preview, await File.ReadAllTextAsync(path));
    TomlTable root = TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(path))!;
    TomlTable servers = (TomlTable)root["mcp_servers"]!;
    Assert.Equal("existing-model", root["model"]);
    Assert.Equal(server.Id, ManagedServerIdentity.Read(servers["managed"]));
    Assert.True(servers.ContainsKey("manual"));

    server.Name = "renamed";
    server.TransportType = McpTransportType.Stdio;
    server.Command = "local-server";
    await service.ExportAsync(target, [server], settings);
    root = TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(path))!;
    servers = (TomlTable)root["mcp_servers"]!;
    Assert.False(servers.ContainsKey("managed"));
    TomlTable renamed = (TomlTable)servers["renamed"]!;
    Assert.False(renamed.ContainsKey("url"));
    Assert.False(renamed.ContainsKey("http_headers"));
    Assert.Equal(server.Id, ManagedServerIdentity.Read(renamed));

    target.EnabledServers.Clear();
    await service.ExportAsync(target, [server], settings);
    root = TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(path))!;
    Assert.Single((TomlTable)root["mcp_servers"]!);
  }

  [Theory]
  [InlineData(TargetClientFlags.ClaudeCode, ".mcp.json", "{\"mcpServers\":{\"managed\":{\"command\":\"manual\"}}}")]
  [InlineData(TargetClientFlags.Codex, "config.toml", "[mcp_servers.managed]\ncommand = 'manual'")]
  public async Task Unmarked_name_collision_leaves_file_untouched(
    TargetClientFlags client, string fileName, string original)
  {
    string path = Path.Combine(_directory, fileName);
    await File.WriteAllTextAsync(path, original);
    McpServer server = CreateServer();
    TargetFolder target = CreateTarget(client, server);
    ConfigExportService service = new();
    GlobalSettings settings = new() { CodexConfigPath = path };

    ExportConflictException error = await Assert.ThrowsAsync<ExportConflictException>(
      () => service.ExportAsync(target, [server], settings));
    Assert.Contains("not owned by MCP Manager", error.Message);
    Assert.Equal(path, error.Conflict.FilePath);
    Assert.Equal(server.Name, error.Conflict.ServerName);
    Assert.Equal(original, await File.ReadAllTextAsync(path));
  }

  [Theory]
  [InlineData(TargetClientFlags.OpenCode, "opencode.jsonc")]
  [InlineData(TargetClientFlags.Codex, "config.toml")]
  [InlineData(TargetClientFlags.ClaudeDesktop, "claude_desktop_config.json")]
  public async Task Unreadable_configuration_is_not_replaced(TargetClientFlags client, string fileName)
  {
    string path = Path.Combine(_directory, fileName);
    const string original = "this is not a valid configuration {";
    await File.WriteAllTextAsync(path, original);
    McpServer server = CreateServer();
    TargetFolder target = CreateTarget(client, server);
    ConfigExportService service = new();

    await Assert.ThrowsAnyAsync<Exception>(
      () => service.ExportAsync(target, [server], new GlobalSettings { CodexConfigPath = path }));
    Assert.Equal(original, await File.ReadAllTextAsync(path));
  }

  [Fact]
  public async Task OpenCode_updates_existing_json_instead_of_creating_a_second_config()
  {
    string path = Path.Combine(_directory, "opencode.json");
    await File.WriteAllTextAsync(path, "{ /* user setting */ \"theme\": \"dark\", \"mcp\": {} }");
    McpServer server = CreateServer();
    await new ConfigExportService().ExportAsync(CreateTarget(TargetClientFlags.OpenCode, server), [server]);

    Assert.False(File.Exists(Path.Combine(_directory, "opencode.jsonc")));
    JsonNode root = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
    Assert.Equal("dark", root["theme"]!.GetValue<string>());
    Assert.Equal(server.Id, ManagedServerIdentity.Read(root["mcp"]![server.Name]));
  }

  [Fact]
  public void Remote_marker_is_case_insensitive_and_stdio_fields_are_removed_on_transport_switch()
  {
    McpServer server = CreateServer();
    server.TransportType = McpTransportType.Stdio;
    server.Command = "old-command";
    server.EnvironmentVariables["OLD_ENV"] = "old-value";
    ClaudeCodeConfigGenerator generator = new();
    string before = generator.GenerateConfig([server]);

    server.TransportType = McpTransportType.StreamableHttp;
    server.HttpHeaders["x-mcp-manager-id"] = Guid.NewGuid().ToString();
    string merged = ManagedConfigMerger.MergeJson(before, generator.GenerateConfig([server]), "mcpServers", "test");
    JsonNode config = JsonNode.Parse(merged)!["mcpServers"]![server.Name]!;
    Assert.Null(config["command"]);
    Assert.Null(config["env"]);
    Assert.Single(config["headers"]!.AsObject(), h =>
      h.Key.Equals(ManagedServerIdentity.HeaderKey, StringComparison.OrdinalIgnoreCase));
    JsonObject headers = config["headers"]!.AsObject();
    headers.Remove(ManagedServerIdentity.HeaderKey);
    headers["x-mcp-manager-id"] = server.Id.ToString();
    Assert.Equal(server.Id, ManagedServerIdentity.Read(config));
  }

  [Fact]
  public async Task Import_recovers_identity_without_exposing_marker_as_editable_header()
  {
    McpServer server = CreateServer();
    string path = Path.Combine(_directory, ".mcp.json");
    await File.WriteAllTextAsync(path, new ClaudeCodeConfigGenerator().GenerateConfig([server]));
    McpServer imported = Assert.Single(await new ConfigImportService().ImportFromClaudeCodeAsync(path));

    Assert.Equal(server.Id, imported.Id);
    Assert.False(imported.HttpHeaders.ContainsKey(ManagedServerIdentity.HeaderKey));
  }

  [Fact]
  public async Task Collision_in_later_client_prevents_all_writes_for_target()
  {
    string claudePath = Path.Combine(_directory, ".mcp.json");
    string cursorDirectory = Path.Combine(_directory, ".cursor");
    Directory.CreateDirectory(cursorDirectory);
    string cursorPath = Path.Combine(cursorDirectory, "mcp.json");
    const string existing = "{\"mcpServers\":{\"managed\":{\"command\":\"manual\"}}}";
    await File.WriteAllTextAsync(cursorPath, existing);
    McpServer server = CreateServer();
    TargetFolder target = CreateTarget(TargetClientFlags.ClaudeCode | TargetClientFlags.Cursor, server);

    await Assert.ThrowsAsync<ExportConflictException>(() => new ConfigExportService().ExportAsync(target, [server]));

    Assert.False(File.Exists(claudePath));
    Assert.Equal(existing, await File.ReadAllTextAsync(cursorPath));
  }

  [Fact]
  public void Invalid_marker_does_not_claim_ownership()
  {
    const string existing = """
      {"mcpServers":{"manual":{"command":"manual","env":{"MCP_MANAGER_ID":"not-an-id"}}}}
      """;
    string generated = new ClaudeCodeConfigGenerator().GenerateConfig([]);
    string result = ManagedConfigMerger.MergeJson(existing, generated, "mcpServers", "test");

    Assert.True(JsonNode.DeepEquals(JsonNode.Parse(existing), JsonNode.Parse(result)));
  }

  [Fact]
  public async Task ClaudeCode_global_merge_preserves_project_configs_and_other_root_settings()
  {
    string path = Path.Combine(_directory, ".claude.json");
    const string existing = """
      {"preferences":{"theme":"dark"},"projects":{"/example":{"mcpServers":{"project":{"command":"p"}}}},
       "mcpServers":{"manual":{"command":"m"}}}
      """;
    await File.WriteAllTextAsync(path, existing);
    McpServer server = CreateServer();
    ClaudeCodeGlobalConfigGenerator generator = new() { ExistingConfigPath = path };
    string merged = ManagedConfigMerger.Merge(path, generator, generator.GenerateConfig([server]));
    JsonNode before = JsonNode.Parse(existing)!;
    JsonNode after = JsonNode.Parse(merged)!;

    Assert.True(JsonNode.DeepEquals(before["projects"], after["projects"]));
    Assert.True(JsonNode.DeepEquals(before["preferences"], after["preferences"]));
    Assert.True(JsonNode.DeepEquals(before["mcpServers"]!["manual"], after["mcpServers"]!["manual"]));
    Assert.Equal(server.Id, ManagedServerIdentity.Read(after["mcpServers"]![server.Name]));
  }

  [Theory]
  [InlineData("claude")]
  [InlineData("desktop")]
  [InlineData("cursor")]
  [InlineData("windsurf")]
  [InlineData("vscode")]
  [InlineData("opencode")]
  public void Stdio_marker_cannot_be_overridden_and_keeps_user_environment(string client)
  {
    IConfigGenerator generator = client switch
    {
      "desktop" => new ClaudeDesktopConfigGenerator(),
      "cursor" => new CursorConfigGenerator(),
      "windsurf" => new WindsurfConfigGenerator(),
      "vscode" => new VsCodeConfigGenerator(),
      "opencode" => new OpenCodeConfigGenerator(),
      _ => new ClaudeCodeConfigGenerator(),
    };
    McpServer server = CreateServer();
    server.TransportType = McpTransportType.Stdio;
    server.Command = "local";
    server.EnvironmentVariables["USER_SETTING"] = "preserved";
    Dictionary<Guid, Dictionary<string, string>> overrides = new()
    {
      [server.Id] = new Dictionary<string, string> { [ManagedServerIdentity.EnvironmentKey] = "override" },
    };
    JsonNode root = JsonNode.Parse(generator.GenerateConfig([server], overrides))!;
    string section = client switch { "vscode" => "servers", "opencode" => "mcp", _ => "mcpServers" };
    JsonNode config = root[section]![server.Name]!;

    Assert.Equal(server.Id, ManagedServerIdentity.Read(config));
    Assert.Equal("preserved", config[client == "opencode" ? "environment" : "env"]!["USER_SETTING"]!.GetValue<string>());
    Assert.False(server.EnvironmentVariables.ContainsKey(ManagedServerIdentity.EnvironmentKey));
  }

  private TargetFolder CreateTarget(TargetClientFlags client, McpServer server) => new()
  {
    Path = _directory,
    EnabledClients = client,
    IsGlobal = client == TargetClientFlags.Codex,
    EnabledServers = [server.Id],
  };

  private static McpServer CreateServer() => new()
  {
    Name = "managed",
    TransportType = McpTransportType.StreamableHttp,
    Url = "https://managed.test/mcp",
  };

  public void Dispose() => Directory.Delete(_directory, recursive: true);
}
