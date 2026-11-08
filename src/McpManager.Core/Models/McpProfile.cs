namespace McpManager.Core.Models;

public class McpProfile
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public string Name { get; set; } = "New profile";
  public HashSet<Guid> EnabledServers { get; set; } = [];
  public Dictionary<Guid, List<string>> ServerToolOverrides { get; set; } = [];
}
