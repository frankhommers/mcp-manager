using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class CommandLineParserTests
{
  [Fact]
  public void Parses_simple_npx_command()
  {
    ParsedCommand result = CommandLineParser.Parse("npx -y @scope/server arg1");

    Assert.Equal("npx", result.Command);
    Assert.Equal(new[] { "-y", "@scope/server", "arg1" }, result.Args);
    Assert.Empty(result.EnvironmentVariables);
    Assert.Equal(McpTransportType.Stdio, result.SuggestedTransport);
  }

  [Fact]
  public void Parses_env_prefix()
  {
    ParsedCommand result = CommandLineParser.Parse("FOO=1 BAR=hello npx server");

    Assert.Equal("npx", result.Command);
    Assert.Equal(new[] { "server" }, result.Args);
    Assert.Equal("1", result.EnvironmentVariables["FOO"]);
    Assert.Equal("hello", result.EnvironmentVariables["BAR"]);
  }

  [Fact]
  public void Keeps_docker_run_verbatim()
  {
    ParsedCommand result = CommandLineParser.Parse("docker run --rm -i mcp/filesystem /data");

    Assert.Equal("docker", result.Command);
    Assert.Equal(new[] { "run", "--rm", "-i", "mcp/filesystem", "/data" }, result.Args);
  }

  [Fact]
  public void Does_not_extract_env_from_docker_e_flag()
  {
    ParsedCommand result = CommandLineParser.Parse("docker run --rm -i -e FOO=bar mcp/image");

    Assert.Empty(result.EnvironmentVariables);
    Assert.Contains("-e", result.Args);
    Assert.Contains("FOO=bar", result.Args);
  }

  [Fact]
  public void Preserves_quoted_args()
  {
    ParsedCommand result = CommandLineParser.Parse("npx server --dsn \"postgres://u:p@h/db?sslmode=disable\"");

    Assert.Equal("npx", result.Command);
    Assert.Contains("postgres://u:p@h/db?sslmode=disable", result.Args);
  }

  [Fact]
  public void Joins_line_continuations()
  {
    string cmd = "npx \\\n  -y \\\n  @scope/server arg";

    ParsedCommand result = CommandLineParser.Parse(cmd);

    Assert.Equal("npx", result.Command);
    Assert.Equal(new[] { "-y", "@scope/server", "arg" }, result.Args);
  }

  [Fact]
  public void Joins_multiline_docker_run()
  {
    string cmd = """
      docker run --rm --init \
         --name dbhub \
         --publish 8080:8080 \
         bytebase/dbhub \
         --transport http \
         --port 8080 \
         --dsn "postgres://user:password@localhost:5432/dbname?sslmode=disable"
      """;

    ParsedCommand result = CommandLineParser.Parse(cmd);

    Assert.Equal("docker", result.Command);
    Assert.Contains("run", result.Args);
    Assert.Contains("--name", result.Args);
    Assert.Contains("dbhub", result.Args);
    Assert.Contains("bytebase/dbhub", result.Args);
    Assert.Contains("postgres://user:password@localhost:5432/dbname?sslmode=disable", result.Args);
  }

  [Fact]
  public void Handles_empty_input()
  {
    ParsedCommand result = CommandLineParser.Parse("");

    Assert.Equal(string.Empty, result.Command);
    Assert.Empty(result.Args);
  }

  [Fact]
  public void Handles_whitespace_only_input()
  {
    ParsedCommand result = CommandLineParser.Parse("   \t  \n  ");

    Assert.Equal(string.Empty, result.Command);
  }

  [Fact]
  public void Handles_single_quoted_args()
  {
    ParsedCommand result = CommandLineParser.Parse("npx server --msg 'hello world'");

    Assert.Equal("npx", result.Command);
    Assert.Contains("hello world", result.Args);
  }
}
