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
        => char.IsLetterOrDigit(c) || c == '\'' || c == '’';

    /// <summary>
    /// Normalizes a single raw word: lower-cased, keeping only letters and
    /// digits (apostrophes and other punctuation are dropped).
    /// </summary>
    public static string NormalizeWord(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Splits arbitrary text (e.g. a speech hypothesis) into normalized match
    /// words, expanding numeric tokens into their spoken words.
    /// </summary>
    public static List<string> ToMatchWords(string text)
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

            string normalized = NormalizeWord(text.Substring(start, i - start));
            if (normalized.Length == 0)
            {
                continue;
            }

            AppendExpanded(normalized, words);
        }

        return words;
    }

    /// <summary>
    /// Tokenizes display text into <see cref="ScriptToken"/> values, preserving
    /// each word's character span in the original string for highlighting.
    /// </summary>
    public static List<ScriptToken> Tokenize(string text)
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
            string normalized = NormalizeWord(text.Substring(start, length));
            if (normalized.Length == 0)
            {
                continue;
            }

            tokens.Add(new ScriptToken(index++, normalized, start, length));
        }

        return tokens;
    }

    private static void AppendExpanded(string normalized, List<string> output)
    {
        if (NumberExpander.IsAllDigits(normalized))
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
