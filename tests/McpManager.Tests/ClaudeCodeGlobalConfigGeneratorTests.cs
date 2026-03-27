using System.Text.Json;
using System.Text.Json.Nodes;
using McpManager.Core.ConfigGenerators;
using McpManager.Core.Models;

namespace McpManager.Tests;

public sealed class ClaudeCodeGlobalConfigGeneratorTests
{
  [Fact]
  public void GenerateConfig_MergesIntoExistingFile()
  {
    string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);
    string existingPath = Path.Combine(tempDir, ".claude.json");

    try
    {
      File.WriteAllText(existingPath, JsonSerializer.Serialize(new JsonObject
      {
        ["numStartups"] = 42,
        ["autoUpdates"] = true,
        ["projects"] = new JsonObject(),
      }, new JsonSerializerOptions { WriteIndented = true }));

      ClaudeCodeGlobalConfigGenerator gen = new()
      {
        ExistingConfigPath = existingPath,
      };

      List<McpServer> servers =
      [
        new McpServer
        {
          Id = Guid.NewGuid(),
          Name = "test-server",
          TransportType = McpTransportType.Stdio,
          Command = "npx",
          Args = ["-y", "@test/server"],
          EnvironmentVariables = new Dictionary<string, string> { ["KEY"] = "val" },
        },
      ];

      string result = gen.GenerateConfig(servers);
      JsonObject root = JsonNode.Parse(result)!.AsObject();

      Assert.Equal(42, root["numStartups"]!.GetValue<int>());
      Assert.True(root["autoUpdates"]!.GetValue<bool>());
      Assert.NotNull(root["projects"]);

      JsonObject mcpServers = root["mcpServers"]!.AsObject();
      Assert.True(mcpServers.ContainsKey("test-server"));
      Assert.Equal("npx", mcpServers["test-server"]!["command"]!.GetValue<string>());
      Assert.Equal("val", mcpServers["test-server"]!["env"]!["KEY"]!.GetValue<string>());
    }
    finally
    {
      Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void GenerateConfig_NoExistingFile_CreatesValidOutput()
  {
    string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);
    string nonExistentPath = Path.Combine(tempDir, ".claude.json");

    try
    {
      ClaudeCodeGlobalConfigGenerator gen = new()
      {
        ExistingConfigPath = nonExistentPath,
      };

      List<McpServer> servers =
      [
        new McpServer
        {
          Id = Guid.NewGuid(),
          Name = "my-server",
          TransportType = McpTransportType.Http,
          Url = "https://example.com/mcp",
        },
      ];

      string result = gen.GenerateConfig(servers);
      JsonObject root = JsonNode.Parse(result)!.AsObject();

      JsonObject mcpServers = root["mcpServers"]!.AsObject();
      Assert.True(mcpServers.ContainsKey("my-server"));
      Assert.Equal("http", mcpServers["my-server"]!["type"]!.GetValue<string>());
      Assert.Equal("https://example.com/mcp", mcpServers["my-server"]!["url"]!.GetValue<string>());
    }
    finally
    {
      Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void GenerateConfig_ReplacesExistingMcpServers()
  {
    string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);
    string existingPath = Path.Combine(tempDir, ".claude.json");

    try
    {
      File.WriteAllText(existingPath, JsonSerializer.Serialize(new JsonObject
      {
        ["mcpServers"] = new JsonObject
        {
          ["old-server"] = new JsonObject { ["command"] = "old" },
        },
      }, new JsonSerializerOptions { WriteIndented = true }));

      ClaudeCodeGlobalConfigGenerator gen = new()
      {
        ExistingConfigPath = existingPath,
      };

      List<McpServer> servers =
      [
        new McpServer
        {
          Id = Guid.NewGuid(),
          Name = "new-server",
          TransportType = McpTransportType.Stdio,
          Command = "new-cmd",
        },
      ];

      string result = gen.GenerateConfig(servers);
      JsonObject root = JsonNode.Parse(result)!.AsObject();
      JsonObject mcpServers = root["mcpServers"]!.AsObject();

      Assert.False(mcpServers.ContainsKey("old-server"));
      Assert.True(mcpServers.ContainsKey("new-server"));
    }
    finally
    {
      Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void HasCorrectMetadata()
  {
    ClaudeCodeGlobalConfigGenerator gen = new();
    Assert.Equal("Claude Code (Global)", gen.ClientName);
    Assert.Equal(".claude.json", gen.ConfigFileName);
    Assert.Null(gen.ConfigSubFolder);
  }
}
