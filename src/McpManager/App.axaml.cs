using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using McpManager.ViewModels;
using McpManager.Views;

namespace McpManager;

public partial class App : Application
{
  public override void Initialize()
  {
    AvaloniaXamlLoader.Load(this);
  }

  private async void AboutMenuItem_OnClick(object? sender, EventArgs e)
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
        desktop.MainWindow != null)
    {
      AboutWindow aboutWindow = new();
      await aboutWindow.ShowDialog(desktop.MainWindow);
    }
  }

  private void QuitMenuItem_OnClick(object? sender, EventArgs e)
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      desktop.TryShutdown();
    }
  }

  public override async void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      MainWindowViewModel viewModel = new();
      desktop.MainWindow = new MainWindow
      {
        DataContext = viewModel,
      };

      // Initialize the view model after the window is set up
      await viewModel.InitializeAsync();
    }

    base.OnFrameworkInitializationCompleted();
  }
}