using System.Text.Json;
using McpManager.Core.Models;

namespace McpManager.Core.Services;

public static class ProfileResolver
{
  public static TargetFolder Resolve(TargetFolder target, IEnumerable<McpProfile> profiles)
  {
    TargetFolder effective = JsonSerializer.Deserialize<TargetFolder>(JsonSerializer.Serialize(target))!;
    if (target.ProfileId is not Guid profileId) return effective;
    McpProfile profile = profiles.SingleOrDefault(p => p.Id == profileId)
      ?? throw new InvalidOperationException($"Profile for '{target.Name}' no longer exists.");
    effective.EnabledServers.UnionWith(profile.EnabledServers);
    effective.EnabledServers.ExceptWith(target.DisabledServers);
    foreach ((Guid serverId, List<string> tools) in profile.ServerToolOverrides)
    {
      effective.ServerToolOverrides.TryAdd(serverId, [.. tools]);
    }
    return effective;
  }
}
