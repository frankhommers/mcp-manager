using System;
using System.Collections.Generic;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public class CandidateEditedEventArgs(ScannedFolder folder, IReadOnlyList<ServerSelectionViewModel> selections)
  : EventArgs
{
  public ScannedFolder Folder { get; } = folder;

  public IReadOnlyList<ServerSelectionViewModel> Selections { get; } = selections;
}
