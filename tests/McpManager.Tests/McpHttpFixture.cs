using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using McpManager.TestServer;

namespace McpManager.Tests;

internal sealed class McpHttpFixture : IAsyncDisposable
{
  private readonly HttpListener _listener = new();
  private readonly CancellationTokenSource _stop = new();
  private readonly Task _run;

  public string Url { get; }
  public ConcurrentQueue<JsonObject> Requests { get; } = new();
  public ConcurrentQueue<string?> ProtocolHeaders { get; } = new();
  public ConcurrentQueue<string?> AuthHeaders { get; } = new();

  public McpHttpFixture(string version, bool includeIdentity = true)
  {
    using TcpListener reserve = new(IPAddress.Loopback, 0);
    reserve.Start();
    int port = ((IPEndPoint)reserve.LocalEndpoint).Port;
    reserve.Stop();
    Url = $"http://127.0.0.1:{port}/mcp/";
    _listener.Prefixes.Add(Url);
    _listener.Start();
    _run = RunAsync(version, includeIdentity);
  }

  private async Task RunAsync(string version, bool includeIdentity)
  {
    try
    {
      while (!_stop.IsCancellationRequested)
      {
        HttpListenerContext context = await _listener.GetContextAsync().WaitAsync(_stop.Token);
        if (context.Request.HttpMethod != "POST")
        {
          context.Response.StatusCode = 405;
          context.Response.Close();
          continue;
        }
        using StreamReader reader = new(context.Request.InputStream, Encoding.UTF8);
        JsonObject request = JsonNode.Parse(await reader.ReadToEndAsync())!.AsObject();
        Requests.Enqueue(request);
        ProtocolHeaders.Enqueue(context.Request.Headers["MCP-Protocol-Version"]);
        AuthHeaders.Enqueue(context.Request.Headers["Authorization"]);
        JsonObject? response = ProtocolFixture.Respond(request, version, includeIdentity);
        context.Response.StatusCode = response == null ? 202 : 200;
        if (response != null)
        {
          context.Response.ContentType = "application/json";
          byte[] bytes = Encoding.UTF8.GetBytes(response.ToJsonString());
          await context.Response.OutputStream.WriteAsync(bytes);
        }
        context.Response.Close();
      }
    }
    catch (Exception ex) when (_stop.IsCancellationRequested &&
      ex is OperationCanceledException or HttpListenerException or ObjectDisposedException)
    {
    }
  }

  public async ValueTask DisposeAsync()
  {
    await _stop.CancelAsync();
    _listener.Close();
    await _run;
    _stop.Dispose();
  }
}
