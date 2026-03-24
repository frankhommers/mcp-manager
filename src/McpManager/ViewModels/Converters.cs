using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

/// <summary>
/// Represents a group of servers in the sidebar tree
/// </summary>
public class ServerGroupViewModel
{
  public string GroupName { get; set; } = string.Empty;
  public string DisplayName => string.IsNullOrEmpty(GroupName) ? "Ungrouped" : GroupName;
  public List<McpServerViewModel> Servers { get; set; } = [];
}

public class TransportTypeDisplayConverter : IValueConverter
{
  public static readonly TransportTypeDisplayConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is McpTransportType type)
    {
      return type.GetDisplayName();
    }

    return value?.ToString();
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotImplementedException();
  }
}

public class TransportBadgeConverter : IValueConverter
{
  public static readonly TransportBadgeConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    return value switch
    {
      McpTransportType.Stdio => "STDIO",
      McpTransportType.Http => "HTTP",
      McpTransportType.Sse => "SSE",
      McpTransportType.StreamableHttp => "STREAMABLE HTTP",
      TransportTypeItem item => Convert(item.Value, targetType, parameter, culture),
      _ => value?.ToString()?.ToUpperInvariant(),
    };
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotImplementedException();
  }
}

/// <summary>
/// Wrapper for transport type enum for ComboBox display
/// </summary>
public record TransportTypeItem(McpTransportType Value, string DisplayName)
{
  public override string ToString()
  {
    return DisplayName;
  }

  public static IEnumerable<TransportTypeItem> All =>
  [
    new(McpTransportType.Stdio, "stdio"),
    new(McpTransportType.Http, "HTTP"),
    new(McpTransportType.Sse, "SSE"),
    new(McpTransportType.StreamableHttp, "Streamable HTTP"),
  ];
}

/// <summary>
/// Connection mode: Local Command (stdio) or Remote URL (HTTP-based)
/// </summary>
public record ConnectionModeItem(bool IsRemote, string DisplayName, string Description)
{
  public override string ToString()
  {
    return DisplayName;
  }

  public static IEnumerable<ConnectionModeItem> All =>
  [
    new(false, "Local Command", "Run a local process (npx, uvx, python, etc.)"),
    new(true, "Remote URL", "Connect to an HTTP endpoint"),
  ];
}

/// <summary>
/// HTTP protocol variants for Remote URL connections
/// </summary>
public record HttpProtocolItem(McpTransportType Value, string DisplayName, string Description)
{
  public override string ToString()
  {
    return DisplayName;
  }

  public static IEnumerable<HttpProtocolItem> All =>
  [
    new(McpTransportType.Sse, "SSE", "Server-Sent Events - most common, URL often ends in /sse"),
    new(McpTransportType.StreamableHttp, "Streamable HTTP", "Modern bidirectional, URL often ends in /mcp"),
    new(McpTransportType.Http, "HTTP", "Basic request/response, no streaming"),
  ];
}

public class TransportDescriptionConverter : IValueConverter
{
  public static readonly TransportDescriptionConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is McpTransportType type)
    {
      return type switch
      {
        McpTransportType.Stdio =>
          "Local command (npx, uvx, python). Supported by all clients.",
        McpTransportType.Http =>
          "Basic HTTP request/response. Claude Desktop needs mcp-proxy bridge.",
        McpTransportType.Sse =>
          "HTTP plus Server-Sent Events for streaming. Claude Desktop needs mcp-proxy bridge. URL typically ends in /sse.",
        McpTransportType.StreamableHttp =>
          "Modern bidirectional HTTP streaming. Claude Desktop needs mcp-proxy bridge. URL typically ends in /mcp.",
        _ => "",
      };
    }

    return "";
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotImplementedException();
  }
}

public class NavigationPageEqualsConverter : IValueConverter
{
  public static readonly NavigationPageEqualsConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is NavigationPage currentPage && parameter is string pageName &&
        Enum.TryParse(pageName, out NavigationPage targetPage))
    {
      return currentPage == targetPage;
    }

    return false;
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotImplementedException();
  }
}

public class StringEqualsConverter : IValueConverter
{
  public static readonly StringEqualsConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    return value?.ToString() == parameter?.ToString();
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is true)
    {
      return parameter?.ToString();
    }

    return BindingOperations.DoNothing;
  }
}
