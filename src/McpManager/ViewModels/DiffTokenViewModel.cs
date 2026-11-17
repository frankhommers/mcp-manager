using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public sealed class DiffTokenViewModel
{
  private readonly DiffToken _token;

  private readonly bool? _isKept;

  public DiffTokenViewModel(
    DiffToken token, bool isRevealed, bool splitNamedValue = false, bool wholeValue = false, bool? isKept = null)
  {
    _token = token;
    _isKept = isKept;
    DisplayText = isRevealed || !token.IsSecret ? token.Text : MaskText(token.Text, splitNamedValue, wholeValue);
  }

  public string DisplayText { get; }

  public DiffKind Kind => _token.Kind;

  public bool IsSame => _token.Kind == DiffKind.Same;

  public bool IsKept => !IsSame && _isKept == true;

  public bool IsDropped => !IsSame && _isKept == false;

  public bool IsPending => !IsSame && _isKept == null;

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
