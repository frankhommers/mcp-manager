using CommunityToolkit.Mvvm.ComponentModel;

namespace McpManager.ViewModels;

public partial class KeyValuePairViewModel : ViewModelBase
{
  [ObservableProperty] private string _key = string.Empty;

  [ObservableProperty] private string _value = string.Empty;

  public KeyValuePairViewModel()
  {
  }

  public KeyValuePairViewModel(string key, string value)
  {
    _key = key;
    _value = value;
  }
}
