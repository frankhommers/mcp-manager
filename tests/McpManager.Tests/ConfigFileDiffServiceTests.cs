using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class ConfigFileDiffServiceTests
{
  private readonly ConfigFileDiffService _service = new(new ConfigImportService());

  [Fact]
  public void Detects_added_removed_and_changed_servers()
  {
    const string left = """{"mcpServers":{"a":{"command":"one"},"b":{"command":"old"}}}""";
    const string right = """{"mcpServers":{"b":{"command":"new"},"c":{"command":"three"}}}""";

    ConfigFileDiff diff = _service.Compare("/x/.mcp.json", TargetClientFlags.ClaudeCode, left, right);

    Assert.Collection(diff.Servers,
      a =>
      {
        Assert.Equal("a", a.ServerName);
        Assert.Equal(DiffKind.Removed, a.Kind);
        Assert.True(a.Details.HasDifferences);
      },
      b =>
      {
        Assert.Equal("b", b.ServerName);
        Assert.Equal(DiffKind.Changed, b.Kind);
      },
      c =>
      {
        Assert.Equal("c", c.ServerName);
        Assert.Equal(DiffKind.Added, c.Kind);
      });
  }

  [Fact]
  public void Missing_left_file_marks_everything_added_and_equal_servers_are_same()
  {
    const string content = """{"mcpServers":{"a":{"command":"one"}}}""";

    ConfigFileDiff missing = _service.Compare("/x/.mcp.json", TargetClientFlags.ClaudeCode, null, content);
    ConfigFileDiff same = _service.Compare("/x/.mcp.json", TargetClientFlags.ClaudeCode, content, content);

    Assert.Equal(DiffKind.Added, Assert.Single(missing.Servers).Kind);
    Assert.Equal(DiffKind.Same, Assert.Single(same.Servers).Kind);
  }

  [Fact]
  public void Conflict_compare_parses_codex_toml()
  {
    ExportConflict conflict = new("/x/config.toml", "a",
      "[server]\ncommand = 'old'\n", "[server]\ncommand = 'new'\n", TargetClientFlags.Codex);

    ServerDiff diff = _service.CompareConflict(conflict);

    Assert.True(diff.HasDifferences);
  }
}
