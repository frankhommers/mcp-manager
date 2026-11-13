using McpManager.Core.Models;

namespace McpManager.ViewModels;

public class MatrixCellViewModel(
  ServerSelectionViewModel selection, ScanServerMatch? difference, bool isUpdatedOnDeploy = false)
{
  public ServerSelectionViewModel Selection { get; } = selection;

  public ScanServerMatch? Difference { get; } = difference;

  public bool HasDifference => Difference != null;

  public bool IsUpdatedOnDeploy { get; } = difference != null && isUpdatedOnDeploy;

  public bool NeedsChoice => HasDifference && !IsUpdatedOnDeploy;
}
