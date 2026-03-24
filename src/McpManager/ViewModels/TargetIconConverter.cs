using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace McpManager.ViewModels;

public class TargetIconConverter : IValueConverter
{
  public static readonly TargetIconConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    string resourceKey = value switch
    {
      TargetFolderViewModel target when target.IsClipboard => "MdiClipboardOutline",
      TargetFolderViewModel target when target.IsQuickExport => "MdiLightningBolt",
      TargetFolderViewModel target when target.IsCodex => "CodexLogo",
      TargetFolderViewModel target when target.IsCursor => "MdiConsole",
      TargetFolderViewModel target when target.IsWindsurf => "MdiAccessPoint",
      TargetFolderViewModel target when target.IsVsCode => "MdiMonitor",
      TargetFolderViewModel target when target.IsGlobal => "ClaudeLogo",
      _ => "MdiFolderOutline",
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

public class TargetIconColorConverter : IValueConverter
{
  public static readonly TargetIconColorConverter Instance = new();

  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is TargetFolderViewModel { IsQuickExport: true })
    {
      return new SolidColorBrush(Color.Parse("#E5C07B"));
    }

    if (value is TargetFolderViewModel target && target.IsGlobal)
    {
      if (target.IsCodex)
      {
        return new SolidColorBrush(Color.Parse("#4EC9B0"));
      }

      if (target.IsCursor)
      {
        return new SolidColorBrush(Color.Parse("#00D1A7"));
      }

      if (target.IsWindsurf)
      {
        return new SolidColorBrush(Color.Parse("#36B5F0"));
      }

      if (target.IsVsCode)
      {
        return new SolidColorBrush(Color.Parse("#007ACC"));
      }

      return new SolidColorBrush(Color.Parse("#D97757"));
    }

    if (Application.Current?.Resources.TryGetResource("SystemAccentColor", null, out object? resource) == true &&
        resource is Color accentColor)
    {
      return new SolidColorBrush(accentColor);
    }

    return Brushes.White;
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotImplementedException();
  }
}
