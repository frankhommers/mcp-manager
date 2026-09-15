using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class BridgeCommandResolverTests
{
  [Fact]
  public void Expands_configured_placeholders_without_splitting_values()
  {
    ResolvedCommand result = BridgeCommandResolver.Resolve(
      "'/Applications/MCP Proxy/mcp-proxy' {args} {headerArgs} --transport streamablehttp {url}",
      "https://example.test/mcp",
      "--timeout 30 --label 'My server'",
      "--headers {key} {value}",
      new Dictionary<string, string>
      {
        ["Authorization"] = "Bearer secret token",
        ["X-Custom"] = "value with spaces",
      });

    Assert.Equal("/Applications/MCP Proxy/mcp-proxy", result.Command);
    Assert.Equal(
      [
        "--timeout",
        "30",
        "--label",
        "My server",
        "--headers",
        "Authorization",
        "Bearer secret token",
        "--headers",
        "X-Custom",
        "value with spaces",
        "--transport",
        "streamablehttp",
        "https://example.test/mcp",
      ],
      result.Arguments);
    Assert.Equal([6, 9], result.SensitiveArgumentIndexes.Order());
  }

  [Fact]
  public void Omits_headers_when_custom_template_has_no_headers_placeholder()
  {
    ResolvedCommand result = BridgeCommandResolver.Resolve(
      "custom-bridge {url}",
      "https://example.test/mcp",
      null,
      "--headers {key} {value}",
      new Dictionary<string, string> { ["Authorization"] = "secret" });

    Assert.Equal(["https://example.test/mcp"], result.Arguments);
  }

  [Theory]
  [InlineData("-H '{key}: {value}'", "Authorization: Bearer secret token")]
  [InlineData("--header={key}={value}", "--header=Authorization=Bearer secret token")]
  public void Expands_configurable_header_argument_template(string template, string expectedArgument)
  {
    ResolvedCommand result = BridgeCommandResolver.Resolve(
      "custom-bridge {headerArgs} {url}",
      "https://example.test/mcp",
      null,
      template,
      new Dictionary<string, string> { ["Authorization"] = "Bearer secret token" });

    Assert.Contains(expectedArgument, result.Arguments);
    Assert.Single(result.SensitiveArgumentIndexes);
  }

  [Fact]
  public void Supports_legacy_headers_placeholder()
  {
    ResolvedCommand result = BridgeCommandResolver.Resolve(
      "custom-bridge {headers} {url}",
      "https://example.test/mcp",
      null,
      "-H {key}:{value}",
      new Dictionary<string, string> { ["X-Key"] = "secret" });

    Assert.Equal(["-H", "X-Key:secret", "https://example.test/mcp"], result.Arguments);
  }
}
