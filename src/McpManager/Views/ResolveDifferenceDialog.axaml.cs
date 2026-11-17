using Avalonia.Controls;
using Avalonia.Data;
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

  public ResolveDifferenceDialog(DiffViewModel diff, bool canDeploy) : this()
  {
    _diff = diff;
    DataContext = diff;
    DiffHost.DataContext = diff;
    DeployHint.IsVisible = !canDeploy;
    if (canDeploy)
    {
      DeployButton.Bind(IsEnabledProperty, new Binding(nameof(DiffViewModel.IsComplete)));
    }
    else
    {
      DeployButton.IsEnabled = false;
    }
  }

  private void ApplyButton_OnClick(object? sender, RoutedEventArgs e) => Close(Resolve(deploy: false));

  private void DeployButton_OnClick(object? sender, RoutedEventArgs e) => Close(Resolve(deploy: true));

  private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(null);

  private DifferenceResolution? Resolve(bool deploy) =>
    _diff?.Choices[0] is { } choice ? new DifferenceResolution(choice, deploy) : null;
}
