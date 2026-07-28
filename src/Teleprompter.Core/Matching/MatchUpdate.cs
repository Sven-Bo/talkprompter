namespace Teleprompter.Core.Matching;

/// <summary>
/// The result of feeding one speech hypothesis to the matcher.
/// </summary>
/// <param name="State">What the reader appears to be doing.</param>
/// <param name="TokenIndex">Index of the current (most recently read) display token, or -1 if none yet.</param>
/// <param name="MatchWordIndex">Internal anchor into the match-word sequence.</param>
/// <param name="Confidence">Similarity (0–1) of the accepted match; 0 when holding.</param>
/// <param name="Advanced">True when the anchor moved forward on this update.</param>
public sealed record MatchUpdate(
    TrackingState State,
    int TokenIndex,
    int MatchWordIndex,
    double Confidence,
    bool Advanced);
