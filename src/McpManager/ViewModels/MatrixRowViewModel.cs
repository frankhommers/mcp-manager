using System.Collections.Generic;

namespace McpManager.ViewModels;

public class MatrixRowViewModel(McpServerViewModel server, IReadOnlyList<ServerSelectionViewModel> cells)
{
  public McpServerViewModel Server { get; } = server;

  public string Label => Server.ListName;

  public IReadOnlyList<ServerSelectionViewModel> Cells { get; } = cells;
}
