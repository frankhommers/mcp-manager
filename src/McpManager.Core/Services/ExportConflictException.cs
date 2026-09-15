using McpManager.Core.Models;

namespace McpManager.Core.Services;

public sealed class ExportConflictException(ExportConflict conflict)
  : InvalidOperationException(
    $"'{conflict.ServerName}' already exists in '{conflict.FilePath}' and is not owned by MCP Manager. " +
    "Choose whether to replace it or keep the existing configuration.")
{
  public ExportConflict Conflict { get; } = conflict;
}
