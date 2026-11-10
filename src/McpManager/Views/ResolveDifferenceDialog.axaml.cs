using Avalonia.Controls;
using Avalonia.Interactivity;
using McpManager.ViewModels;

namespace McpManager.Views;

public partial class ResolveDifferenceDialog : Window
{
  private readonly DiffViewModel? _diff;

  public ResolveDifferenceDialog()
  {
    InitializeComponent();
  }

  public ResolveDifferenceDialog(DiffViewModel diff) : this()
  {
    _diff = diff;
    DataContext = diff;
    DiffHost.DataContext = diff;
  }

  private void ApplyButton_OnClick(object? sender, RoutedEventArgs e) => Close(_diff?.Choices[0]);

  private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(null);
}
