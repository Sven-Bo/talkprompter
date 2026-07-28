namespace Teleprompter.Core.Speech;

/// <summary>
/// A recognition result from a speech engine.
/// </summary>
/// <param name="Text">The recognized words so far.</param>
/// <param name="IsFinal">
/// True when the engine has committed this utterance (silence detected); false
/// for an in-progress partial that will keep growing.
/// </param>
public sealed record SpeechHypothesis(string Text, bool IsFinal);
