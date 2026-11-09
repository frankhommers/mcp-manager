using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public sealed class DiffTokenViewModel
{
  private readonly DiffToken _token;

  public DiffTokenViewModel(DiffToken token, bool isRevealed, bool splitNamedValue = false, bool wholeValue = false)
  {
    _token = token;
    DisplayText = isRevealed || !token.IsSecret ? token.Text : MaskText(token.Text, splitNamedValue, wholeValue);
  }

  public string DisplayText { get; }

  public DiffKind Kind => _token.Kind;

  public bool IsAdded => _token.Kind == DiffKind.Added;

  public bool IsRemoved => _token.Kind == DiffKind.Removed;

  public bool IsSame => _token.Kind == DiffKind.Same;

  private static string MaskText(string text, bool splitNamedValue, bool wholeValue)
  {
    int separator = splitNamedValue ? text.IndexOfAny(['=', ':']) : -1;
    if (separator > 0 && SecretMask.IsSecretKey(text[..separator].TrimStart('-')))
    {
      return text[..(separator + 1)] + SecretMask.Mask(text[(separator + 1)..]);
    }

    return !wholeValue && SecretMask.HasSecretQuery(text) ? SecretMask.MaskQuery(text) : SecretMask.Mask(text);
  }
}
