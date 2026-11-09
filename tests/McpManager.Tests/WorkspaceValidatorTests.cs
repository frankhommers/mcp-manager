using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class WorkspaceValidatorTests
{
  private static readonly string AbsolutePath = Path.GetFullPath(Path.GetTempPath());

  [Fact]
  public void Valid_workspace_has_no_error()
  {
    McpRegistry registry = new()
    {
      Servers =
      [
        new McpServer { Name = "local", Command = "npx" },
        new McpServer { Name = "remote", TransportType = McpTransportType.Sse, Url = "https://example.test/sse" },
      ],
      TargetFolders =
      [
        new TargetFolder { Name = "Project", Path = AbsolutePath },
        new TargetFolder { Name = "Global", IsGlobal = true, EnabledClients = TargetClientFlags.Codex },
        new TargetFolder { Name = "Clipboard", IsClipboard = true, EnabledClients = TargetClientFlags.None },
        new TargetFolder { Name = "Quick Export", IsQuickExport = true, EnabledClients = TargetClientFlags.None },
      ],
    };
    Assert.Null(WorkspaceValidator.Validate(registry));
  }

  [Theory]
  [InlineData(McpTransportType.Stdio, null, null, "Server 'broken' needs a command.")]
  [InlineData(McpTransportType.Stdio, " ", null, "Server 'broken' needs a command.")]
  [InlineData(McpTransportType.Http, null, "ftp://example.test", "Server 'broken' needs an http(s) URL.")]
  [InlineData(McpTransportType.Sse, null, "example.test/sse", "Server 'broken' needs an http(s) URL.")]
  [InlineData(McpTransportType.StreamableHttp, null, "", "Server 'broken' needs an http(s) URL.")]
  public void Invalid_server_is_named(McpTransportType transport, string? command, string? url, string expected)
  {
    McpRegistry registry = new()
    {
      Servers = [new McpServer { Name = "broken", TransportType = transport, Command = command, Url = url }],
    };
    Assert.Equal(expected, WorkspaceValidator.Validate(registry));
  }

  [Fact]
  public void Place_needs_a_harness_and_an_absolute_path()
  {
    McpRegistry noHarness = new()
    {
      TargetFolders = [new TargetFolder { Name = "Empty", Path = AbsolutePath, EnabledClients = TargetClientFlags.None }],
    };
    McpRegistry relative = new() { TargetFolders = [new TargetFolder { Name = "Relative", Path = "repos/x" }] };

    Assert.Equal("Place 'Empty' needs at least one harness.", WorkspaceValidator.Validate(noHarness));
    Assert.Equal("Place 'Relative' needs an absolute folder path.", WorkspaceValidator.Validate(relative));
  }
}
