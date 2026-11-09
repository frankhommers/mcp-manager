using McpManager.Core.Models;
using McpManager.ViewModels;

namespace McpManager.Tests;

public class DiffViewModelTests
{
  private static ServerDiff Server(string name, params FieldDiff[] fields) => new(name, fields);

  private static FieldDiff Field(string field, DiffKind kind, bool isSecret, DiffToken[] left, DiffToken[] right) =>
    new(field, kind, isSecret, left, right);

  private static DiffViewModel Create(int count, params DiffChoice[] choices) => new(
    "In file",
    "MCP Manager will write",
    [.. Enumerable.Range(1, count).Select(i => Server($"s{i}"))],
    choices);

  [Fact]
  public void Secrets_masked_until_revealed()
  {
    ServerDiff server = Server(
      "jira",
      Field(
        "env:JIRA_API_TOKEN",
        DiffKind.Changed,
        true,
        [new DiffToken("abcdefgh1234", DiffKind.Changed, true)],
        [new DiffToken("zyxwvuts5678", DiffKind.Changed, true)]),
      Field(
        "args",
        DiffKind.Changed,
        false,
        [new DiffToken("--token=secretvalue9999", DiffKind.Removed, true), new DiffToken("run", DiffKind.Same, false)],
        [new DiffToken("run", DiffKind.Same, false)]));
    DiffViewModel diff = new("In file", "Write", [server], []);

    FieldDiffViewModel token = diff.Fields[0];
    Assert.Equal("JIRA_API_TOKEN", token.Label);
    Assert.Equal("env:JIRA_API_TOKEN", token.Field);
    Assert.Equal("••••••••1234", token.LeftText);
    Assert.Equal("••••••••5678", token.RightText);

    token.ToggleRevealCommand.Execute(null);
    Assert.Equal("abcdefgh1234", token.LeftText);
    Assert.Equal("zyxwvuts5678", token.RightText);

    token.ToggleRevealCommand.Execute(null);
    Assert.Equal("••••••••1234", token.LeftText);

    FieldDiffViewModel args = diff.Fields[1];
    Assert.Equal("--token=••••••••9999\nrun", args.LeftText);
    Assert.True(args.LeftTokens[0].IsRemoved);
    Assert.True(args.LeftTokens[1].IsSame);
    args.ToggleRevealCommand.Execute(null);
    Assert.Equal("--token=secretvalue9999\nrun", args.LeftText);
    Assert.Equal("--token=secretvalue9999", args.LeftTokens[0].DisplayText);
  }

  [Fact]
  public void Whole_value_secrets_containing_equals_are_fully_masked()
  {
    FieldDiffViewModel env = new(Field(
      "env:API_KEY", DiffKind.Changed, true, [new DiffToken("abc123xyz==", DiffKind.Changed, true)], []));
    FieldDiffViewModel header = new(Field(
      "header:Authorization", DiffKind.Changed, true, [new DiffToken("sk-ab=cdef1234", DiffKind.Changed, true)], []));

    Assert.Equal("••••••••yz==", env.LeftText);
    Assert.Equal("••••••••1234", header.LeftText);
    env.ToggleRevealCommand.Execute(null);
    Assert.Equal("abc123xyz==", env.LeftText);
  }

  [Fact]
  public void Args_secret_with_secret_name_keeps_name_and_masks_value()
  {
    FieldDiffViewModel args = new(Field(
      "args",
      DiffKind.Changed,
      false,
      [
        new DiffToken("JIRA_API_TOKEN=abc=def", DiffKind.Same, true),
        new DiffToken("PLAIN=value1234", DiffKind.Same, true),
      ],
      []));

    Assert.Equal("JIRA_API_TOKEN=••••••••=def\n••••••••1234", args.LeftText);
    args.ToggleRevealCommand.Execute(null);
    Assert.Equal("JIRA_API_TOKEN=abc=def\nPLAIN=value1234", args.LeftText);
  }

  [Fact]
  public void Secret_url_query_masks_only_the_value()
  {
    FieldDiffViewModel url = new(Field(
      "url", DiffKind.Changed, true,
      [new DiffToken("https://example.com/mcp?api_key=abcdef123456&mode=fast", DiffKind.Changed, true)], []));
    FieldDiffViewModel args = new(Field(
      "args", DiffKind.Changed, true,
      [new DiffToken("--url=https://example.com/sse?token=zyxw9876", DiffKind.Removed, true)], []));

    Assert.Equal("https://example.com/mcp?api_key=••••••••3456&mode=fast", url.LeftText);
    Assert.Equal("--url=https://example.com/sse?token=••••••••9876", args.LeftText);
    url.ToggleRevealCommand.Execute(null);
    Assert.Equal("https://example.com/mcp?api_key=abcdef123456&mode=fast", url.LeftText);
  }

