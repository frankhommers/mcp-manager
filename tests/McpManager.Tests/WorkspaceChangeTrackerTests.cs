using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Tests;

public class WorkspaceChangeTrackerTests
{
  private static McpRegistry CreateRegistry()
  {
    McpRegistry registry = new();
    registry.Servers.Add(new McpServer { Name = "alpha", Command = "npx" });
    registry.TargetFolders.Add(new TargetFolder { Name = "home", Path = "/tmp/home" });
    return registry;
  }

  [Fact]
  public void Clean_after_mark_saved()
  {
    McpRegistry registry = CreateRegistry();
    WorkspaceChangeTracker tracker = new();
    tracker.MarkSaved(registry);
    Assert.False(tracker.IsDirty(registry));
  }

  [Fact]
  public void Dirty_before_anything_saved()
  {
    Assert.True(new WorkspaceChangeTracker().IsDirty(CreateRegistry()));
  }

  [Fact]
  public void Dirty_after_server_edit_and_after_new_place()
  {
    McpRegistry registry = CreateRegistry();
    WorkspaceChangeTracker tracker = new();
    tracker.MarkSaved(registry);

    registry.Servers[0].Command = "uvx";
    Assert.True(tracker.IsDirty(registry));

    tracker.MarkSaved(registry);
    Assert.False(tracker.IsDirty(registry));

    registry.TargetFolders.Add(new TargetFolder { Name = "new", Path = "/tmp/new" });
    Assert.True(tracker.IsDirty(registry));
  }

  [Fact]
  public void Restore_returns_saved_state_without_new_server_or_place()
  {
    McpRegistry registry = CreateRegistry();
    WorkspaceChangeTracker tracker = new();
    tracker.MarkSaved(registry);
    registry.Servers.Add(new McpServer { Name = "beta" });
    registry.TargetFolders.Add(new TargetFolder { Name = "new" });

    McpRegistry restored = tracker.RestoreSaved();

    Assert.Single(restored.Servers);
    Assert.Equal("alpha", restored.Servers[0].Name);
    Assert.Single(restored.TargetFolders);
    Assert.False(tracker.IsDirty(restored));
  }

  [Fact]
  public void Restored_copy_is_independent()
  {
    McpRegistry registry = CreateRegistry();
    WorkspaceChangeTracker tracker = new();
    tracker.MarkSaved(registry);

    McpRegistry restored = tracker.RestoreSaved();
    restored.Servers[0].Name = "changed";
    restored.Servers.Add(new McpServer { Name = "extra" });

    Assert.False(tracker.IsDirty(registry));
    Assert.Equal("alpha", tracker.RestoreSaved().Servers[0].Name);
    Assert.Single(tracker.SavedCopy().Servers);
  }

  [Fact]
  public void Restore_and_saved_copy_throw_before_mark_saved()
  {
    WorkspaceChangeTracker tracker = new();
    Assert.Throws<InvalidOperationException>(() => { tracker.RestoreSaved(); });
    Assert.Throws<InvalidOperationException>(() => { tracker.SavedCopy(); });
  }

  [Fact]
  public void Deserialize_rejects_unsupported_format()
  {
    Assert.Throws<InvalidDataException>(() => RegistryService.Deserialize("{\"formatVersion\":1}"));
    Assert.Throws<InvalidDataException>(() => RegistryService.Deserialize("{}"));
  }
}
