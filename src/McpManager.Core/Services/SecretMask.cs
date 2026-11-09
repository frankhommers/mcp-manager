namespace McpManager.Core.Services;

public static class SecretMask
{
  private const string Bullets = "••••••••";
  private const int VisibleCount = 4;

  private static readonly string[] SecretFragments = ["TOKEN", "KEY", "SECRET", "PASSWORD", "AUTH"];

  public static bool IsSecretKey(string key) =>
    !string.IsNullOrEmpty(key)
    && (string.Equals(key, "Authorization", StringComparison.OrdinalIgnoreCase)
      || SecretFragments.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase)));

  public static string Mask(string value) =>
    value.Length <= VisibleCount ? Bullets : Bullets + value[^VisibleCount..];
}