  [Fact]
  public void Secret_header_values_mask_whole_value_even_with_query()
  {
    FieldDiffViewModel header = new(Field(
      "header:Authorization", DiffKind.Changed, true,
      [new DiffToken("https://token.example.com/?token=abcdef123456", DiffKind.Changed, true)], []));
    FieldDiffViewModel args = new(Field(
      "args", DiffKind.Changed, true,
      [
        new DiffToken("Authorization", DiffKind.Same, false),
        new DiffToken("Bearer abcdef123456", DiffKind.Same, true),
        new DiffToken("Authorization: Bearer zyxw9876", DiffKind.Same, true),
      ], []));

    Assert.Equal("••••••••3456", header.LeftText);
    Assert.Equal("Authorization\n••••••••3456\nAuthorization:••••••••9876", args.LeftText);
  }

  [Fact]
  public void Same_fields_are_dimmed_and_header_labels_are_stripped()
  {
    FieldDiffViewModel same = new(Field("header:X-Id", DiffKind.Same, false, [], []));
    FieldDiffViewModel changed = new(Field("url", DiffKind.Changed, false, [], []));

    Assert.True(same.IsDimmed);
    Assert.Equal("X-Id", same.Label);
    Assert.False(changed.IsDimmed);
    Assert.Equal("url", changed.Label);
  }

  [Fact]
  public void Cannot_complete_until_every_item_has_choice()
  {
    DiffViewModel diff = Create(2, DiffChoice.KeepLibrary, DiffChoice.UseFoundVersion);

    Assert.False(diff.IsComplete);
    diff.SelectedChoice = DiffChoice.KeepLibrary;
    Assert.False(diff.IsComplete);

    diff.NextCommand.Execute(null);
    diff.SelectedChoice = DiffChoice.UseFoundVersion;

    Assert.True(diff.IsComplete);
    Assert.Equal([DiffChoice.KeepLibrary, DiffChoice.UseFoundVersion], diff.Choices);
  }

  [Fact]
  public void Unavailable_choices_are_hidden_per_item()
  {
    DiffViewModel diff = new(
      "Library",
      "Found",
      [Server("s1"), Server("s2")],
      [DiffChoice.KeepLibrary, DiffChoice.UseFoundVersion, DiffChoice.ImportSeparately],
      (index, choice) => index == 0 || choice != DiffChoice.UseFoundVersion);

    Assert.Equal(3, diff.Options.Count);
    diff.NextCommand.Execute(null);

    Assert.Equal([DiffChoice.KeepLibrary, DiffChoice.ImportSeparately], diff.Options.Select(o => o.Choice));
    diff.SelectedChoice = DiffChoice.UseFoundVersion;
    Assert.Null(diff.SelectedChoice);
  }

  [Fact]
  public void Navigation_keeps_choice_per_item()
  {
    DiffViewModel diff = Create(3, DiffChoice.KeepLibrary, DiffChoice.UseFoundVersion);

    Assert.Equal("1 of 3", diff.Position);
    Assert.False(diff.PreviousCommand.CanExecute(null));
    diff.SelectedChoice = DiffChoice.UseFoundVersion;

    diff.NextCommand.Execute(null);
    Assert.Equal("2 of 3", diff.Position);
    Assert.Equal("s2", diff.Current!.ServerName);
    Assert.Null(diff.SelectedChoice);
    diff.SelectedChoice = DiffChoice.KeepLibrary;

    diff.NextCommand.Execute(null);
    Assert.False(diff.CanGoNext);
    Assert.False(diff.NextCommand.CanExecute(null));

    diff.PreviousCommand.Execute(null);
    diff.PreviousCommand.Execute(null);
    Assert.Equal(DiffChoice.UseFoundVersion, diff.SelectedChoice);
    Assert.True(diff.CanGoNext);
  }

  [Fact]
  public void Empty_choices_list_is_complete_and_empty_items_have_no_current()
  {
    DiffViewModel withoutChoices = Create(1);
    Assert.True(withoutChoices.IsComplete);
    Assert.False(withoutChoices.HasChoices);
    Assert.False(withoutChoices.HasMultipleItems);
    Assert.True(withoutChoices.HasTitle);

    DiffViewModel empty = new("a", "b", [], []);
    Assert.Null(empty.Current);
    Assert.False(empty.HasTitle);
    Assert.Equal("", empty.Position);
    Assert.Empty(empty.Fields);
  }
}
