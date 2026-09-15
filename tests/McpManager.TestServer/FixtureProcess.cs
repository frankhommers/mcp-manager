using System.Text.Json.Nodes;

namespace McpManager.TestServer;

public static class FixtureProcess
{
  public static async Task Main(string[] args)
  {
    string version = args.Length > 0 ? args[0] : ProtocolFixture.LatestVersion;
    string? recordPath = args.Length > 1 ? args[1] : null;
    bool includeIdentity = !args.Contains("--omit-identity");
    while (await Console.In.ReadLineAsync() is string line)
    {
      if (recordPath != null)
      {
        await File.AppendAllTextAsync(recordPath, line + Environment.NewLine);
      }
      JsonObject request = JsonNode.Parse(line)!.AsObject();
      JsonObject? response = ProtocolFixture.Respond(request, version, includeIdentity);
      if (response != null)
      {
        await Console.Out.WriteLineAsync(response.ToJsonString());
        await Console.Out.FlushAsync();
      }
    }
  }
}
