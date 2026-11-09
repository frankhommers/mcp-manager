using McpManager.Core.Models;

namespace McpManager.Core.Services;

public static class ServerDiffService
{
  public static ServerDiff Compare(McpServer left, McpServer right)
  {
    List<FieldDiff> fields = [];

    AddIfPresent(fields, Scalar("type", left.TransportType.GetDisplayName(), right.TransportType.GetDisplayName()));
    AddIfPresent(fields, Scalar("command", left.Command, right.Command));

    FieldDiff? args = Args(left.Args, right.Args);
    if (args != null)
    {
      fields.Add(args);
    }

    AddIfPresent(fields, Scalar("url", left.Url, right.Url));
    AddIfPresent(fields, Scalar("cwd", left.WorkingDirectory, right.WorkingDirectory));

    fields.AddRange(Map("env:", left.EnvironmentVariables, right.EnvironmentVariables,
      StringComparer.Ordinal, StringComparer.Ordinal, ManagedServerIdentity.EnvironmentKey));
    fields.AddRange(Map("header:", left.HttpHeaders, right.HttpHeaders,
      StringComparer.OrdinalIgnoreCase, StringComparer.OrdinalIgnoreCase, ManagedServerIdentity.HeaderKey));

    FieldDiff? tools = Tools(left.AlwaysAllow, right.AlwaysAllow);
    if (tools != null)
    {
      fields.Add(tools);
    }

    string name = string.IsNullOrEmpty(left.Name) ? right.Name : left.Name;
    return new ServerDiff(name, fields);
  }

  private static void AddIfPresent(List<FieldDiff> fields, FieldDiff? field)
  {
    if (field != null)
    {
      fields.Add(field);
    }
  }

  private static FieldDiff? Scalar(string field, string? left, string? right)
  {
    bool hasLeft = !string.IsNullOrEmpty(left);
    bool hasRight = !string.IsNullOrEmpty(right);
    if (!hasLeft && !hasRight)
    {
      return null;
    }

    return Value(field, hasLeft ? left : null, hasRight ? right : null, false);
  }

  private static FieldDiff Value(string field, string? left, string? right, bool secretKey)
  {
    DiffKind kind = left == null ? DiffKind.Added
      : right == null ? DiffKind.Removed
      : string.Equals(left, right, StringComparison.Ordinal) ? DiffKind.Same
      : DiffKind.Changed;

    DiffKind leftKind = kind == DiffKind.Same ? DiffKind.Same : kind == DiffKind.Changed ? DiffKind.Changed : DiffKind.Removed;
    DiffKind rightKind = kind == DiffKind.Same ? DiffKind.Same : kind == DiffKind.Changed ? DiffKind.Changed : DiffKind.Added;

    bool leftSecret = secretKey || SecretMask.HasSecretQuery(left);
    bool rightSecret = secretKey || SecretMask.HasSecretQuery(right);
    List<DiffToken> leftTokens = left == null ? [] : [new DiffToken(left, leftKind, leftSecret)];
    List<DiffToken> rightTokens = right == null ? [] : [new DiffToken(right, rightKind, rightSecret)];
    return new FieldDiff(field, kind, leftSecret || rightSecret, leftTokens, rightTokens);
  }

  private static IEnumerable<FieldDiff> Map(
    string prefix,
    Dictionary<string, string> left,
    Dictionary<string, string> right,
    StringComparer lookupComparer,
    StringComparer sortComparer,
    string managedKey)
  {
    Dictionary<string, string> leftMap = new(left.Where(p => !lookupComparer.Equals(p.Key, managedKey)), lookupComparer);
    Dictionary<string, string> rightMap = new(right.Where(p => !lookupComparer.Equals(p.Key, managedKey)), lookupComparer);

    return leftMap.Keys.Union(rightMap.Keys, lookupComparer)
      .OrderBy(k => k, sortComparer)
      .Select(key =>
      {
        string? l = leftMap.TryGetValue(key, out string? lv) ? lv : null;
        string? r = rightMap.TryGetValue(key, out string? rv) ? rv : null;
        return Value(prefix + key, l, r, SecretMask.IsSecretKey(key));
      });
  }

