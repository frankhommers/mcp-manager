using System.Text.Json.Nodes;
using McpManager.Core.Models;
using McpManager.Core.Services;
using Tomlyn;
using Tomlyn.Model;

namespace McpManager.Tests;

public class TargetServerImportTests
{
  [Theory]
  [InlineData(TargetClientFlags.ClaudeCode)]
  [InlineData(TargetClientFlags.ClaudeCodeGlobal)]
  [InlineData(TargetClientFlags.ClaudeDesktop)]
  [InlineData(TargetClientFlags.Cursor)]
  [InlineData(TargetClientFlags.Windsurf)]
  [InlineData(TargetClientFlags.VsCode)]
  [InlineData(TargetClientFlags.OpenCode)]
  public void Imports_local_json_servers_with_arguments_environment_directory_tools_and_identity(TargetClientFlags client)
  {
    Guid id = Guid.NewGuid();
    bool openCode = client == TargetClientFlags.OpenCode;
    JsonObject config = new()
    {
      ["command"] = openCode ? new JsonArray("test-command", "--flag", "value with spaces") : JsonValue.Create("test-command"),
      ["cwd"] = "/example/working-directory",
      ["alwaysAllow"] = new JsonArray("read_tool"),
      [openCode ? "environment" : "env"] = new JsonObject
      {
        ["SETTING"] = "test-value",
        [ManagedServerIdentity.EnvironmentKey] = id.ToString(),
      },
    };
    if (openCode || client == TargetClientFlags.VsCode)
    {
      config["type"] = openCode ? "local" : "stdio";
    }

    if (!openCode)
    {
      config["args"] = new JsonArray("--flag", "value with spaces");
    }

    string original = config.ToJsonString();
    McpServer server = new ConfigImportService().ImportServer(
      new ExistingTargetServer("test--server", id, true, client, "/example/config.json", original));

    Assert.Equal(id, server.Id);
    Assert.Equal("test--server", server.Name);
    Assert.Equal("Test Server", server.DisplayName);
    Assert.Equal(McpTransportType.Stdio, server.TransportType);
    Assert.Equal("test-command", server.Command);
    Assert.Equal(["--flag", "value with spaces"], server.Args);
    Assert.Equal("/example/working-directory", server.WorkingDirectory);
    Assert.Equal("test-value", server.EnvironmentVariables["SETTING"]);
    Assert.False(server.EnvironmentVariables.ContainsKey(ManagedServerIdentity.EnvironmentKey));
    Assert.Equal(["read_tool"], server.AlwaysAllow);
    Assert.Equal(original, config.ToJsonString());
  }

  [Theory]
  [InlineData(TargetClientFlags.ClaudeCode, "http", McpTransportType.Http)]
  [InlineData(TargetClientFlags.ClaudeCodeGlobal, "sse", McpTransportType.Sse)]
  [InlineData(TargetClientFlags.ClaudeDesktop, "streamable-http", McpTransportType.StreamableHttp)]
  [InlineData(TargetClientFlags.Cursor, null, McpTransportType.StreamableHttp)]
  [InlineData(TargetClientFlags.Windsurf, null, McpTransportType.StreamableHttp)]
  [InlineData(TargetClientFlags.VsCode, "http", McpTransportType.Http)]
  [InlineData(TargetClientFlags.OpenCode, "remote", McpTransportType.StreamableHttp)]
  public void Imports_remote_json_urls_headers_and_identity(
    TargetClientFlags client, string? type, McpTransportType expectedType)
  {
    Guid id = Guid.NewGuid();
    JsonObject config = new()
    {
      [client == TargetClientFlags.Windsurf ? "serverUrl" : "url"] = "https://example.test/mcp",
      ["headers"] = new JsonObject
      {
        ["X-Test-Header"] = "test-value",
        ["x-mcp-manager-id"] = id.ToString(),
      },
    };
    if (type != null)
    {
      config["type"] = type;
    }

    McpServer server = new ConfigImportService().ImportServer(
      new ExistingTargetServer("remote", id, true, client, "/example/config.json", config.ToJsonString()));

    Assert.Equal(id, server.Id);
    Assert.Equal(expectedType, server.TransportType);
    Assert.Equal("https://example.test/mcp", server.Url);
    Assert.Equal("test-value", Assert.Single(server.HttpHeaders).Value);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void Imports_codex_local_and_remote_servers(bool remote)
  {
    Guid id = Guid.NewGuid();
    TomlTable config = new()
    {
      ["enabled_tools"] = new TomlArray { "read_tool" },
      [remote ? "http_headers" : "env"] = new TomlTable
      {
        [remote ? ManagedServerIdentity.HeaderKey : ManagedServerIdentity.EnvironmentKey] = id.ToString(),
        ["SETTING"] = "test-value",
      },
    };
    if (remote)
    {
      config["url"] = "https://example.test/mcp";
    }
    else
    {
      config["command"] = "test-command";
      config["args"] = new TomlArray { "--flag", "value with spaces" };
      config["cwd"] = "/example/working-directory";
    }

    string content = TomlSerializer.Serialize(new TomlTable { ["server"] = config });
    McpServer server = new ConfigImportService().ImportServer(
      new ExistingTargetServer("codex-server", id, false, TargetClientFlags.Codex, "/example/config.toml", content));

    Assert.Equal(id, server.Id);
    Assert.Equal(["read_tool"], server.AlwaysAllow);
    if (remote)
    {
      Assert.Equal(McpTransportType.StreamableHttp, server.TransportType);
      Assert.Equal("https://example.test/mcp", server.Url);
      Assert.Equal("test-value", Assert.Single(server.HttpHeaders).Value);
    }
    else
    {
      Assert.Equal(McpTransportType.Stdio, server.TransportType);
      Assert.Equal("test-command", server.Command);
      Assert.Equal(["--flag", "value with spaces"], server.Args);
      Assert.Equal("/example/working-directory", server.WorkingDirectory);
      Assert.Equal("test-value", Assert.Single(server.EnvironmentVariables).Value);
    }
  }

  [Theory]
  [InlineData("{}")]
  [InlineData("{\"type\":\"http\"}")]
  [InlineData("{\"type\":\"unsupported\",\"url\":\"https://example.test\"}")]
  public void Rejects_entries_without_a_supported_connection(string config)
  {
    ExistingTargetServer source = new("invalid", null, true, TargetClientFlags.ClaudeCode, "config.json", config);
    Assert.Throws<InvalidDataException>(() => new ConfigImportService().ImportServer(source));
  }
}
