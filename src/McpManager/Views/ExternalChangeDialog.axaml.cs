using Avalonia.Controls;
using Avalonia.Interactivity;
using McpManager.ViewModels;

namespace McpManager.Views;

public partial class ExternalChangeDialog : Window
{
  public ExternalChangeDialog()
  {
    InitializeComponent();
  }

  public ExternalChangeDialog(DiffViewModel diff) : this()
  {
    DiffHost.DataContext = diff;
  }

  private void OverwriteButton_OnClick(object? sender, RoutedEventArgs e) => Close(DiffChoice.Overwrite);

  private void KeepButton_OnClick(object? sender, RoutedEventArgs e) => Close(DiffChoice.KeepFile);

  private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close(null);
}
