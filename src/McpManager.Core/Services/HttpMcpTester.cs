using System.Net.Http;
using McpManager.Core.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpManager.Core.Services;

public interface IHttpMcpTester
{
  Task<HttpMcpTestResult> TestInitializeAsync(
    string url,
    McpTransportType transportType,
    Dictionary<string, string>? httpHeaders = null,
    CancellationToken cancellationToken = default);
}

public sealed record HttpMcpTestResult(
  bool Success,
  string StatusMessage,
  string ResultText,
  string? ServerName = null,
  string? ServerVersion = null,
  string? ProtocolVersion = null);

public sealed class HttpMcpTester : IHttpMcpTester
{
  public async Task<HttpMcpTestResult> TestInitializeAsync(
    string url,
    McpTransportType transportType,
    Dictionary<string, string>? httpHeaders = null,
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

      HttpClient httpClient = new();
      httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("McpManager/1.0");

      if (httpHeaders is { Count: > 0 })
      {
        foreach ((string key, string value) in httpHeaders)
        {
          httpClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
        }
      }

      HttpClientTransportOptions httpOptions = new()
      {
        Endpoint = new Uri(url),
        TransportMode = mode,
      };

      HttpClientTransport transport = new(httpOptions, httpClient, null!, true);

      await using McpClient client = await McpClient.CreateAsync(
        transport, cancellationToken: linkedCts.Token);

      Implementation? serverInfo = McpClientMetadata.ReadServerInfo(client);
      string identity = serverInfo == null
        ? "Server identity not provided."
        : $"📦 Server: {serverInfo.Name} v{serverInfo.Version}";
      string resultText = $"✅ MCP Server Connected! (SDK)\n\n{identity}\nMCP protocol: {client.NegotiatedProtocolVersion}";
      return new HttpMcpTestResult(true, serverInfo == null ? "MCP OK" : $"MCP OK: {serverInfo.Name}",
        resultText, serverInfo?.Name, serverInfo?.Version, client.NegotiatedProtocolVersion);
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
