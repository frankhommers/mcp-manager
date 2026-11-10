using System.Collections.Generic;

namespace McpManager.ViewModels;

public class UnknownServerRowViewModel(string name, IReadOnlyList<bool> foundIn)
{
  public string Name { get; } = name;

  public IReadOnlyList<bool> FoundIn { get; } = foundIn;
}
