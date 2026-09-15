using System.Text;
using McpManager.Core.Models;
using ModelContextProtocol.Client;

namespace McpManager.Core.Services;

public interface IStdioMcpTester
{
  Task<StdioMcpTestResult> TestInitializeAsync(
    McpServer server,
    CancellationToken cancellationToken = default);
}

public sealed record StdioMcpTestResult(
  bool Success,
  string StatusMessage,
  string ResultText,
  string? ServerName = null,
  string? ServerVersion = null);

public sealed class StdioMcpTester : IStdioMcpTester
{
  public async Task<StdioMcpTestResult> TestInitializeAsync(
    McpServer server,
    CancellationToken cancellationToken = default)
  {
    StringBuilder standardError = new();

    try
    {
      using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(10));
      using CancellationTokenSource linkedCts =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

      StdioClientTransportOptions options = CreateTransportOptions(server, line =>
      {
        lock (standardError)
        {
          standardError.AppendLine(line);
        }
      });

      StdioClientTransport transport = new(options);
      await using McpClient client = await McpClient.CreateAsync(
        transport,
        cancellationToken: linkedCts.Token).ConfigureAwait(false);

      string? serverName = client.ServerInfo?.Name;
      string? serverVersion = client.ServerInfo?.Version;

      if (!string.IsNullOrWhiteSpace(serverName))
      {
        string resultText = $"✅ MCP Server Connected! (SDK)\n\n📦 Server: {serverName}" +
                            (string.IsNullOrEmpty(serverVersion) ? string.Empty : $" v{serverVersion}");

        return new StdioMcpTestResult(true, $"MCP OK: {serverName}", resultText, serverName, serverVersion);
      }

      return new StdioMcpTestResult(false, "MCP test: no server info", "❓ Connected but no server info returned.");
    }
    catch (OperationCanceledException)
    {
      return new StdioMcpTestResult(
        false,
        "MCP test timed out",
        "❌ Connection timed out (10s)\n\nThe server may not be running or the command may be incorrect.");
    }
    catch (Exception ex)
    {
      string stderr = standardError.ToString().Trim();
      string details = string.IsNullOrEmpty(stderr) ? string.Empty : $"\n\nStderr:\n{stderr}";
      return new StdioMcpTestResult(false, $"MCP error: {ex.Message}", $"❌ MCP Error: {ex.Message}{details}");
    }
  }

  internal static StdioClientTransportOptions CreateTransportOptions(
    McpServer server,
    Action<string>? standardErrorLines = null)
  {
    Dictionary<string, string?> environmentVariables = server.EnvironmentVariables
      .ToDictionary(pair => pair.Key, pair => (string?)pair.Value);

    return new StdioClientTransportOptions
    {
      Name = string.IsNullOrWhiteSpace(server.DisplayName) ? server.Name : server.DisplayName,
      Command = server.Command ?? string.Empty,
      Arguments = server.Args,
      WorkingDirectory = server.WorkingDirectory,
      EnvironmentVariables = environmentVariables.Count > 0 ? environmentVariables : null,
      StandardErrorLines = standardErrorLines,
    };
  }
}
