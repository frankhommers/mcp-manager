using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.Models;
using McpManager.Core.Services;
using Tomlyn;
using Tomlyn.Model;

namespace McpManager.Tests;

public class ExportConflictTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-export-conflicts-").FullName;
  private readonly ConfigExportService _service = new();

  public static TheoryData<TargetClientFlags, string, string> Clients => new()
  {
    { TargetClientFlags.ClaudeCode, ".mcp.json", "mcpServers" },
    { TargetClientFlags.ClaudeDesktop, "claude_desktop_config.json", "mcpServers" },
    { TargetClientFlags.Cursor, ".cursor/mcp.json", "mcpServers" },
    { TargetClientFlags.Windsurf, "mcp_config.json", "mcpServers" },
    { TargetClientFlags.VsCode, ".vscode/mcp.json", "servers" },
    { TargetClientFlags.OpenCode, "opencode.jsonc", "mcp" },
    { TargetClientFlags.Codex, ".codex/config.toml", "mcp_servers" },
  };

  [Theory]
  [MemberData(nameof(Clients))]
  public async Task Replace_adopts_only_the_conflicting_entry_and_future_exports_need_no_choice(
    TargetClientFlags client, string relativePath, string section)
  {
    string path = Path.Combine(_directory, relativePath);
    string original = await WriteExistingAsync(path, section);
    McpServer tmux = CreateServer("tmux");
    McpServer other = CreateServer("other");
    TargetFolder target = CreateTarget(client, [tmux, other]);
    int choices = 0;

    Dictionary<string, string>? configs = await _service.PrepareExportAsync([target], [tmux, other], null, conflict =>
    {
      choices++;
      Assert.Equal("tmux", conflict.ServerName);
      Assert.Equal(path, conflict.FilePath);
      Assert.Contains("existing-command", conflict.ExistingConfiguration);
      Assert.Contains("new-command", conflict.ProposedConfiguration);
      Assert.Equal(original, File.ReadAllText(path));
      return Task.FromResult<ExportConflictResolution?>(ExportConflictResolution.Replace);
    });

    Assert.Equal(1, choices);
    Assert.NotNull(configs);
    Assert.Equal(original, await File.ReadAllTextAsync(path));
    await _service.WriteConfigsAsync(configs);
    Assert.Equal(configs[path], await File.ReadAllTextAsync(path));
    JsonNode root = ReadRoot(path);
    Assert.Equal("original-model", root["model"]!.GetValue<string>());
    Assert.True(JsonNode.DeepEquals(ReadRoot(original, path)[section]!["manual"], root[section]!["manual"]));
    Assert.Equal(tmux.Id, ManagedServerIdentity.Read(root[section]!["tmux"]));
    Assert.Equal(other.Id, ManagedServerIdentity.Read(root[section]!["other"]));
    Assert.Null(root[section]!["tmux"]!["customOption"]);

    tmux.Name = "renamed";
    await _service.ExportAsync(target, [tmux, other]);
    root = ReadRoot(path);
    Assert.Null(root[section]!["tmux"]);
    Assert.Equal(tmux.Id, ManagedServerIdentity.Read(root[section]!["renamed"]));
  }

  [Theory]
  [MemberData(nameof(Clients))]
  public async Task Keep_preserves_the_existing_entry_without_ownership_and_exports_other_servers(
    TargetClientFlags client, string relativePath, string section)
  {
    string path = Path.Combine(_directory, relativePath);
    string original = await WriteExistingAsync(path, section);
    McpServer tmux = CreateServer("tmux");
    McpServer other = CreateServer("other");
    TargetFolder target = CreateTarget(client, [tmux, other]);

    Dictionary<string, string>? configs = await _service.PrepareExportAsync([target], [tmux, other], null,
      _ => Task.FromResult<ExportConflictResolution?>(ExportConflictResolution.KeepExisting));

    Assert.NotNull(configs);
    await _service.WriteConfigsAsync(configs);
    JsonNode root = ReadRoot(path);
    Assert.True(JsonNode.DeepEquals(ReadRoot(original, path)[section]!["tmux"], root[section]!["tmux"]));
    Assert.Null(ManagedServerIdentity.Read(root[section]!["tmux"]));
    Assert.Equal(other.Id, ManagedServerIdentity.Read(root[section]!["other"]));
    Assert.Equal("original-model", root["model"]!.GetValue<string>());

    await Assert.ThrowsAsync<ExportConflictException>(() => _service.ExportAsync(target, [tmux, other]));
  }

  [Fact]
  public async Task Choices_are_independent_for_each_server_in_the_same_file()
  {
    string path = Path.Combine(_directory, ".mcp.json");
    string original = await WriteExistingAsync(path, "mcpServers");
    McpServer tmux = CreateServer("tmux");
    McpServer manual = CreateServer("manual");
    List<string> prompted = [];

    Dictionary<string, string>? configs = await _service.PrepareExportAsync(
      [CreateTarget(TargetClientFlags.ClaudeCode, [tmux, manual])], [tmux, manual], null, conflict =>
      {
        prompted.Add(conflict.ServerName);
        Assert.Equal(original, File.ReadAllText(path));
        return Task.FromResult<ExportConflictResolution?>(conflict.ServerName == "tmux"
          ? ExportConflictResolution.Replace
          : ExportConflictResolution.KeepExisting);
      });

    Assert.Equal(["tmux", "manual"], prompted);
    Assert.NotNull(configs);
    await _service.WriteConfigsAsync(configs);
    JsonNode servers = ReadRoot(path)["mcpServers"]!;
    Assert.Equal(tmux.Id, ManagedServerIdentity.Read(servers["tmux"]));
    Assert.True(JsonNode.DeepEquals(ReadRoot(original, path)["mcpServers"]!["manual"], servers["manual"]));
  }

  [Fact]
  public async Task Same_name_in_different_files_requires_separate_choices()
  {
    string claudePath = Path.Combine(_directory, ".mcp.json");
    string codexPath = Path.Combine(_directory, ".codex", "config.toml");
    await WriteExistingAsync(claudePath, "mcpServers");
    string codexOriginal = await WriteExistingAsync(codexPath, "mcp_servers");
    McpServer tmux = CreateServer("tmux");
    TargetFolder target = CreateTarget(TargetClientFlags.ClaudeCode | TargetClientFlags.Codex, [tmux]);
    List<string> prompted = [];

    Dictionary<string, string>? configs = await _service.PrepareExportAsync([target], [tmux], null, conflict =>
    {
      prompted.Add(conflict.FilePath);
      return Task.FromResult<ExportConflictResolution?>(conflict.FilePath == claudePath
        ? ExportConflictResolution.Replace
        : ExportConflictResolution.KeepExisting);
    });

    Assert.Equal([claudePath, codexPath], prompted);
    Assert.NotNull(configs);
    await _service.WriteConfigsAsync(configs);
    Assert.Equal(tmux.Id, ManagedServerIdentity.Read(ReadRoot(claudePath)["mcpServers"]!["tmux"]));
    Assert.True(JsonNode.DeepEquals(ReadRoot(codexOriginal, codexPath), ReadRoot(codexPath)));
  }

  [Fact]
  public async Task Cancelling_a_later_target_leaves_all_destinations_untouched_even_after_replace_choice()
  {
    McpServer tmux = CreateServer("tmux");
    TargetFolder first = CreateTarget(TargetClientFlags.ClaudeCode, [tmux]);
    TargetFolder second = CreateTarget(TargetClientFlags.ClaudeCode, [tmux]);
    second.Path = Path.Combine(_directory, "second");
    TargetFolder third = CreateTarget(TargetClientFlags.ClaudeCode, [tmux]);
    third.Path = Path.Combine(_directory, "third");
    string secondPath = Path.Combine(second.Path, ".mcp.json");
    string thirdPath = Path.Combine(third.Path, ".mcp.json");
    string secondOriginal = await WriteExistingAsync(secondPath, "mcpServers");
    string thirdOriginal = await WriteExistingAsync(thirdPath, "mcpServers");
    int choices = 0;

    Dictionary<string, string>? configs = await _service.PrepareExportAsync([first, second, third], [tmux], null,
      conflict =>
      {
        choices++;
        return Task.FromResult<ExportConflictResolution?>(conflict.FilePath == secondPath
          ? ExportConflictResolution.Replace
          : null);
      });

    Assert.Null(configs);
    Assert.Equal(2, choices);
    Assert.False(File.Exists(Path.Combine(first.Path, ".mcp.json")));
    Assert.Equal(secondOriginal, await File.ReadAllTextAsync(secondPath));
    Assert.Equal(thirdOriginal, await File.ReadAllTextAsync(thirdPath));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Changed_configuration_while_dialog_is_open_requires_a_fresh_choice(bool changeExisting)
  {
    string path = Path.Combine(_directory, ".mcp.json");
    string original = await WriteExistingAsync(path, "mcpServers");
    McpServer tmux = CreateServer("tmux");
    TargetFolder target = CreateTarget(TargetClientFlags.ClaudeCode, [tmux]);
    int choices = 0;

    Dictionary<string, string>? configs = await _service.PrepareExportAsync([target], [tmux], null, async conflict =>
    {
      choices++;
      if (choices == 1)
      {
        if (changeExisting)
        {
          await File.WriteAllTextAsync(path, original.Replace("existing-command", "changed-command"));
        }
        else
        {
          tmux.Command = "changed-command";
        }

        return ExportConflictResolution.Replace;
      }

      Assert.Contains("changed-command", changeExisting ? conflict.ExistingConfiguration : conflict.ProposedConfiguration);
      return null;
    });

    Assert.Null(configs);
    Assert.Equal(2, choices);
    Assert.Equal(changeExisting ? original.Replace("existing-command", "changed-command") : original,
      await File.ReadAllTextAsync(path));
  }

  private static async Task<string> WriteExistingAsync(string path, string section)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    string content = path.EndsWith(".toml", StringComparison.Ordinal)
      ? """
        model = "original-model"
        [mcp_servers.tmux]
        command = "existing-command"
        customOption = 123
        [mcp_servers.tmux.env]
        EXISTING = "preserved"
        [mcp_servers.manual]
        command = "manual-command"
        """
      : new JsonObject
      {
        ["model"] = "original-model",
        [section] = new JsonObject
        {
          ["tmux"] = new JsonObject
          {
            ["command"] = "existing-command",
            ["customOption"] = 123,
            ["env"] = new JsonObject { ["EXISTING"] = "preserved" },
          },
          ["manual"] = new JsonObject { ["command"] = "manual-command" },
        },
      }.ToJsonString();
    await File.WriteAllTextAsync(path, content);
    return content;
  }

  private static JsonNode ReadRoot(string path) => ReadRoot(File.ReadAllText(path), path);

  private static JsonNode ReadRoot(string content, string path) =>
    path.EndsWith(".toml", StringComparison.Ordinal)
      ? JsonSerializer.SerializeToNode(TomlSerializer.Deserialize<TomlTable>(content))!
      : JsonNode.Parse(content)!;

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
    Command = "new-command",
  };

  public void Dispose() => Directory.Delete(_directory, recursive: true);
}
