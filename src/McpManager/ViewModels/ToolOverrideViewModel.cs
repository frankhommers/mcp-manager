using CommunityToolkit.Mvvm.ComponentModel;

namespace McpManager.ViewModels;

public partial class ToolOverrideViewModel : ObservableObject
{
  [ObservableProperty] private string _toolName = string.Empty;

  [ObservableProperty] private bool _isAllowed = true;
}
