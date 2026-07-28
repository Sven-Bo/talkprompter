using System;
using System.Collections.Generic;
using Teleprompter.Core.Text;

namespace Teleprompter.Core.Matching;

/// <summary>
/// Tracks where a speaker is in a known script by fuzzily aligning the most
/// recent spoken words against a small window of script just ahead of the last
/// matched position (the "anchor").
///
/// The behavior the app needs falls out of one rule: advance the anchor only
/// when recent speech matches the script near it. If the reader pauses or goes
/// off-script, nothing matches, the anchor holds, and scrolling stops. When
/// they resume, matches reappear near the anchor and it advances again. If the
/// hold persists, re-acquisition scans further ahead to catch a skipped
/// paragraph.
///
/// The matcher is deterministic and free of timers or audio dependencies, so
/// the whole tracking behavior can be unit-tested by feeding it strings.
/// </summary>
public sealed class ScriptMatcher
{
    private readonly ScriptModel _script;

    private int _anchor;       // index into MatchWords of the next expected word
    private int _noMatchCount;
    private TrackingState _state = TrackingState.Idle;

    public ScriptMatcher(ScriptModel script, MatcherOptions? options = null)
    {
        _script = script ?? throw new ArgumentNullException(nameof(script));
        Options = options ?? new MatcherOptions();
    }

    /// <summary>Live-tunable matching options (e.g. a sensitivity slider).</summary>
    public MatcherOptions Options { get; set; }

    /// <summary>The display token the reader is currently on, or -1 before any match.</summary>
    public int CurrentTokenIndex
        => _anchor > 0 ? _script.TokenIndexForMatchWord(_anchor - 1) : -1;

    public TrackingState State => _state;

    /// <summary>
    /// Feeds a speech hypothesis (partial or final) and returns the updated
    /// position. Safe to call on every partial result.
    /// </summary>
    public MatchUpdate Process(string hypothesis)
    {
        List<string> words = TextNormalizer.ToMatchWords(hypothesis);
        if (words.Count == 0 || _script.MatchWordCount == 0)
        {
            return Snapshot(advanced: false);
        }

        int tail = Math.Min(words.Count, Options.TailWords);
        List<string> spoken = words.GetRange(words.Count - tail, tail);

        // Fast path: a lone word that is exactly the next expected script word
        // advances immediately. Without this, the first word spoken after every
        // pause or utterance boundary could not move the highlight until a
        // second word arrived, which reads as lag at the start of each line.
        if (spoken.Count < Options.MinMatchedWords
            && spoken.Count == 1
            && _anchor < _script.MatchWordCount
            && string.Equals(spoken[0], _script.MatchWords[_anchor], StringComparison.Ordinal))
        {
            return Accept(new SearchResult(true, _anchor + 1, 1.0, Advanced: true));
        }

        // Not enough evidence yet: hold rather than risk a spurious jump.
        if (spoken.Count < Options.MinMatchedWords)
        {
            return Snapshot(advanced: false);
        }

        // The window looks back far enough to cover the whole spoken tail (a
        // streaming partial grows from the utterance start, so its earliest
        // words may sit behind the anchor) and ahead by ForwardWindow.
        SearchResult local = Search(
            spoken,
            from: Math.Max(0, _anchor - spoken.Count - Options.BackTolerance),
            to: Math.Min(_script.MatchWordCount, _anchor + spoken.Count + Options.ForwardWindow),
            threshold: Options.AcceptThreshold);

        if (local.Accepted && local.NewAnchor <= _anchor + spoken.Count + Options.MaxJump)
        {
            return Accept(local);
        }

        _noMatchCount++;

        if (_noMatchCount >= Options.LostThreshold)
        {
            SearchResult forward = Search(
                spoken,
                from: _anchor,
                to: Math.Min(_script.MatchWordCount, _anchor + Options.ReacquireWindow),
                threshold: Options.ReacquireThreshold);

            // A flubbed take usually means re-reading an EARLIER sentence, so
            // scan a window behind the anchor too and take the better match.
            SearchResult backward = default;
            if (spoken.Count >= Options.ReacquireBackMinWords && _anchor > 0)
            {
                backward = Search(
                    spoken,
                    from: Math.Max(0, _anchor - Options.ReacquireBackWindow),
                    to: _anchor,
                    threshold: Options.ReacquireThreshold,
                    allowBehind: true);
            }

            SearchResult best =
                backward.Accepted && (!forward.Accepted || backward.Confidence > forward.Confidence)
                    ? backward
                    : forward;

            if (best.Accepted)
            {
                return Accept(best);
            }

            _state = TrackingState.Lost;
            return Snapshot(advanced: false);
        }

        _state = TrackingState.Paused;
        return Snapshot(advanced: false);
    }

    /// <summary>
    /// Manually re-anchor so <paramref name="tokenIndex"/> becomes the current
    /// (just-read) word - e.g. the user clicked a word to resume there.
    /// </summary>
    public void SeekToToken(int tokenIndex)
    {
        if (tokenIndex < 0 || _script.TokenCount == 0)
        {
            Reset();
            return;
        }

        int clamped = Math.Min(tokenIndex, _script.TokenCount - 1);
        _anchor = clamped + 1 < _script.TokenCount
            ? _script.FirstMatchWordForToken(clamped + 1)
            : _script.MatchWordCount;
        _noMatchCount = 0;
        _state = TrackingState.Idle;
    }

    /// <summary>Return to the start of the script.</summary>
    public void Reset()
    {
        _anchor = 0;
        _noMatchCount = 0;
        _state = TrackingState.Idle;
    }

    private MatchUpdate Accept(SearchResult result)
    {
        _anchor = result.NewAnchor;
        _noMatchCount = 0;
        _state = TrackingState.Tracking;
        return new MatchUpdate(
            TrackingState.Tracking,
            CurrentTokenIndex,
            _anchor,
            result.Confidence,
            Advanced: result.Advanced);
    }

    private MatchUpdate Snapshot(bool advanced)
        => new(_state, CurrentTokenIndex, _anchor, 0.0, advanced);

    /// <summary>
    /// Slides the spoken words across the [from, to) window of the script and
    /// returns the best-aligned position. The new anchor points just past the
    /// aligned region - i.e. the next word the reader is expected to say.
    /// </summary>
    private SearchResult Search(List<string> spoken, int from, int to, double threshold, bool allowBehind = false)
    {
        double bestSimilarity = -1.0;
        int bestEnd = _anchor;

        for (int start = from; start < to; start++)
        {
            int length = Math.Min(spoken.Count, _script.MatchWordCount - start);
            if (length <= 0)
            {
                break;
            }

            List<string> reference = Slice(start, length);
            double similarity = Levenshtein.Similarity(spoken, reference);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestEnd = start + length;
            }
        }

        bool accepted = bestSimilarity >= threshold
            && (allowBehind || bestEnd >= _anchor - Options.BackTolerance);
        return new SearchResult(accepted, bestEnd, bestSimilarity, Advanced: bestEnd > _anchor);
    }

    private List<string> Slice(int start, int length)
    {
        var slice = new List<string>(length);
        for (int i = 0; i < length; i++)
        {
            slice.Add(_script.MatchWords[start + i]);
        }

        return slice;
    }

    private readonly record struct SearchResult(bool Accepted, int NewAnchor, double Confidence, bool Advanced);
}

