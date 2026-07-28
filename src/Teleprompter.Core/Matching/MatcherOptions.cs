namespace Teleprompter.Core.Matching;

/// <summary>
/// Tunables for <see cref="ScriptMatcher"/>. Defaults favor staying with the
/// reader through recognition errors (small models, fast or accented speech)
/// rather than pausing at the first mismatch.
/// </summary>
public sealed record MatcherOptions
{
    /// <summary>How many of the most recent spoken words to align each update.</summary>
    public int TailWords { get; init; } = 7;

    /// <summary>Extra script words to search ahead of the current anchor.</summary>
    public int ForwardWindow { get; init; } = 14;

    /// <summary>How far the search may look behind the anchor to correct small slips.</summary>
    public int BackTolerance { get; init; } = 2;

    /// <summary>Maximum words the anchor may advance in a single update (guards against far-away repeats).</summary>
    public int MaxJump { get; init; } = 18;

    /// <summary>Minimum similarity (0–1) required to accept a match and scroll.</summary>
    public double AcceptThreshold { get; init; } = 0.42;

    /// <summary>Minimum number of spoken words before a match is trusted.</summary>
    public int MinMatchedWords { get; init; } = 2;

    /// <summary>Consecutive non-matching hypotheses before entering re-acquisition.</summary>
    public int LostThreshold { get; init; } = 8;

    /// <summary>Stricter similarity required to jump during re-acquisition.</summary>
    public double ReacquireThreshold { get; init; } = 0.7;

    /// <summary>How far ahead re-acquisition scans when the reader has skipped text.</summary>
    public int ReacquireWindow { get; init; } = 400;

    /// <summary>
    /// How far behind the anchor re-acquisition scans — a flubbed take usually
    /// means re-reading an earlier sentence, and the prompter should follow.
    /// </summary>
    public int ReacquireBackWindow { get; init; } = 80;

    /// <summary>Minimum spoken words before a backward re-acquisition is trusted.</summary>
    public int ReacquireBackMinWords { get; init; } = 4;
}
