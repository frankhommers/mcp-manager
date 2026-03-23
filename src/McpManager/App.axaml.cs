using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
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
      desktop.Shutdown();
    }
  }

  public override async void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
      // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
      DisableAvaloniaDataAnnotationValidation();

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

  private void DisableAvaloniaDataAnnotationValidation()
  {
    // Get an array of plugins to remove
    DataAnnotationsValidationPlugin[] dataValidationPluginsToRemove =
      BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

    // remove each entry found
    foreach (DataAnnotationsValidationPlugin plugin in dataValidationPluginsToRemove)
    {
      BindingPlugins.DataValidators.Remove(plugin);
    }
  }
}