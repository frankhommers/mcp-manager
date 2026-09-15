using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public partial class TargetFolderViewModel
{
  private IReadOnlyList<ExistingTargetServer> _existingServers = [];

  public ObservableCollection<FoundTargetServerViewModel> FoundServers { get; } = [];
  public bool HasFoundServers => FoundServers.Count > 0;

  private void RefreshFoundServers()
  {
    CollectionViewUpdater.Update(FoundServers, _existingServers
      .Where(existing => !ServerSelections.Any(server =>
        server.ServerId == existing.ManagedId || server.ServerKey.Equals(existing.Name, StringComparison.Ordinal)))
      .OrderBy(server => server.Name, StringComparer.CurrentCultureIgnoreCase)
      .ThenBy(server => server.FilePath, StringComparer.Ordinal)
      .Select(server => new FoundTargetServerViewModel(server)));
    OnPropertyChanged(nameof(HasFoundServers));
  }

  public void SelectImportedServer(Guid serverId)
  {
    ServerSelectionViewModel selection = ServerSelections.Single(server => server.ServerId == serverId);
    _selectionOverrides[serverId] = true;
    selection.IsEnabled = true;
    selection.IsDisabled = false;
    ServerSearchText = string.Empty;
    UpdateModel();
  }
}
