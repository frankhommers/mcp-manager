using McpManager.Core.Models;

namespace McpManager.ViewModels;

public sealed record FoundTargetServerViewModel(ExistingTargetServer Source)
{
  public string Name => Source.Name;
  public string FilePath => Source.FilePath;
  public string ClientName => Source.Client switch
  {
    TargetClientFlags.ClaudeCode => "Claude Code",
    TargetClientFlags.ClaudeCodeGlobal => "Claude Code (global)",
    TargetClientFlags.ClaudeDesktop => "Claude Desktop",
    TargetClientFlags.OpenCode => "OpenCode",
    TargetClientFlags.Codex => "Codex",
    TargetClientFlags.Cursor => "Cursor",
    TargetClientFlags.Windsurf => "Windsurf",
    TargetClientFlags.VsCode => "VS Code",
    _ => Source.Client.ToString(),
  };

  public string Status =>
    (Source.ManagedId.HasValue ? "Missing from library" : "Not owned by MCP Manager") +
    (Source.IsEnabled ? string.Empty : " · Disabled in this target");
}
