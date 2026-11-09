using McpManager.Core.Models;

namespace McpManager.Core.Services;

public class WorkspaceChangeTracker
{
  private string? _savedJson;

  public void MarkSaved(McpRegistry registry) => _savedJson = RegistryService.Serialize(registry);

  public bool IsDirty(McpRegistry registry) =>
    !string.Equals(_savedJson, RegistryService.Serialize(registry), StringComparison.Ordinal);

  public McpRegistry RestoreSaved() => SavedCopy();

  public McpRegistry SavedCopy() => RegistryService.Deserialize(
    _savedJson ?? throw new InvalidOperationException("No saved workspace has been recorded."));
}
