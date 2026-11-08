using System.Security.Cryptography;
using System.Text;
using McpManager.Core.Models;

namespace McpManager.Core.Services;

public static class TargetSyncService
{
  public static string FileHash(string path) => File.Exists(path)
    ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "missing";

  public static string ContentHash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

  public static bool HasExternalChanges(TargetFolder target) => target.AppliedFileHashes
    .Any(pair => FileHash(pair.Key) != pair.Value);

  public static void RecordApplied(TargetFolder target, IReadOnlyDictionary<string, string> configs)
  {
    target.AppliedFileHashes = configs.ToDictionary(pair => pair.Key, pair => ContentHash(pair.Value));
    target.LastAppliedAt = DateTimeOffset.UtcNow;
  }

  public static bool NeedsUpdate(IReadOnlyDictionary<string, string> configs) => configs
    .Any(pair => FileHash(pair.Key) != ContentHash(pair.Value));
}
