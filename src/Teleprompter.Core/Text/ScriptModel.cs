using System;
using System.Collections.Generic;

namespace Teleprompter.Core.Text;

/// <summary>
/// The immutable, matcher-ready representation of a script.
///
/// It holds two parallel views of the text:
/// <list type="bullet">
/// <item><b>Tokens</b> — one per displayed word, with character spans for the UI.</item>
/// <item><b>MatchWords</b> — the flat sequence the matcher aligns against. A
/// numeric token such as "25" expands to several match words ("twenty", "five")
/// that all point back to the same token, so spoken numbers still map to the
/// right word on screen.</item>
/// </list>
/// </summary>
public sealed class ScriptModel
{
    private readonly int[] _matchWordToToken;
    private readonly int[] _tokenToFirstMatchWord;

    private ScriptModel(
        string text,
        IReadOnlyList<ScriptToken> tokens,
        IReadOnlyList<string> matchWords,
        int[] matchWordToToken,
        int[] tokenToFirstMatchWord,
        IReadOnlyList<int> paragraphStartTokens)
    {
        Text = text;
        Tokens = tokens;
        MatchWords = matchWords;
        _matchWordToToken = matchWordToToken;
        _tokenToFirstMatchWord = tokenToFirstMatchWord;
        ParagraphStartTokens = paragraphStartTokens;
    }

    /// <summary>
    /// Token indices that begin a paragraph (blank-line separated), for section
    /// navigation. The first token is always a paragraph start.
    /// </summary>
    public IReadOnlyList<int> ParagraphStartTokens { get; }

    /// <summary>The original display text, unchanged.</summary>
    public string Text { get; }

    /// <summary>Displayable word tokens with character spans.</summary>
    public IReadOnlyList<ScriptToken> Tokens { get; }

    /// <summary>The normalized word sequence the matcher advances through.</summary>
    public IReadOnlyList<string> MatchWords { get; }

    public int TokenCount => Tokens.Count;

    public int MatchWordCount => MatchWords.Count;

    /// <summary>Maps a match-word index to the display token it belongs to.</summary>
    public int TokenIndexForMatchWord(int matchWordIndex)
    {
        if (matchWordIndex < 0 || matchWordIndex >= _matchWordToToken.Length)
        {
            return -1;
        }

        return _matchWordToToken[matchWordIndex];
    }

    /// <summary>Maps a display token to the first match-word that represents it.</summary>
    public int FirstMatchWordForToken(int tokenIndex)
    {
        if (tokenIndex < 0 || tokenIndex >= _tokenToFirstMatchWord.Length)
        {
            return 0;
        }

        return _tokenToFirstMatchWord[tokenIndex];
    }

    public static ScriptModel Build(string text)
    {
        text ??= string.Empty;
        List<ScriptToken> tokens = TextNormalizer.Tokenize(text);

        var matchWords = new List<string>(tokens.Count);
        var matchWordToToken = new List<int>(tokens.Count);
        var tokenToFirstMatchWord = new int[tokens.Count];

        foreach (ScriptToken token in tokens)
        {
            tokenToFirstMatchWord[token.Index] = matchWords.Count;
            AppendTokenWords(token.Normalized, token.Index, matchWords, matchWordToToken);
        }

        return new ScriptModel(
            text,
            tokens,
            matchWords,
            matchWordToToken.ToArray(),
            tokenToFirstMatchWord,
            FindParagraphStarts(text, tokens));
    }

    /// <summary>A token starts a paragraph when the gap before it holds a blank line.</summary>
    private static List<int> FindParagraphStarts(string text, List<ScriptToken> tokens)
    {
        var starts = new List<int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (i == 0)
            {
                starts.Add(0);
                continue;
            }

            int newlines = 0;
            for (int pos = tokens[i - 1].End; pos < tokens[i].Start; pos++)
            {
                if (text[pos] == '\n')
                {
                    newlines++;
                }
            }

            if (newlines >= 2)
            {
                starts.Add(i);
            }
        }

        return starts;
    }

    private static void AppendTokenWords(
        string normalized,
        int tokenIndex,
        List<string> matchWords,
        List<int> matchWordToToken)
    {
        if (NumberExpander.IsAllDigits(normalized))
        {
            foreach (string word in NumberExpander.ToWords(normalized))
            {
                matchWords.Add(word);
                matchWordToToken.Add(tokenIndex);
            }
        }
        else
        {
            matchWords.Add(normalized);
            matchWordToToken.Add(tokenIndex);
        }
    }
}
