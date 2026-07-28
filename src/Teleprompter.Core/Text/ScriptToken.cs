namespace Teleprompter.Core.Text;

/// <summary>
/// One word from the script, carrying both the normalized form used for
/// matching and the exact character span in the original display text so the
/// UI can highlight and scroll to it.
/// </summary>
/// <param name="Index">Zero-based position of this token in the script.</param>
/// <param name="Normalized">Lower-cased, punctuation-stripped form used for matching.</param>
/// <param name="Start">Character offset of the word in the original text.</param>
/// <param name="Length">Character length of the word in the original text.</param>
public sealed record ScriptToken(int Index, string Normalized, int Start, int Length)
{
    public int End => Start + Length;
}
