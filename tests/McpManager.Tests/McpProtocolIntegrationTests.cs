using System.Text.Json.Nodes;
using McpManager.Core.Models;
using McpManager.Core.Services;
using McpManager.TestServer;
using McpManager.ViewModels;

namespace McpManager.Tests;

public sealed class McpProtocolIntegrationTests : IDisposable
{
  private readonly string _directory = Directory.CreateTempSubdirectory("mcp-protocol-").FullName;
  private string RecordPath => Path.Combine(_directory, "requests.jsonl");
  private static string FixtureAssembly => typeof(FixtureProcess).Assembly.Location;

  [Theory]
  [InlineData("2026-07-28", true)]
  [InlineData("2026-07-28", false)]
  [InlineData("2025-11-25", true)]
  public async Task Stdio_connects_using_negotiated_protocol(string version, bool includeIdentity)
  {
    McpServer server = CreateStdioServer(version);
    if (!includeIdentity) server.Args.Add("--omit-identity");
    StdioMcpTestResult result = await new StdioMcpTester().TestInitializeAsync(server);
    Assert.True(result.Success, result.ResultText);
    Assert.Equal(version, result.ProtocolVersion);
    Assert.Equal(includeIdentity ? "Protocol fixture" : null, result.ServerName);
    JsonObject[] requests = ReadRequests();
    Assert.Equal("server/discover", requests[0]["method"]!.GetValue<string>());
    Assert.Equal(version != ProtocolFixture.LatestVersion, HasInitialize(requests));
  }

  [Fact]
  public async Task Bridge_test_uses_sdk_discovery_and_keeps_credentials_out_of_displayed_command()
  {
    McpServer server = new()
    {
      Name = "remote", Url = "https://fixture.invalid/mcp", TransportType = McpTransportType.Http,
      HttpHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer fixture private token" },
    };
    MainWindowViewModel vm = await CreateManagerAsync(server);
    vm.SelectedServer = Assert.Single(vm.Servers);
    vm.BridgeCommandHttp = $"dotnet \"{FixtureAssembly}\" {ProtocolFixture.LatestVersion} \"{RecordPath}\" {{headerArgs}} {{url}}";
    await vm.TestMcpBridgeCommand.ExecuteAsync(null);

    Assert.StartsWith("MCP OK", vm.StatusMessage);
    Assert.Contains(ProtocolFixture.LatestVersion, vm.McpTestResult);
    Assert.Contains("***", vm.McpTestResult);
    Assert.DoesNotContain("fixture private token", vm.McpTestResult);
    Assert.False(vm.IsLoading);
    Assert.False(HasInitialize(ReadRequests()));
  }

  [Fact]
  public async Task Target_tool_discovery_reads_all_modern_http_pages_and_preserves_choices()
  {
    await using McpHttpFixture fixture = new(ProtocolFixture.LatestVersion);
    McpServer server = new()
    {
      Name = "remote", Url = fixture.Url, TransportType = McpTransportType.StreamableHttp,
      AlwaysAllow = ["read_note"],
      HttpHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer fixture-token" },
    };
    MainWindowViewModel vm = await CreateManagerAsync(server);
    TargetFolderViewModel target = vm.TargetFolders.Single(t => t.Name == "Fixture target");
    vm.SelectedTarget = target;
    ServerSelectionViewModel selection = Assert.Single(target.ServerSelections);
    await vm.FetchToolsForServerCommand.ExecuteAsync(selection);

    Assert.Equal(["read_note", "write_note"], selection.VisibleTools.Select(t => t.ToolName));
    Assert.True(selection.VisibleTools[0].IsAllowed);
    Assert.False(selection.VisibleTools[1].IsAllowed);
    Assert.Equal(2, fixture.Requests.Count(r => r["method"]!.GetValue<string>() == "tools/list"));
    Assert.False(HasInitialize(fixture.Requests));
    Assert.All(fixture.ProtocolHeaders, header => Assert.Equal(ProtocolFixture.LatestVersion, header));
    Assert.All(fixture.AuthHeaders, header => Assert.Equal("Bearer fixture-token", header));
    Assert.False(selection.IsFetchingTools);
  }

  [Fact]
  public async Task Target_tool_discovery_uses_modern_stdio_requests()
  {
    McpServer server = CreateStdioServer(ProtocolFixture.LatestVersion);
    MainWindowViewModel vm = await CreateManagerAsync(server);
    vm.SelectedTarget = vm.TargetFolders.Single(t => t.Name == "Fixture target");
    ServerSelectionViewModel selection = Assert.Single(vm.SelectedTarget.ServerSelections);
    await vm.FetchToolsForServerCommand.ExecuteAsync(selection);

    Assert.Equal(["read_note", "write_note"], selection.VisibleTools.Select(t => t.ToolName));
    JsonObject[] requests = ReadRequests();
    Assert.Equal(2, requests.Count(r => r["method"]!.GetValue<string>() == "tools/list"));
    Assert.False(HasInitialize(requests));
  }

  private McpServer CreateStdioServer(string version) => new()
  {
    Name = "stdio-fixture", Command = "dotnet", Args = [FixtureAssembly, version, RecordPath],
  };

  private JsonObject[] ReadRequests() => File.ReadAllLines(RecordPath)
    .Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();

  private static bool HasInitialize(IEnumerable<JsonObject> requests) =>
    requests.Any(r => r["method"]!.GetValue<string>() == "initialize");

  private async Task<MainWindowViewModel> CreateManagerAsync(McpServer server)
  {
    RegistryService registryService = new(Path.Combine(_directory, "registry.json"));
    McpRegistry registry = new()
    {
      Servers = [server],
      TargetFolders = [new TargetFolder
      {
        Name = "Fixture target", Path = _directory, EnabledServers = [server.Id],
      }],
      Settings = new GlobalSettings { CodexConfigPath = Path.Combine(_directory, "global.toml") },
    };
    await registryService.SaveAsync(registry);
    MainWindowViewModel vm = new(registryService, new ConfigExportService(), new ConfigImportService(),
      new HttpMcpTester(), new StdioMcpTester(), new TransportDetectionService(), new FixtureShellEnvironment());
    await vm.InitializeAsync();
    return vm;
  }

  public void Dispose() => Directory.Delete(_directory, recursive: true);

  private sealed class FixtureShellEnvironment : IShellEnvironmentService
  {
    public string? ResolvedPath => null;
    public Task ResolveAsync() => Task.CompletedTask;
  }
}
