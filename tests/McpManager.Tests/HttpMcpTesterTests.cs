using McpManager.Core.Models;
using McpManager.Core.Services;
using McpManager.TestServer;

namespace McpManager.Tests;

public sealed class HttpMcpTesterTests
{
  [Theory]
  [InlineData("2026-07-28")]
  [InlineData("2025-11-25")]
  [InlineData("2024-11-05")]
  public async Task Connection_negotiates_the_server_protocol_and_reports_identity(string version)
  {
    await using McpHttpFixture fixture = new(version);
    HttpMcpTestResult result = await new HttpMcpTester().TestInitializeAsync(
      fixture.Url, McpTransportType.StreamableHttp,
      new Dictionary<string, string> { ["Authorization"] = "Bearer fixture-token" });

    Assert.True(result.Success, result.ResultText);
    Assert.Equal("Protocol fixture", result.ServerName);
    Assert.Equal("1.2.3", result.ServerVersion);
    Assert.Equal(version, result.ProtocolVersion);
    Assert.Contains(version, result.ResultText);
    Assert.Equal("server/discover", fixture.Requests.First()["method"]!.GetValue<string>());
    Assert.Equal(version != ProtocolFixture.LatestVersion,
      fixture.Requests.Any(r => r["method"]!.GetValue<string>() == "initialize"));
    Assert.All(fixture.AuthHeaders, header => Assert.Equal("Bearer fixture-token", header));
  }

  [Fact]
  public async Task Discovery_without_optional_identity_is_a_successful_connection()
  {
    await using McpHttpFixture fixture = new(ProtocolFixture.LatestVersion, includeIdentity: false);
    HttpMcpTestResult result = await new HttpMcpTester().TestInitializeAsync(
      fixture.Url, McpTransportType.StreamableHttp);
    Assert.True(result.Success, result.ResultText);
    Assert.Null(result.ServerName);
    Assert.Equal(ProtocolFixture.LatestVersion, result.ProtocolVersion);

    TransportDetectionResult detection = await new TransportDetectionService().DetectTransportTypeAsync(fixture.Url);
    Assert.True(detection.Success, detection.RawResponse);
    Assert.Equal(McpTransportType.StreamableHttp, detection.DetectedType);
    Assert.Contains(ProtocolFixture.LatestVersion, detection.RawResponse);
  }
}

public sealed class WindsurfGeneratorTests
{
  [Fact]
  public void WindsurfGenerator_HasCorrectFileName()
  {
    var gen = new McpManager.Core.ConfigGenerators.WindsurfConfigGenerator();
    Assert.Equal("mcp_config.json", gen.ConfigFileName);
    Assert.Equal("Windsurf", gen.ClientName);
    Assert.Null(gen.ConfigSubFolder);
  }

  [Fact]
  public void WindsurfGenerator_ViaInterface_HasCorrectFileName()
  {
    McpManager.Core.ConfigGenerators.IConfigGenerator gen = new McpManager.Core.ConfigGenerators.WindsurfConfigGenerator();
    Assert.Equal("mcp_config.json", gen.ConfigFileName);
  }
}
