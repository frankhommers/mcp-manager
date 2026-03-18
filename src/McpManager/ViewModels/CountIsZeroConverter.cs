using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace McpManager.ViewModels;

public class CountIsZeroConverter : IValueConverter
{
  public static readonly CountIsZeroConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    return value switch
    {
      int count => count == 0,
      _ => false,
    };
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotImplementedException();
  }
}
