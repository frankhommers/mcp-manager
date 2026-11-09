using System.Text;

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

  public static bool HasSecretQuery(string? text) => text != null && MaskQuery(text) != text;

  public static string MaskQuery(string text)
  {
    int query = text.IndexOf('?');
    if (query < 0)
    {
      return text;
    }

    int fragment = text.IndexOf('#', query);
    int end = fragment < 0 ? text.Length : fragment;
    StringBuilder builder = new(text[..(query + 1)]);
    string[] parameters = text[(query + 1)..end].Split('&');
    for (int i = 0; i < parameters.Length; i++)
    {
      string parameter = parameters[i];
      int equals = parameter.IndexOf('=');
      bool secret = equals > 0 && equals < parameter.Length - 1 && IsSecretKey(parameter[..equals]);
      builder.Append(i > 0 ? "&" : string.Empty)
        .Append(secret ? parameter[..(equals + 1)] + Mask(parameter[(equals + 1)..]) : parameter);
    }

    return builder.Append(text[end..]).ToString();
  }
}
