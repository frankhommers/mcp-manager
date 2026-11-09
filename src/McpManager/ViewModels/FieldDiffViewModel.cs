using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public partial class FieldDiffViewModel : ViewModelBase
{
  private readonly FieldDiff _diff;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(LeftTokens), nameof(RightTokens), nameof(LeftText), nameof(RightText))]
  [NotifyPropertyChangedFor(nameof(RevealLabel))]
  private bool _isRevealed;

  public FieldDiffViewModel(FieldDiff diff)
  {
    _diff = diff;
  }

  public string Field => _diff.Field;

  public string Label => _diff.Field.StartsWith(ServerDiffService.EnvPrefix, StringComparison.Ordinal)
    || _diff.Field.StartsWith(ServerDiffService.HeaderPrefix, StringComparison.Ordinal)
    ? _diff.Field[(_diff.Field.IndexOf(':') + 1)..]
    : _diff.Field;

  public DiffKind Kind => _diff.Kind;

  public bool IsSecret => _diff.IsSecret || _diff.Left.Concat(_diff.Right).Any(t => t.IsSecret);

  public bool IsDimmed => _diff.Kind == DiffKind.Same;

  public string RevealLabel => IsRevealed ? "Hide" : "Show";

  public IReadOnlyList<DiffTokenViewModel> LeftTokens => Build(_diff.Left);

  public IReadOnlyList<DiffTokenViewModel> RightTokens => Build(_diff.Right);

  public string LeftText => Join(LeftTokens);

  public string RightText => Join(RightTokens);

  [RelayCommand]
  private void ToggleReveal() => IsRevealed = !IsRevealed;

  private List<DiffTokenViewModel> Build(IReadOnlyList<DiffToken> tokens) =>
    [.. tokens.Select(t => new DiffTokenViewModel(t, IsRevealed, _diff.Field == ServerDiffService.ArgsField, HasSecretKey))];

  private bool HasSecretKey => Label != _diff.Field && SecretMask.IsSecretKey(Label);

  private static string Join(IReadOnlyList<DiffTokenViewModel> tokens) =>
    string.Join("\n", tokens.Select(t => t.DisplayText));
}
