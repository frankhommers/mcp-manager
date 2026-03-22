using McpManager.Core.Models;
using ModelContextProtocol.Client;

namespace McpManager.Core.Services;

public interface ITransportDetectionService
{
  Task<TransportDetectionResult> DetectTransportTypeAsync(string url, CancellationToken cancellationToken = default);
}

public record TransportDetectionResult(
  bool Success,
  McpTransportType? DetectedType,
  string? Message,
  string? ServerName = null,
  string? ServerVersion = null,
  string? RawResponse = null
);

public class TransportDetectionService : ITransportDetectionService
{
  public async Task<TransportDetectionResult> DetectTransportTypeAsync(
    string url,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(url))
    {
      return new TransportDetectionResult(false, null, "URL is empty");
    }

    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
    {
      return new TransportDetectionResult(false, null, "Invalid URL format");
    }

    List<string> results = [$"Testing URL: {url}", ""];

    // Try each HTTP transport mode using the official MCP SDK.
    // Order: Streamable HTTP first (modern), then SSE (legacy).
    (McpTransportType type, HttpTransportMode mode, string label)[] modes =
    [
      (McpTransportType.StreamableHttp, HttpTransportMode.StreamableHttp, "Streamable HTTP"),
      (McpTransportType.Sse, HttpTransportMode.Sse, "SSE"),
    ];

    foreach ((McpTransportType transportType, HttpTransportMode httpMode, string label) in modes)
    {
      results.Add($"=== Testing {label} Transport (SDK) ===");
      TransportDetectionResult result = await TryConnectWithSdkAsync(
        uri, transportType, httpMode, label, results, cancellationToken);

      if (result.Success)
      {
        return result;
      }

      results.Add("");
    }

    results.Add("=== Result ===");
    results.Add("Could not detect MCP transport type.");
    results.Add("Make sure the server is running and the URL is correct.");

    return new TransportDetectionResult(
      false,
      null,
      "Could not connect via any transport type",
      RawResponse: string.Join("\n", results)
    );
  }

  private static async Task<TransportDetectionResult> TryConnectWithSdkAsync(
    Uri uri,
    McpTransportType transportType,
    HttpTransportMode httpMode,
    string label,
    List<string> results,
    CancellationToken cancellationToken)
  {
    try
    {
      results.Add($"Connecting with {label} mode...");

      using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(10));
      using CancellationTokenSource linkedCts =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

      HttpClientTransport transport = new(new HttpClientTransportOptions
      {
        Endpoint = uri,
        TransportMode = httpMode,
      });

      await using McpClient client = await McpClient.CreateAsync(
        transport, cancellationToken: linkedCts.Token);

      string? serverName = client.ServerInfo?.Name;
      string? serverVersion = client.ServerInfo?.Version;

      if (!string.IsNullOrWhiteSpace(serverName))
      {
        results.Add($"Connected successfully!");
        results.Add($"Server: {serverName} v{serverVersion}");
        results.Add("");
        results.Add("=== Result ===");
        results.Add($"{label} Transport detected!");

        return new TransportDetectionResult(
          true,
          transportType,
          $"{label} verified - {serverName} v{serverVersion}",
          serverName,
          serverVersion,
          string.Join("\n", results)
        );
      }

      results.Add("Connected but no server info returned");
    }
    catch (OperationCanceledException)
    {
      results.Add($"{label}: Connection timed out (10s)");
    }
    catch (Exception ex)
    {
      results.Add($"{label}: {ex.Message}");
    }

    return new TransportDetectionResult(false, null, null);
  }
}
