using McpManager.Core.Models;
using McpManager.Core.Services;
using ModelContextProtocol.Client;

namespace McpManager.Tests;

public sealed class StdioMcpTesterTests
{
  [Fact]
  public void CreateTransportOptions_PreservesCommandArgumentsAndEnvironment()
  {
    McpServer server = new()
    {
      DisplayName = "Hopper",
      Command = "/Applications/Hopper Disassembler.app/Contents/MacOS/HopperMCPServer",
      Args = ["--value", "argument with spaces"],
      WorkingDirectory = "/tmp/working directory",
      EnvironmentVariables = new Dictionary<string, string>
      {
        ["MCP_VALUE"] = "value with spaces",
      },
    };

    StdioClientTransportOptions options = StdioMcpTester.CreateTransportOptions(server);

    Assert.Equal(server.Command, options.Command);
    Assert.Equal(server.Args, options.Arguments);
    Assert.Equal(server.WorkingDirectory, options.WorkingDirectory);
    Assert.Equal("value with spaces", options.EnvironmentVariables!["MCP_VALUE"]);
  }
}
