using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace McpManager.Views;

public partial class AboutWindow : Window
{
  public AboutWindow()
  {
    InitializeComponent();
    string appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
    VersionText.Text = $"Version {appVersion}";
  }

  private void GitHubLink_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
    OpenBrowser("https://github.com/frankhommers/mcp-manager");
  }

  private void SponsorLink_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
    OpenBrowser("https://ko-fi.com/frankhommers");
  }

  private void OkButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
    Close();
  }

  private static void OpenBrowser(string url)
  {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
      Process.Start("open", url);
    }
    else
    {
      Process.Start("xdg-open", url);
    }
  }
}
