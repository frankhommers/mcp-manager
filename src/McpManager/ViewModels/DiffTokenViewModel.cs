using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public sealed class DiffTokenViewModel
{
  private readonly DiffToken _token;

  public DiffTokenViewModel(DiffToken token, bool isRevealed)
  {
    _token = token;
    DisplayText = isRevealed || !token.IsSecret ? token.Text : MaskText(token.Text);
  }

  public string DisplayText { get; }

  public DiffKind Kind => _token.Kind;

  public bool IsAdded => _token.Kind == DiffKind.Added;

  public bool IsRemoved => _token.Kind == DiffKind.Removed;

  public bool IsSame => _token.Kind == DiffKind.Same;

  private static string MaskText(string text)
  {
    int separator = text.IndexOf('=');
    return separator > 0
      ? text[..(separator + 1)] + SecretMask.Mask(text[(separator + 1)..])
      : SecretMask.Mask(text);
  }
}
