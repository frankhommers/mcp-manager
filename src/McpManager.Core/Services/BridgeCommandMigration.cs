using McpManager.Core.Models;

namespace McpManager.Core.Services;

public static class BridgeCommandMigration
{
  private const string ArgsPlaceholder = "{args}";
  private const string HeaderArgsPlaceholder = "{headerArgs}";
  private const string LegacyHeadersPlaceholder = "{headers}";
  private const string UrlPlaceholder = "{url}";
  private const string McpProxyExecutable = "mcp-proxy";

  public static void Apply(GlobalSettings settings)
  {
    settings.BridgeCommandHttp = Migrate(settings.BridgeCommandHttp);
    settings.BridgeCommandSse = Migrate(settings.BridgeCommandSse);
    settings.BridgeCommandStreamableHttp = Migrate(settings.BridgeCommandStreamableHttp);
  }

  public static string Migrate(string command)
  {
    if (string.IsNullOrWhiteSpace(command))
    {
      return command;
    }

    string migrated = command.Replace(LegacyHeadersPlaceholder, HeaderArgsPlaceholder);
    if (!migrated.Contains(ArgsPlaceholder))
    {
      migrated = InsertBeforeUrl(migrated, ArgsPlaceholder);
    }

    ParsedCommand parsed = CommandLineParser.Parse(migrated);
    if (Path.GetFileNameWithoutExtension(parsed.Command).Equals(McpProxyExecutable, StringComparison.OrdinalIgnoreCase)
        && !migrated.Contains(HeaderArgsPlaceholder))
    {
      migrated = InsertBeforeUrl(migrated, HeaderArgsPlaceholder);
    }

    return migrated;
  }

  private static string InsertBeforeUrl(string command, string placeholder)
  {
    int urlIndex = command.IndexOf(UrlPlaceholder, StringComparison.Ordinal);
    return urlIndex >= 0 ? command.Insert(urlIndex, placeholder + " ") : $"{command} {placeholder}";
  }
}
