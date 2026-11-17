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

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(LeftTokens), nameof(RightTokens))]
  private DiffSide? _keptSide;

  public FieldDiffViewModel(FieldDiff diff, DiffSide? keptSide = DiffSide.Right)
  {
    _diff = diff;
    _keptSide = keptSide;
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

  public IReadOnlyList<DiffTokenViewModel> LeftTokens => Build(_diff.Left, IsKept(DiffSide.Left));

  public IReadOnlyList<DiffTokenViewModel> RightTokens => Build(_diff.Right, IsKept(DiffSide.Right));

  public string LeftText => Join(LeftTokens);

  public string RightText => Join(RightTokens);

  [RelayCommand]
  private void ToggleReveal() => IsRevealed = !IsRevealed;

  private bool? IsKept(DiffSide side) => KeptSide is { } kept ? kept == side || kept == DiffSide.Both : null;

  private List<DiffTokenViewModel> Build(IReadOnlyList<DiffToken> tokens, bool? isKept) =>
  [
    .. tokens.Select(t => new DiffTokenViewModel(
      t, IsRevealed, _diff.Field == ServerDiffService.ArgsField, HasSecretKey, isKept)),
  ];

  private bool HasSecretKey => Label != _diff.Field && SecretMask.IsSecretKey(Label);

  private static string Join(IReadOnlyList<DiffTokenViewModel> tokens) =>
    string.Join("\n", tokens.Select(t => t.DisplayText));
}
