using McpManager.Core.Models;

namespace McpManager.ViewModels;

public class MatrixCellViewModel(ServerSelectionViewModel selection, ScanServerMatch? difference)
{
  public ServerSelectionViewModel Selection { get; } = selection;

  public ScanServerMatch? Difference { get; } = difference;

  public bool HasDifference => Difference != null;
}
