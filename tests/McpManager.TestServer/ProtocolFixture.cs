using System.Text.Json.Nodes;

namespace McpManager.TestServer;

public static class ProtocolFixture
{
  public const string LatestVersion = "2026-07-28";

  public static JsonObject? Respond(JsonObject request, string version, bool includeIdentity = true)
  {
    if (request["id"] == null)
    {
      return null;
    }

    string? method = request["method"]?.GetValue<string>();
    JsonObject response = new() { ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone() };
    JsonObject result = new();
    bool modern = version == LatestVersion;
    if ((modern && method == "initialize") || (!modern && method == "server/discover"))
    {
      response["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" };
      return response;
    }

    if (modern && request["params"]?["_meta"]?["io.modelcontextprotocol/protocolVersion"]?.GetValue<string>() != version)
    {
      response["error"] = new JsonObject { ["code"] = -32600, ["message"] = "Missing protocol metadata" };
      return response;
    }

    switch (method)
    {
      case "server/discover":
        result["supportedVersions"] = new JsonArray(version);
        result["capabilities"] = new JsonObject { ["tools"] = new JsonObject() };
        if (includeIdentity)
        {
          result["_meta"] = new JsonObject { ["io.modelcontextprotocol/serverInfo"] = Identity() };
        }
        break;
      case "initialize":
        result["protocolVersion"] = version;
        result["serverInfo"] = Identity();
        result["capabilities"] = new JsonObject { ["tools"] = new JsonObject() };
        break;
      case "tools/list":
        bool secondPage = request["params"]?["cursor"]?.GetValue<string>() == "second";
        result["tools"] = new JsonArray(new JsonObject
        {
          ["name"] = secondPage ? "write_note" : "read_note",
          ["inputSchema"] = new JsonObject { ["type"] = "object" },
        });
        if (!secondPage)
        {
          result["nextCursor"] = "second";
        }
        break;
      default:
        response["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" };
        return response;
    }

    if (modern)
    {
      result["resultType"] = "complete";
      result["ttlMs"] = 0;
      result["cacheScope"] = "private";
    }
    response["result"] = result;
    return response;
  }

  private static JsonObject Identity() => new() { ["name"] = "Protocol fixture", ["version"] = "1.2.3" };
}
