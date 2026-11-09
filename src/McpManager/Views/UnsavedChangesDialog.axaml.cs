using Avalonia.Controls;
using Avalonia.Interactivity;
using McpManager.ViewModels;

namespace McpManager.Views;

public partial class UnsavedChangesDialog : Window
{
  public UnsavedChangesDialog()
  {
    InitializeComponent();
  }

  private void SaveButton_OnClick(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Save);

  private void DiscardButton_OnClick(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Discard);

  private void StayButton_OnClick(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Stay);
}
