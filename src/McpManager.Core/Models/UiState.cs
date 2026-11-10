using System.Text.Json.Serialization;

namespace McpManager.Core.Models;

public record UiState
{
  [JsonPropertyName("lastProjectId")]
  public Guid? LastProjectId { get; init; }
}
