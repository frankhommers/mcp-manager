using McpManager.Core.Models;

namespace McpManager.Core.ConfigGenerators;

public interface IConfigGenerator
{
  string ClientName { get; }
  string ConfigFileName { get; }
  string? ConfigSubFolder { get; }

  string GenerateConfig(
    IEnumerable<McpServer> servers,
    Dictionary<Guid, Dictionary<string, string>>? envOverrides = null,
    Dictionary<Guid, List<string>>? toolOverrides = null,
    string? bridgeArgs = null);
}