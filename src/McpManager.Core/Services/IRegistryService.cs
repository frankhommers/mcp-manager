using McpManager.Core.Models;

namespace McpManager.Core.Services;

public interface IRegistryService
{
  Task<McpRegistry> LoadAsync();
  Task SaveAsync(McpRegistry registry);
  string RegistryFilePath { get; }
}