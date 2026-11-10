using System.Collections.Generic;

namespace McpManager.ViewModels;

public class MatrixRowViewModel(McpServerViewModel server, IReadOnlyList<MatrixCellViewModel> cells)
{
  public McpServerViewModel Server { get; } = server;

  public string Label => Server.ListName;

  public IReadOnlyList<MatrixCellViewModel> Cells { get; } = cells;
}
