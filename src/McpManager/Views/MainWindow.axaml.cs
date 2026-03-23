using Avalonia.Controls;
using Avalonia.Interactivity;

namespace McpManager.Views;

public partial class MainWindow : Window
{
  public MainWindow()
  {
    InitializeComponent();
  }

  private async void AboutButton_OnClick(object? sender, RoutedEventArgs e)
  {
    AboutWindow aboutWindow = new();
    await aboutWindow.ShowDialog(this);
  }
}