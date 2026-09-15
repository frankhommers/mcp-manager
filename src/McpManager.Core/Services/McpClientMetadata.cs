using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpManager.Core.Services;

internal static class McpClientMetadata
{
  public static Implementation? ReadServerInfo(McpClient client)
  {
    try
    {
      return client.ServerInfo;
    }
    catch (InvalidOperationException) when (client.NegotiatedProtocolVersion != null)
    {
      // The SDK throws when a connected server omits optional discovery identity metadata.
      return null;
    }
  }
}
