using System.Text;
using System.Text.RegularExpressions;
using McpManager.Core.Models;

namespace McpManager.Core.Services;

public static class CommandLineParser
{
  public static ParsedCommand Parse(string input)
  {
    string original = input ?? string.Empty;
    string normalized = NormalizeLineContinuations(original).Trim();

    if (string.IsNullOrWhiteSpace(normalized))
    {
      return new ParsedCommand(
        string.Empty, [], new Dictionary<string, string>(),
        McpTransportType.Stdio, null, null, original);
    }

    List<string> tokens = Tokenize(normalized);
    if (tokens.Count == 0)
    {
      return new ParsedCommand(
        string.Empty, [], new Dictionary<string, string>(),
        McpTransportType.Stdio, null, null, original);
    }

    Dictionary<string, string> envFromPrefix = ConsumeEnvPrefix(tokens);

    if (tokens.Count == 0)
    {
      return new ParsedCommand(
        string.Empty, [], envFromPrefix,
        McpTransportType.Stdio, null, null, original);
    }

    string command = tokens[0];
    List<string> args = tokens.Count > 1 ? tokens.GetRange(1, tokens.Count - 1) : [];
    return new ParsedCommand(
      command, args, envFromPrefix,
      McpTransportType.Stdio, null, null, original);
  }

  private static string NormalizeLineContinuations(string input)
  {
    string s = input.Replace("\\\r\n", " ").Replace("\\\n", " ");
    return Regex.Replace(s, @"[ \t]+", " ");
  }

  private static List<string> Tokenize(string input)
  {
    List<string> tokens = [];
    StringBuilder current = new();
    bool inSingle = false;
    bool inDouble = false;

    for (int i = 0; i < input.Length; i++)
    {
      char c = input[i];

      if (c == '\\' && !inSingle && i + 1 < input.Length)
      {
        char next = input[i + 1];
        if (inDouble && (next == '"' || next == '\\' || next == '$' || next == '`'))
        {
          current.Append(next);
          i++;
          continue;
        }
        if (!inDouble)
        {
          current.Append(next);
          i++;
          continue;
        }
        current.Append(c);
        continue;
      }

      if (c == '\'' && !inDouble)
      {
        inSingle = !inSingle;
        continue;
      }

      if (c == '"' && !inSingle)
      {
        inDouble = !inDouble;
        continue;
      }

      if ((c == ' ' || c == '\t' || c == '\n') && !inSingle && !inDouble)
      {
        if (current.Length > 0)
        {
          tokens.Add(current.ToString());
          current.Clear();
        }
        continue;
      }

      current.Append(c);
    }

    if (current.Length > 0)
    {
      tokens.Add(current.ToString());
    }

    return tokens;
  }

  private static Dictionary<string, string> ConsumeEnvPrefix(List<string> tokens)
  {
    Dictionary<string, string> env = [];
    while (tokens.Count > 0 && IsEnvAssignment(tokens[0]))
    {
      (string k, string v) = SplitEnv(tokens[0]);
      env[k] = v;
      tokens.RemoveAt(0);
    }
    return env;
  }

  private static bool IsEnvAssignment(string token)
  {
    int eq = token.IndexOf('=');
    if (eq <= 0)
    {
      return false;
    }
    string key = token.Substring(0, eq);
    foreach (char c in key)
    {
      if (!(char.IsLetterOrDigit(c) || c == '_'))
      {
        return false;
      }
    }
    return char.IsLetter(key[0]) || key[0] == '_';
  }

  private static (string Key, string Value) SplitEnv(string token)
  {
    int eq = token.IndexOf('=');
    return (token.Substring(0, eq), token.Substring(eq + 1));
  }
}
