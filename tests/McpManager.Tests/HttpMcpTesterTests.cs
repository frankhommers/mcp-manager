using System.Net;
using System.Text;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public sealed class HttpMcpTesterTests
{
  [Fact]
  public async Task TestInitializeAsync_StreamableHttp_ReturnsServerInfo()
  {
    using HttpListener listener = new();
    int port = GetFreePort();
    string url = $"http://127.0.0.1:{port}/mcp/";
    listener.Prefixes.Add(url);
    listener.Start();

    string initResponse =
      """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2024-11-05","serverInfo":{"name":"Test Server","version":"1.2.3"},"capabilities":{}}}""";

    using CancellationTokenSource serverCts = new(TimeSpan.FromSeconds(10));

    Task serverTask = Task.Run(async () =>
    {
      while (!serverCts.Token.IsCancellationRequested)
      {
        HttpListenerContext context;
        try
        {
          context = await listener.GetContextAsync().WaitAsync(serverCts.Token);
        }
        catch (OperationCanceledException)
        {
          break;
        }

        using StreamReader reader = new(context.Request.InputStream, Encoding.UTF8);
        string body = await reader.ReadToEndAsync();

        if (body.Contains("\"initialize\""))
        {
          context.Response.StatusCode = (int)HttpStatusCode.OK;
          context.Response.ContentType = "application/json";
          context.Response.Headers.Add("Mcp-Session-Id", "session-123");
          byte[] responseBytes = Encoding.UTF8.GetBytes(initResponse);
          await context.Response.OutputStream.WriteAsync(responseBytes);
          context.Response.Close();
        }
        else if (body.Contains("notifications/initialized"))
        {
          context.Response.StatusCode = (int)HttpStatusCode.OK;
          context.Response.ContentType = "application/json";
          context.Response.Headers.Add("Mcp-Session-Id", "session-123");
          context.Response.Close();
        }
        else
        {
          context.Response.StatusCode = (int)HttpStatusCode.OK;
          context.Response.ContentType = "application/json";
          context.Response.Headers.Add("Mcp-Session-Id", "session-123");
          byte[] emptyResponse = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":2,"result":{}}""");
          await context.Response.OutputStream.WriteAsync(emptyResponse);
          context.Response.Close();
        }
      }
    }, serverCts.Token);

    try
    {
      HttpMcpTester tester = new();
      HttpMcpTestResult result = await tester.TestInitializeAsync(url, McpTransportType.StreamableHttp);

      Assert.True(result.Success, $"Expected success but got: {result.StatusMessage} - {result.ResultText}");
      Assert.Equal("Test Server", result.ServerName);
      Assert.Equal("1.2.3", result.ServerVersion);
    }
    finally
    {
      await serverCts.CancelAsync();
      listener.Stop();
    }
  }

  private static int GetFreePort()
  {
    using System.Net.Sockets.TcpListener tcpListener = new(IPAddress.Loopback, 0);
    tcpListener.Start();
    int port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
    tcpListener.Stop();
    return port;
  }
}
