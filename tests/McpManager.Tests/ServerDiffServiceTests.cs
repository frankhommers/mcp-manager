using McpManager.Core.ConfigGenerators;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class ServerDiffServiceTests
{
  private static McpServer Docker(params string[] args) => new()
  {
    Name = "atlassian",
    Command = "docker",
    Args = [.. args],
  };

  [Fact]
  public void Inserted_arg_is_added_and_rest_same()
  {
    McpServer left = Docker("run", "--rm", "-i", "mcp/atlassian");
    McpServer right = Docker("run", "--rm", "-i", "--name", "atlassian-argaleo-mcp", "mcp/atlassian");

    FieldDiff args = ServerDiffService.Compare(left, right).Fields.Single(f => f.Field == "args");

    Assert.Equal(DiffKind.Changed, args.Kind);
    Assert.Equal(["--name", "atlassian-argaleo-mcp"], args.Right.Where(t => t.Kind == DiffKind.Added).Select(t => t.Text));
    Assert.All(args.Left, t => Assert.Equal(DiffKind.Same, t.Kind));
  }

  [Fact]
  public void Token_in_docker_env_arg_is_secret()
  {
    McpServer left = Docker("run", "-e", "JIRA_API_TOKEN=abcd1234", "mcp/atlassian");
    McpServer right = Docker("run", "-e", "JIRA_API_TOKEN=abcd1234", "--name", "x", "mcp/atlassian");

    FieldDiff args = ServerDiffService.Compare(left, right).Fields.Single(f => f.Field == "args");

    Assert.True(args.IsSecret);
    Assert.True(args.Left.Single(t => t.Text.StartsWith("JIRA_API_TOKEN=")).IsSecret);
    Assert.False(args.Left.Single(t => t.Text == "mcp/atlassian").IsSecret);
  }

  [Fact]
  public void Arg_after_secret_flag_is_secret()
  {
    McpServer left = Docker("--api-key", "hunter2hunter2");
    FieldDiff args = ServerDiffService.Compare(left, Docker()).Fields.Single(f => f.Field == "args");

    Assert.True(args.Left.Single(t => t.Text == "hunter2hunter2").IsSecret);
    Assert.False(args.Left.Single(t => t.Text == "--api-key").IsSecret);
  }

  [Fact]
  public void Header_values_in_default_bridge_template_are_secret()
  {
    ClaudeDesktopConfigGenerator generator = new();
    ResolvedCommand bridge = BridgeCommandResolver.Resolve(
      generator.BridgeCommandHttp,
      "https://example.com/mcp",
      null,
      generator.BridgeHeaderArgumentTemplate,
      new Dictionary<string, string> { ["Authorization"] = "Bearer abcdef123456", ["X-Api-Key"] = "k-987654" });
    McpServer left = new() { Name = "s", Command = bridge.Command, Args = [.. bridge.Arguments] };

    FieldDiff args = ServerDiffService.Compare(left, new McpServer { Name = "s" }).Fields.Single(f => f.Field == "args");

    Assert.True(args.Left.Single(t => t.Text == "Bearer abcdef123456").IsSecret);
    Assert.True(args.Left.Single(t => t.Text == "k-987654").IsSecret);
    Assert.False(args.Left.Single(t => t.Text == "Authorization").IsSecret);
    Assert.All(args.Left.Where(t => t.Text == "--headers"), t => Assert.False(t.IsSecret));
    Assert.False(args.Left.Single(t => t.Text == "https://example.com/mcp").IsSecret);
  }

  [Fact]
  public void Header_given_as_key_colon_value_is_secret()
  {
    McpServer left = Docker("--header", "Authorization: Bearer abcdef123456", "https://example.com/mcp");

    FieldDiff args = ServerDiffService.Compare(left, Docker()).Fields.Single(f => f.Field == "args");

    Assert.True(args.Left.Single(t => t.Text.StartsWith("Authorization")).IsSecret);
    Assert.False(args.Left.Single(t => t.Text == "https://example.com/mcp").IsSecret);
  }

  [Fact]
  public void Secret_url_query_values_are_secret_in_url_and_args()
  {
    McpServer left = new()
    {
      Name = "s",
      Url = "https://example.com/mcp?api_key=abcdef123456&mode=fast",
      Args = ["--url", "https://example.com/sse?token=zyxw9876", "https://example.com/?mode=fast"],
    };

    ServerDiff diff = ServerDiffService.Compare(left, new McpServer { Name = "s" });

    FieldDiff url = diff.Fields.Single(f => f.Field == "url");
    Assert.True(url.IsSecret);
    Assert.True(Assert.Single(url.Left).IsSecret);
    FieldDiff args = diff.Fields.Single(f => f.Field == "args");
    Assert.True(args.Left.Single(t => t.Text.Contains("token=")).IsSecret);
    Assert.False(args.Left.Single(t => t.Text.Contains("mode=")).IsSecret);
  }

  [Theory]
  [InlineData("https://example.com/mcp?api_key=abcdef123456&mode=fast", "https://example.com/mcp?api_key=••••••••3456&mode=fast")]
  [InlineData("https://example.com/mcp?mode=fast&token=ab#top", "https://example.com/mcp?mode=fast&token=••••••••#top")]
  [InlineData("https://example.com/mcp?mode=fast", "https://example.com/mcp?mode=fast")]
  public void Mask_query_masks_only_secret_values(string url, string expected) =>
    Assert.Equal(expected, SecretMask.MaskQuery(url));

  [Fact]
  public void Different_secrets_with_same_last_four_are_still_changed()
  {
    McpServer left = new() { Name = "s", EnvironmentVariables = { ["API_KEY"] = "xx1234" } };
    McpServer right = new() { Name = "s", EnvironmentVariables = { ["API_KEY"] = "yy1234" } };

    FieldDiff field = ServerDiffService.Compare(left, right).Fields.Single(f => f.Field == "env:API_KEY");

    Assert.Equal(DiffKind.Changed, field.Kind);
    Assert.True(field.IsSecret);
  }

  [Fact]
  public void Managed_markers_are_ignored()
  {
    McpServer left = new() { Name = "s", EnvironmentVariables = { [ManagedServerIdentity.EnvironmentKey] = "1" } };
    McpServer right = new() { Name = "s", HttpHeaders = { [ManagedServerIdentity.HeaderKey.ToLowerInvariant()] = "1" } };

    Assert.False(ServerDiffService.Compare(left, right).HasDifferences);
    Assert.DoesNotContain(ServerDiffService.Compare(left, right).Fields, f => f.Field.StartsWith("env:") || f.Field.StartsWith("header:"));
  }

  [Fact]
  public void Header_keys_compare_case_insensitively()
  {
    McpServer left = new() { Name = "s", HttpHeaders = { ["authorization"] = "Bearer a" } };
    McpServer right = new() { Name = "s", HttpHeaders = { ["Authorization"] = "Bearer a" } };

    Assert.False(ServerDiffService.Compare(left, right).HasDifferences);
  }

  [Fact]
  public void Env_only_on_one_side_is_added_and_fields_are_ordered()
  {
    McpServer left = new() { Name = "s", Command = "x", AlwaysAllow = ["b", "a"] };
    McpServer right = new()
    {
      Name = "s",
      Command = "x",
      AlwaysAllow = ["a", "b"],
      EnvironmentVariables = { ["B"] = "1", ["A"] = "2" },
    };

    ServerDiff diff = ServerDiffService.Compare(left, right);

    Assert.Equal(["type", "command", "env:A", "env:B", "tools"], diff.Fields.Select(f => f.Field));
    Assert.Equal(DiffKind.Added, diff.Fields.Single(f => f.Field == "env:A").Kind);
    Assert.Empty(diff.Fields.Single(f => f.Field == "env:A").Left);
    Assert.Equal(DiffKind.Same, diff.Fields.Single(f => f.Field == "tools").Kind);
  }

  [Theory]
  [InlineData("JIRA_API_TOKEN", true)]
  [InlineData("Authorization", true)]
  [InlineData("JIRA_URL", false)]
  public void Secret_keys(string key, bool expected) => Assert.Equal(expected, SecretMask.IsSecretKey(key));

  [Fact]
  public void Mask_shows_last_four() => Assert.Equal("••••••••7be5", SecretMask.Mask("ATATT3xFf7be5"));

  [Fact]
  public void Mask_hides_short_values() => Assert.Equal("••••••••", SecretMask.Mask("abcd"));
}
