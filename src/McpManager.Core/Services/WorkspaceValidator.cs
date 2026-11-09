using McpManager.Core.Models;

namespace McpManager.Core.Services;

public static class WorkspaceValidator
{
  public static string? Validate(McpRegistry registry) =>
    registry.Servers.Select(ValidateServer).FirstOrDefault(e => e != null)
    ?? registry.TargetFolders.Select(ValidatePlace).FirstOrDefault(e => e != null);

  private static string? ValidateServer(McpServer server)
  {
    string name = string.IsNullOrWhiteSpace(server.DisplayName) ? server.Name : server.DisplayName;
    if (server.TransportType == McpTransportType.Stdio)
    {
      return string.IsNullOrWhiteSpace(server.Command) ? $"Server '{name}' needs a command." : null;
    }

    bool isHttpUrl = Uri.TryCreate(server.Url, UriKind.Absolute, out Uri? uri)
      && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    return isHttpUrl ? null : $"Server '{name}' needs an http(s) URL.";
  }

  private static string? ValidatePlace(TargetFolder place)
  {
    if (place.IsClipboard || place.IsQuickExport)
    {
      return null;
    }

    string name = string.IsNullOrWhiteSpace(place.Name) ? place.Path : place.Name;
    if (place.EnabledClients == TargetClientFlags.None)
    {
      return $"Place '{name}' needs at least one harness.";
    }

    return place.IsGlobal || Path.IsPathFullyQualified(place.Path)
      ? null
      : $"Place '{name}' needs an absolute folder path.";
  }
}
