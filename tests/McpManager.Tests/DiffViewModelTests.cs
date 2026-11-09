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

    DiffViewModel empty = new("a", "b", [], []);
    Assert.Null(empty.Current);
    Assert.Equal("", empty.Position);
    Assert.Empty(empty.Fields);
  }
}
