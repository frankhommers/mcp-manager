using Avalonia.Controls;
using Avalonia.Interactivity;
using McpManager.Core.Models;

namespace McpManager.Views;

public partial class ExportConflictDialog : Window
{
  public ExportConflictDialog()
  {
    InitializeComponent();
  }

  private void KeepButton_OnClick(object? sender, RoutedEventArgs e) =>
    Close(ExportConflictResolution.KeepExisting);

  private void ReplaceButton_OnClick(object? sender, RoutedEventArgs e) =>
    Close(ExportConflictResolution.Replace);

  private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(null);
}
