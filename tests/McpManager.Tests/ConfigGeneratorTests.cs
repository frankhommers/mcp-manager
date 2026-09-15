using System.Text.Json.Nodes;
using McpManager.Core.ConfigGenerators;
using McpManager.Core.Models;
using McpManager.Core.Services;
using Tomlyn;
using Tomlyn.Model;

namespace McpManager.Tests;

public class ConfigGeneratorTests
{
  [Fact]
  public void ClaudeCode_emits_remote_headers_without_stdio_environment()
  {
    JsonObject server = GetJsonServer(new ClaudeCodeConfigGenerator().GenerateConfig([CreateRemoteServer()]));

    Assert.Equal("Bearer secret token", server["headers"]?["Authorization"]?.GetValue<string>());
    Assert.Null(server["env"]);
  }

  [Fact]
  public void Cursor_emits_remote_headers_without_stdio_environment()
  {
    JsonObject server = GetJsonServer(new CursorConfigGenerator().GenerateConfig([CreateRemoteServer()]));

    Assert.Equal("Bearer secret token", server["headers"]?["Authorization"]?.GetValue<string>());
    Assert.Null(server["env"]);
  }

  [Fact]
  public void ClaudeDesktop_passes_headers_to_proxy_as_distinct_arguments()
  {
    McpServer model = CreateRemoteServer();
    JsonObject server = GetJsonServer(new ClaudeDesktopConfigGenerator().GenerateConfig([model]));
    string[] arguments = server["args"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray();

    Assert.Equal(
      [
        "--headers",
        "Authorization",
        "Bearer secret token",
        "--transport",
        "streamablehttp",
        "https://example.test/mcp",
      ],
      arguments);
    JsonObject environment = server["env"]!.AsObject();
    Assert.Single(environment);
    Assert.Equal(model.Id.ToString("D"), environment[ManagedServerIdentity.EnvironmentKey]!.GetValue<string>());
  }

  [Fact]
  public void ClaudeDesktop_uses_configured_header_argument_template()
  {
    ClaudeDesktopConfigGenerator generator = new()
    {
      BridgeCommandStreamableHttp = "custom-bridge {headerArgs} {url}",
      BridgeHeaderArgumentTemplate = "-H '{key}: {value}'",
    };
    JsonObject server = GetJsonServer(generator.GenerateConfig([CreateRemoteServer()]));
    string[] arguments = server["args"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray();

    Assert.Equal(
      ["-H", "Authorization: Bearer secret token", "https://example.test/mcp"],
      arguments);
  }

  [Fact]
  public void OpenCode_emits_remote_headers_without_stdio_environment()
  {
    JsonObject root = JsonNode.Parse(new OpenCodeConfigGenerator().GenerateConfig([CreateRemoteServer()]))!.AsObject();
    JsonObject server = root["mcp"]!["remote"]!.AsObject();

    Assert.Equal("Bearer secret token", server["headers"]?["Authorization"]?.GetValue<string>());
    Assert.Null(server["environment"]);
  }

  [Fact]
  public void Windsurf_uses_native_remote_transport_with_headers()
  {
    JsonObject server = GetJsonServer(new WindsurfConfigGenerator().GenerateConfig([CreateRemoteServer()]));

    Assert.Equal("https://example.test/mcp", server["serverUrl"]?.GetValue<string>());
    Assert.Equal("Bearer secret token", server["headers"]?["Authorization"]?.GetValue<string>());
    Assert.Null(server["command"]);
    Assert.Null(server["env"]);
  }

  [Fact]
  public void VsCode_uses_root_servers_and_omits_remote_environment()
  {
    JsonObject root = JsonNode.Parse(new VsCodeConfigGenerator().GenerateConfig([CreateRemoteServer()]))!.AsObject();
    JsonObject server = root["servers"]!["remote"]!.AsObject();

    Assert.Null(root["mcp"]);
    Assert.Equal("Bearer secret token", server["headers"]?["Authorization"]?.GetValue<string>());
    Assert.Null(server["env"]);
  }

  [Fact]
  public void Codex_emits_http_headers_without_stdio_environment()
  {
    TomlTable root = TomlSerializer.Deserialize<TomlTable>(
      new CodexConfigGenerator().GenerateConfig([CreateRemoteServer()]))!;
    TomlTable server = (TomlTable)((TomlTable)root["mcp_servers"]!)["remote"]!;
    TomlTable headers = (TomlTable)server["http_headers"]!;

    Assert.Equal("Bearer secret token", headers["Authorization"]);
    Assert.False(server.ContainsKey("env"));
  }

  [Fact]
  public void Stdio_environment_is_still_exported()
  {
    McpServer stdio = new()
    {
      Name = "local",
      TransportType = McpTransportType.Stdio,
      Command = "server",
      EnvironmentVariables = new Dictionary<string, string> { ["API_KEY"] = "value" },
    };
    JsonObject server = GetJsonServer(new ClaudeCodeConfigGenerator().GenerateConfig([stdio]), "local");

    Assert.Equal("value", server["env"]?["API_KEY"]?.GetValue<string>());
  }

  private static McpServer CreateRemoteServer() => new()
  {
    Name = "remote",
    TransportType = McpTransportType.StreamableHttp,
    Url = "https://example.test/mcp",
    EnvironmentVariables = new Dictionary<string, string> { ["SHOULD_NOT_EXPORT"] = "value" },
    HttpHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer secret token" },
  };

  private static JsonObject GetJsonServer(string json, string serverName = "remote")
  {
    JsonObject root = JsonNode.Parse(json)!.AsObject();
    return root["mcpServers"]![serverName]!.AsObject();
  }
}
