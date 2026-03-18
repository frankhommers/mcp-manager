using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace McpManager.ViewModels;

public class TargetBadgeConverter : IValueConverter
{
  public static readonly TargetBadgeConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is TargetFolderViewModel target)
    {
      if (target.IsClipboard)
      {
        return "CLIPBOARD";
      }

      if (target.IsGlobal)
      {
        return "DESKTOP";
      }

      return "FOLDER";
    }

    return "FOLDER";
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotImplementedException();
  }
}
