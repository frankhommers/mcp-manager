using System.ComponentModel;

namespace McpManager.Core.Models;

public enum McpTransportType
{
  [Description("stdio")] Stdio,

  [Description("HTTP")] Http,

  [Description("SSE")] Sse,

  [Description("Streamable HTTP")] StreamableHttp,
}

public static class McpTransportTypeExtensions
{
  public static string GetDisplayName(this McpTransportType type)
  {
    return type switch
    {
      McpTransportType.Stdio => "stdio",
      McpTransportType.Http => "HTTP",
      McpTransportType.Sse => "SSE",
      McpTransportType.StreamableHttp => "Streamable HTTP",
      _ => type.ToString(),
    };
  }
}