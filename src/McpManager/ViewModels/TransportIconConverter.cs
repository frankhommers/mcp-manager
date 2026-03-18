using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public class TransportIconConverter : IValueConverter
{
  public static readonly TransportIconConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    McpTransportType? transportType = value switch
    {
      McpTransportType t => t,
      TransportTypeItem item => item.Value,
      _ => null,
    };

    if (transportType == null)
    {
      return null;
    }

    string resourceKey = transportType switch
    {
      McpTransportType.Stdio => "MdiConsole",
      McpTransportType.StreamableHttp => "MdiAccessPoint",
      _ => "MdiWeb",
    };

    if (Application.Current?.Resources.TryGetResource(resourceKey, null, out object? resource) == true &&
        resource is StreamGeometry geometry)
    {
      return geometry;
    }

    return null;
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotImplementedException();
  }
}
