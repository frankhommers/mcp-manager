using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public partial class MainWindowViewModel
{
  [RelayCommand]
  private async Task ImportFoundServerAsync(FoundTargetServerViewModel? found)
  {
    TargetFolderViewModel? target = SelectedTarget;
    if (found == null || target == null || _registry == null || !target.FoundServers.Contains(found))
    {
      return;
    }

    IsLoading = true;
    StatusMessage = $"Importing '{found.Name}'...";
    try
    {
      foreach (McpServerViewModel serverVm in Servers)
      {
        serverVm.UpdateModel();
      }

      ExistingTargetServers? current = await target.RefreshExistingServersAsync(_registry.Settings);
      if (current == null || SelectedTarget != target || !current.Servers.Contains(found.Source) ||
          !target.FoundServers.Contains(found))
      {
        StatusMessage = "The source or library changed. Review the refreshed list before importing.";
        return;
      }

      McpServer server = _configImportService.ImportServer(found.Source);
      if (AddImportedServers([server]) == 0)
      {
        StatusMessage = $"'{found.Name}' is already in MCP Manager.";
        return;
      }

      target.SelectImportedServer(server.Id);
      StatusMessage = $"Imported '{server.DisplayName}' and selected it for '{target.Name}'. Save to keep your changes.";
    }
    catch (Exception ex)
    {
      StatusMessage = $"Import error: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }
}