  private static FieldDiff? Tools(List<string> left, List<string> right)
  {
    if (left.Count == 0 && right.Count == 0)
    {
      return null;
    }

    HashSet<string> leftSet = new(left, StringComparer.Ordinal);
    HashSet<string> rightSet = new(right, StringComparer.Ordinal);
    List<DiffToken> leftTokens = leftSet.Order(StringComparer.Ordinal)
      .Select(t => new DiffToken(t, rightSet.Contains(t) ? DiffKind.Same : DiffKind.Removed, false)).ToList();
    List<DiffToken> rightTokens = rightSet.Order(StringComparer.Ordinal)
      .Select(t => new DiffToken(t, leftSet.Contains(t) ? DiffKind.Same : DiffKind.Added, false)).ToList();
    return Build("tools", leftTokens, rightTokens);
  }

  private static FieldDiff? Args(List<string> left, List<string> right)
  {
    if (left.Count == 0 && right.Count == 0)
    {
      return null;
    }

    bool[] leftSecret = MarkSecrets(left);
    bool[] rightSecret = MarkSecrets(right);
    int[,] lcs = new int[left.Count + 1, right.Count + 1];
    for (int i = left.Count - 1; i >= 0; i--)
    {
      for (int j = right.Count - 1; j >= 0; j--)
      {
        lcs[i, j] = left[i] == right[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
      }
    }

    List<DiffToken> leftTokens = [];
    List<DiffToken> rightTokens = [];
    int a = 0;
    int b = 0;
    while (a < left.Count && b < right.Count)
    {
      if (left[a] == right[b])
      {
        leftTokens.Add(new DiffToken(left[a], DiffKind.Same, leftSecret[a] || rightSecret[b]));
        rightTokens.Add(new DiffToken(right[b], DiffKind.Same, leftSecret[a] || rightSecret[b]));
        a++;
        b++;
      }
      else if (lcs[a + 1, b] >= lcs[a, b + 1])
      {
        leftTokens.Add(new DiffToken(left[a], DiffKind.Removed, leftSecret[a]));
        a++;
      }
      else
      {
        rightTokens.Add(new DiffToken(right[b], DiffKind.Added, rightSecret[b]));
        b++;
      }
    }

    for (; a < left.Count; a++)
    {
      leftTokens.Add(new DiffToken(left[a], DiffKind.Removed, leftSecret[a]));
    }

    for (; b < right.Count; b++)
    {
      rightTokens.Add(new DiffToken(right[b], DiffKind.Added, rightSecret[b]));
    }

    return Build("args", leftTokens, rightTokens);
  }

  private static FieldDiff Build(string field, List<DiffToken> left, List<DiffToken> right)
  {
    bool leftChanged = left.Any(t => t.Kind != DiffKind.Same);
    bool rightChanged = right.Any(t => t.Kind != DiffKind.Same);
    DiffKind kind = !leftChanged && !rightChanged ? DiffKind.Same
      : left.Count == 0 ? DiffKind.Added
      : right.Count == 0 ? DiffKind.Removed
      : DiffKind.Changed;
    bool secret = left.Concat(right).Any(t => t.IsSecret);
    return new FieldDiff(field, kind, secret, left, right);
  }

  private static bool[] MarkSecrets(List<string> args)
  {
    bool[] secret = new bool[args.Count];
    for (int i = 0; i < args.Count; i++)
    {
      string arg = args[i];
      int separator = arg.IndexOfAny(['=', ':']);
      if (separator > 0 && SecretMask.IsSecretKey(arg[..separator].TrimStart('-')))
      {
        secret[i] = true;
      }
      else if (i > 0 && !secret[i - 1] && !arg.StartsWith('-') && !args[i - 1].Contains('=')
        && SecretMask.IsSecretKey(args[i - 1].TrimStart('-')))
      {
        secret[i] = true;
      }
      else
      {
        secret[i] = SecretMask.HasSecretQuery(arg);
      }
    }

    return secret;
  }
}
