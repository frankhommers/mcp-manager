using Avalonia.Controls;
using Avalonia.Interactivity;
using McpManager.ViewModels;

namespace McpManager.Views;

public partial class ReviewDeployDialog : Window
{
  public ReviewDeployDialog()
  {
    InitializeComponent();
  }

  public ReviewDeployDialog(DiffViewModel diff) : this()
  {
    DiffHost.DataContext = diff;
  }

  private void DeployButton_OnClick(object? sender, RoutedEventArgs e) => Close(true);

  private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}
