using McpManager.Core.Models;

namespace McpManager.Core.Services;

public static class BridgeCommandResolver
{
  public static ResolvedCommand Resolve(
    string commandTemplate,
    string url,
    string? bridgeArgs,
    string? headerArgumentTemplate,
    IReadOnlyDictionary<string, string> headers)
  {
    ParsedCommand parsedTemplate = CommandLineParser.Parse(commandTemplate);
    List<string> arguments = [];
    HashSet<int> sensitiveArgumentIndexes = [];

    foreach (string argument in parsedTemplate.Args)
    {
      switch (argument)
      {
        case "{args}":
          arguments.AddRange(ParseArguments(bridgeArgs));
          break;

        case "{headerArgs}":
        case "{headers}":
          AddHeaderArguments(
            headerArgumentTemplate,
            headers,
            arguments,
            sensitiveArgumentIndexes);
          break;

        case "{url}":
          arguments.Add(url);
          break;

        default:
          arguments.Add(argument.Replace("{url}", url));
          break;
      }
    }

    return new ResolvedCommand(parsedTemplate.Command, arguments, sensitiveArgumentIndexes);
  }

  private static void AddHeaderArguments(
    string? headerArgumentTemplate,
    IReadOnlyDictionary<string, string> headers,
    List<string> arguments,
    HashSet<int> sensitiveArgumentIndexes)
  {
    if (string.IsNullOrWhiteSpace(headerArgumentTemplate) || headers.Count == 0)
    {
      return;
    }

    ParsedCommand parsedTemplate = CommandLineParser.Parse($"header-arguments {headerArgumentTemplate}");

    foreach ((string key, string value) in headers)
    {
      foreach (string argument in parsedTemplate.Args)
      {
        int argumentIndex = arguments.Count;
        arguments.Add(argument
          .Replace("{key}", key)
          .Replace("{value}", value));

        if (argument.Contains("{value}"))
        {
          sensitiveArgumentIndexes.Add(argumentIndex);
        }
      }
    }
  }

  private static IReadOnlyList<string> ParseArguments(string? arguments)
  {
    if (string.IsNullOrWhiteSpace(arguments))
    {
      return [];
    }

    ParsedCommand parsed = CommandLineParser.Parse($"bridge-arguments {arguments}");
    return parsed.Args;
  }
}
