using McpManager.Core.Models;
using ModelContextProtocol.Client;

namespace McpManager.Core.Services;

public interface IHttpMcpTester
{
  Task<HttpMcpTestResult> TestInitializeAsync(
    string url,
    McpTransportType transportType,
    CancellationToken cancellationToken = default);
}

public sealed record HttpMcpTestResult(
  bool Success,
  string StatusMessage,
  string ResultText,
  string? ServerName = null,
  string? ServerVersion = null);

public sealed class HttpMcpTester : IHttpMcpTester
{
  public async Task<HttpMcpTestResult> TestInitializeAsync(
    string url,
    McpTransportType transportType,
    CancellationToken cancellationToken = default)
  {
    try
    {
      using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(10));
      using CancellationTokenSource linkedCts =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

      HttpTransportMode mode = transportType switch
      {
        McpTransportType.Sse => HttpTransportMode.Sse,
        McpTransportType.StreamableHttp => HttpTransportMode.StreamableHttp,
        _ => HttpTransportMode.AutoDetect,
      };

      HttpClientTransport transport = new(new HttpClientTransportOptions
      {
        Endpoint = new Uri(url),
        TransportMode = mode,
      });

      await using McpClient client = await McpClient.CreateAsync(
        transport, cancellationToken: linkedCts.Token);

      string? serverName = client.ServerInfo?.Name;
      string? serverVersion = client.ServerInfo?.Version;

      if (!string.IsNullOrWhiteSpace(serverName))
      {
        string resultText = $"✅ MCP Server Connected! (SDK)\n\n📦 Server: {serverName}" +
                            (string.IsNullOrEmpty(serverVersion) ? string.Empty : $" v{serverVersion}");

        return new HttpMcpTestResult(true, $"MCP OK: {serverName}", resultText, serverName, serverVersion);
      }

      return new HttpMcpTestResult(false, "MCP test: no server info", "❓ Connected but no server info returned.");
    }
    catch (OperationCanceledException)
    {
      return new HttpMcpTestResult(
        false,
        "MCP test timed out",
        "❌ Connection timed out (10s)\n\nThe server may not be running or the URL may be incorrect.");
    }
    catch (Exception ex)
    {
      return new HttpMcpTestResult(false, $"MCP error: {ex.Message}", $"❌ MCP Error: {ex.Message}");
    }
  }
}
