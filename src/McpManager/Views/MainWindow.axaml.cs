using Avalonia.Controls;
using Avalonia.Interactivity;
using McpManager.ViewModels;

namespace McpManager.Views;

public partial class MainWindow : Window
{
  private bool _closeConfirmed;

  public MainWindow()
  {
    InitializeComponent();
  }

  protected override async void OnClosing(WindowClosingEventArgs e)
  {
    base.OnClosing(e);
    if (_closeConfirmed || e.Cancel || DataContext is not MainWindowViewModel viewModel)
    {
      return;
    }

    viewModel.CheckTargetsCommand.Execute(null);
    if (!viewModel.IsDirty)
    {
      return;
    }

    e.Cancel = true;
    if (await viewModel.ConfirmLeaveAsync())
    {
      _closeConfirmed = true;
      Close();
    }
  }

  private async void AboutButton_OnClick(object? sender, RoutedEventArgs e)
  {
    AboutWindow aboutWindow = new();
    await aboutWindow.ShowDialog(this);
  }
}
