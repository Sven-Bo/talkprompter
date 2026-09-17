using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Teleprompter.Core.Text;

/// <summary>
/// Turns raw text into normalized, matchable word sequences. The same
/// normalization is applied to both the script and the live speech hypotheses
/// so the matcher compares like with like.
/// </summary>
public static class TextNormalizer
{
    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || IsCombiningMark(c) || c == '\'' || c == '’';

    // Accents typed as a separate combining character ("e" + U+0301) belong to
    // the letter before them, not between two words.
    private static bool IsCombiningMark(char c) => CharUnicodeInfo.GetUnicodeCategory(c)
        is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    /// <summary>
    /// Normalizes a single raw word: lower-cased, keeping only letters, digits
    /// and their accents (apostrophes and other punctuation are dropped), in
    /// composed form so "e" + combining acute equals "é" as recognizers write it.
    /// </summary>
    public static string NormalizeWord(string raw) => NormalizeWord(raw, TextRules.English);

    /// <inheritdoc cref="NormalizeWord(string)"/>
    public static string NormalizeWord(string raw, TextRules rules)
    {
        var sb = new StringBuilder(raw.Length);
        bool hasLetterOrDigit = false;
        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c))
            {
                hasLetterOrDigit = true;
                sb.Append(char.ToLower(c, rules.Casing));
            }
            else if (IsCombiningMark(c))
            {
                sb.Append(c);
            }
        }

        if (!hasLetterOrDigit)
        {
            return string.Empty; // a stray accent is not a word
        }

        string word = sb.ToString();
        return word.IsNormalized(NormalizationForm.FormC) ? word : word.Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Splits arbitrary text (e.g. a speech hypothesis) into normalized match
    /// words, expanding numeric tokens into their spoken words.
    /// </summary>
    public static List<string> ToMatchWords(string text) => ToMatchWords(text, TextRules.English);

    /// <inheritdoc cref="ToMatchWords(string)"/>
    public static List<string> ToMatchWords(string text, TextRules rules)
    {
        var words = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return words;
        }

        int i = 0;
        int n = text.Length;
        while (i < n)
        {
            if (!IsWordChar(text[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < n && IsWordChar(text[i]))
            {
                i++;
            }

            string normalized = NormalizeWord(text.Substring(start, i - start), rules);
            if (normalized.Length == 0)
            {
                continue;
            }

            AppendExpanded(normalized, words, rules);
        }

        return words;
    }

    /// <summary>
    /// Tokenizes display text into <see cref="ScriptToken"/> values, preserving
    /// each word's character span in the original string for highlighting.
    /// </summary>
    public static List<ScriptToken> Tokenize(string text) => Tokenize(text, TextRules.English);

    /// <inheritdoc cref="Tokenize(string)"/>
    public static List<ScriptToken> Tokenize(string text, TextRules rules)
    {
        var tokens = new List<ScriptToken>();
        if (string.IsNullOrEmpty(text))
        {
            return tokens;
        }

        int i = 0;
        int n = text.Length;
        int index = 0;
        while (i < n)
        {
            if (!IsWordChar(text[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < n && IsWordChar(text[i]))
            {
                i++;
            }

            int length = i - start;
            string normalized = NormalizeWord(text.Substring(start, length), rules);
            if (normalized.Length == 0)
            {
                continue;
            }

            tokens.Add(new ScriptToken(index++, normalized, start, length));
        }

        return tokens;
    }

    private static void AppendExpanded(string normalized, List<string> output, TextRules rules)
    {
        if (rules.ExpandNumbers && NumberExpander.IsAllDigits(normalized))
        {
            foreach (string word in NumberExpander.ToWords(normalized))
            {
                output.Add(word);
            }
        }
        else
        {
            output.Add(normalized);
        }
    }
}
