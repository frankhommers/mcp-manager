using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class UiStateStoreTests : IDisposable
{
  private readonly string _directory = Path.Combine(Path.GetTempPath(), "mcp-ui-state-" + Guid.NewGuid());

  public void Dispose()
  {
    if (Directory.Exists(_directory))
    {
      Directory.Delete(_directory, true);
    }
  }

  [Fact]
  public void Saves_next_to_workspace_and_round_trips()
  {
    Guid id = Guid.NewGuid();
    UiStateStore store = UiStateStore.NextTo(Path.Combine(_directory, "workspace.json"));

    Assert.Null(store.Load().LastProjectId);
    store.Save(new UiState { LastProjectId = id });

    Assert.True(File.Exists(Path.Combine(_directory, UiStateStore.FileName)));
    Assert.Equal(id, UiStateStore.NextTo(Path.Combine(_directory, "workspace.json")).Load().LastProjectId);
  }

  [Fact]
  public void Corrupt_file_or_missing_path_gives_empty_state()
  {
    Directory.CreateDirectory(_directory);
    File.WriteAllText(Path.Combine(_directory, UiStateStore.FileName), "{not json");

    Assert.Null(UiStateStore.NextTo(Path.Combine(_directory, "workspace.json")).Load().LastProjectId);
    UiStateStore none = UiStateStore.NextTo("");
    none.Save(new UiState { LastProjectId = Guid.NewGuid() });
    Assert.Null(none.Load().LastProjectId);
  }
}
