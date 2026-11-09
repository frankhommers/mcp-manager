using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public sealed class DiffTokenViewModel
{
  private readonly DiffToken _token;

  public DiffTokenViewModel(DiffToken token, bool isRevealed, bool splitNamedValue = false)
  {
    _token = token;
    DisplayText = isRevealed || !token.IsSecret ? token.Text : MaskText(token.Text, splitNamedValue);
  }

  public string DisplayText { get; }

  public DiffKind Kind => _token.Kind;

  public bool IsAdded => _token.Kind == DiffKind.Added;

  public bool IsRemoved => _token.Kind == DiffKind.Removed;

  public bool IsSame => _token.Kind == DiffKind.Same;

  private static string MaskText(string text, bool splitNamedValue)
  {
    int separator = splitNamedValue ? text.IndexOf('=') : -1;
    return separator > 0 && SecretMask.IsSecretKey(text[..separator])
      ? text[..(separator + 1)] + SecretMask.Mask(text[(separator + 1)..])
      : SecretMask.Mask(text);
  }
}
