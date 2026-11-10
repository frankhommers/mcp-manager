namespace McpManager.ViewModels;

internal sealed record NavigationSnapshot(
  McpServerViewModel? Server,
  TargetFolderViewModel? Target,
  bool ShowSettings);
