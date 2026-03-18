using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using McpManager.Core.Models;

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
  private static readonly HttpClient _httpClient = new()
  {
    Timeout = TimeSpan.FromSeconds(15),
  };

  private const string McpInitializeRequest = """
                                              {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"MCP Manager Detector","version":"1.0.0"}}}
                                              """;

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

    List<string> results = new() {$"Testing URL: {url}", ""};

    // Step 1: Try SSE transport (GET with text/event-stream)
    results.Add("=== Testing SSE Transport ===");
    TransportDetectionResult sseResult = await TrySseTransportAsync(url, results, cancellationToken);
    if (sseResult.Success)
    {
      return sseResult;
    }

    // Step 2: Try Streamable HTTP (POST to /mcp style endpoint)
    results.Add("");
    results.Add("=== Testing Streamable HTTP Transport ===");
    TransportDetectionResult streamableResult = await TryStreamableHttpTransportAsync(url, results, cancellationToken);
    if (streamableResult.Success)
    {
      return streamableResult;
    }

    // Step 3: Try basic HTTP POST
    results.Add("");
    results.Add("=== Testing HTTP Transport ===");
    TransportDetectionResult httpResult = await TryHttpTransportAsync(url, results, cancellationToken);
    if (httpResult.Success)
    {
      return httpResult;
    }

    // No transport worked
    results.Add("");
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

  private async Task<TransportDetectionResult> TrySseTransportAsync(
    string url,
    List<string> results,
    CancellationToken cancellationToken)
  {
    try
    {
      results.Add($"GET {url}");
      results.Add("Accept: text/event-stream");

      using HttpRequestMessage request = new(HttpMethod.Get, url);
      request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

      // Use a shorter timeout for the initial connection
      using CancellationTokenSource connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      connectionCts.CancelAfter(TimeSpan.FromSeconds(5));

      using HttpResponseMessage response = await _httpClient.SendAsync(
        request,
        HttpCompletionOption.ResponseHeadersRead,
        connectionCts.Token);

      string? contentType = response.Content.Headers.ContentType?.MediaType;
      results.Add($"Response: {(int) response.StatusCode} {response.StatusCode}");
      results.Add($"Content-Type: {contentType}");

      if (contentType != "text/event-stream")
      {
        results.Add("Not SSE - Content-Type is not text/event-stream");
        return new TransportDetectionResult(false, null, null);
      }

      // Read SSE stream to find the endpoint event with a short timeout
      results.Add("Reading SSE stream for endpoint event (3s timeout)...");

      using CancellationTokenSource streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      streamCts.CancelAfter(TimeSpan.FromSeconds(3)); // Short timeout for stream reading

      using Stream stream = await response.Content.ReadAsStreamAsync(streamCts.Token);
      using StreamReader reader = new(stream);

      string? messageEndpoint = null;
      int lineCount = 0;
      int maxLines = 50; // Don't read forever

      while (lineCount < maxLines)
      {
        // Check cancellation before each read
        if (streamCts.Token.IsCancellationRequested)
        {
          break;
        }

        ValueTask<string?> lineTask = reader.ReadLineAsync(streamCts.Token);
        string? line = await lineTask.AsTask().WaitAsync(TimeSpan.FromSeconds(1), streamCts.Token);

        if (line == null)
        {
          break;
        }

        lineCount++;

        results.Add($"  SSE: {line}");

        // Look for endpoint event (format varies by server)
        // Common formats:
        // event: endpoint
        // data: /message?sessionId=xxx
        // or: data: {"endpoint": "/message?sessionId=xxx"}
        if (line.StartsWith("data:"))
        {
          string data = line[5..].Trim();

          // Try to extract endpoint from JSON
          if (data.StartsWith("{"))
          {
            try
            {
              using JsonDocument doc = JsonDocument.Parse(data);
              if (doc.RootElement.TryGetProperty("endpoint", out JsonElement ep))
              {
                messageEndpoint = ep.GetString();
              }
              else if (doc.RootElement.TryGetProperty("uri", out JsonElement uriProp))
              {
                messageEndpoint = uriProp.GetString();
              }
            }
            catch
            {
            }
          }
          // Or it might be a plain URL/path
          else if (data.StartsWith("/") || data.StartsWith("http"))
          {
            messageEndpoint = data;
          }

          if (messageEndpoint != null)
          {
            results.Add($"Found message endpoint: {messageEndpoint}");

            // SSE is verified when we:
            // 1. GET returns text/event-stream
            // 2. We receive an endpoint event with a session-based message URL
            // Note: We don't test the POST because SSE responses come via the stream,
            // which requires keeping the connection open during the request
            results.Add("");
            results.Add("=== Result ===");
            results.Add("SSE Transport detected!");
            results.Add("- GET returns text/event-stream");
            results.Add("- Received endpoint event with message URL");

            return new TransportDetectionResult(
              true,
              McpTransportType.Sse,
              "SSE transport verified",
              RawResponse: string.Join("\n", results)
            );
          }
        }
      }

      // We got text/event-stream but no endpoint event
      results.Add("SSE stream connected but no endpoint event received");
      return new TransportDetectionResult(
        true,
        McpTransportType.Sse,
        "SSE detected (no endpoint event)",
        RawResponse: string.Join("\n", results)
      );
    }
    catch (TaskCanceledException)
    {
      results.Add("SSE stream timed out (this is normal if server doesn't send endpoint quickly)");
    }
    catch (TimeoutException)
    {
      results.Add("SSE read timed out");
    }
    catch (OperationCanceledException)
    {
      results.Add("SSE operation cancelled/timed out");
    }
    catch (Exception ex)
    {
      results.Add($"Error: {ex.Message}");
    }

    return new TransportDetectionResult(false, null, null);
  }

  private async Task<TransportDetectionResult> TryStreamableHttpTransportAsync(
    string url,
    List<string> results,
    CancellationToken cancellationToken)
  {
    try
    {
      results.Add($"POST {url}");
      results.Add("Content-Type: application/json");
      results.Add("Accept: application/json, text/event-stream");
      results.Add($"Body: {McpInitializeRequest[..60]}...");

      // Use a 5 second timeout for the POST request
      using CancellationTokenSource postCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      postCts.CancelAfter(TimeSpan.FromSeconds(5));

      using HttpRequestMessage request = new(HttpMethod.Post, url);
      request.Content = new StringContent(McpInitializeRequest, Encoding.UTF8, "application/json");
      request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
      request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

      using HttpResponseMessage response = await _httpClient.SendAsync(request, postCts.Token);

      int statusCode = (int) response.StatusCode;
      string? contentType = response.Content.Headers.ContentType?.MediaType;
      results.Add($"Response: {statusCode} {response.StatusCode}");
      results.Add($"Content-Type: {contentType}");

      if (!response.IsSuccessStatusCode)
      {
        results.Add("Request failed");
        return new TransportDetectionResult(false, null, null);
      }

      string body = await response.Content.ReadAsStringAsync(cancellationToken);
      results.Add($"Body: {(body.Length > 200 ? body[..200] + "..." : body)}");

      // Check if it's a valid MCP response
      (string? serverName, string? serverVersion) = ParseMcpResponse(body);

      if (serverName != null)
      {
        // Determine if it's Streamable HTTP or regular HTTP
        // Streamable HTTP requires actual evidence of streaming capability:
        // - Response Content-Type is text/event-stream (streaming response)
        // - Transfer-Encoding is chunked AND multiple JSON objects in response
        // - Server sends X-MCP-Session header (session-based streaming)

        bool hasStreamingContentType = contentType == "text/event-stream";
        bool hasMcpSessionHeader = response.Headers.Contains("Mcp-Session-Id") ||
                                   response.Headers.Contains("X-MCP-Session") ||
                                   response.Headers.Contains("Mcp-Session");
        bool hasChunkedEncoding = response.Headers.TransferEncodingChunked == true;

        // Check for multiple JSON-RPC messages in response (streaming indicator)
        bool hasMultipleMessages = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
          .Count(line => line.TrimStart().StartsWith("{\"jsonrpc\"")) > 1;

        bool isStreamable = hasStreamingContentType || hasMcpSessionHeader ||
                            (hasChunkedEncoding && hasMultipleMessages);

        results.Add("");
        results.Add("=== Streaming Detection ===");
        results.Add($"Content-Type streaming: {hasStreamingContentType}");
        results.Add($"MCP-Session header: {hasMcpSessionHeader}");
        results.Add($"Chunked encoding: {hasChunkedEncoding}");
        results.Add($"Multiple messages: {hasMultipleMessages}");

        results.Add("");
        results.Add("=== Result ===");
        results.Add($"MCP Response received!");
        results.Add($"Server: {serverName} v{serverVersion}");

        if (isStreamable)
        {
          results.Add("Transport: Streamable HTTP (streaming evidence found)");
          return new TransportDetectionResult(
            true,
            McpTransportType.StreamableHttp,
            $"Streamable HTTP verified - {serverName} v{serverVersion}",
            serverName,
            serverVersion,
            string.Join("\n", results)
          );
        }
        else
        {
          results.Add("Transport: HTTP (no streaming evidence, using basic HTTP)");
          return new TransportDetectionResult(
            true,
            McpTransportType.Http,
            $"HTTP verified - {serverName} v{serverVersion}",
            serverName,
            serverVersion,
            string.Join("\n", results)
          );
        }
      }
      else
      {
        results.Add("Response is not a valid MCP initialize response");
      }
    }
    catch (TaskCanceledException)
    {
      results.Add("Request timed out");
    }
    catch (Exception ex)
    {
      results.Add($"Error: {ex.Message}");
    }

    return new TransportDetectionResult(false, null, null);
  }

  private async Task<TransportDetectionResult> TryHttpTransportAsync(
    string url,
    List<string> results,
    CancellationToken cancellationToken)
  {
    // This is essentially the same as Streamable HTTP but without the streaming expectations
    // We already tried this above, so just note that
    results.Add("(Already tested via Streamable HTTP check above)");
    return new TransportDetectionResult(false, null, null);
  }

  private static (string? name, string? version) ParseMcpResponse(string json)
  {
    try
    {
      using JsonDocument doc = JsonDocument.Parse(json);
      JsonElement root = doc.RootElement;

      // Check for error
      if (root.TryGetProperty("error", out _))
      {
        return (null, null);
      }

      // Look for result.serverInfo
      if (root.TryGetProperty("result", out JsonElement result) &&
          result.TryGetProperty("serverInfo", out JsonElement serverInfo))
      {
        string? name = serverInfo.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
        string? version = serverInfo.TryGetProperty("version", out JsonElement v) ? v.GetString() : null;
        return (name, version);
      }
    }
    catch
    {
    }

    return (null, null);
  }
}