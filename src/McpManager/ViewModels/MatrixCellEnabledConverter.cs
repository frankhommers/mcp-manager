using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace McpManager.ViewModels;

public class MatrixCellEnabledConverter : IValueConverter
{
  public static readonly MatrixCellEnabledConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    value is Guid id && id != Guid.Empty;

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}
