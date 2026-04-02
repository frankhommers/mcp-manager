using CommunityToolkit.Mvvm.ComponentModel;

namespace McpManager.ViewModels;

public partial class StringItemViewModel : ViewModelBase
{
  [ObservableProperty] private string _value = string.Empty;

  public StringItemViewModel()
  {
  }

  public StringItemViewModel(string value)
  {
    _value = value;
  }
}
