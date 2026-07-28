using System;
using System.Collections.Generic;

namespace Teleprompter.Core.Speech;

/// <summary>
/// A streaming, offline speech recognizer. Implementations consume 16-bit mono
/// PCM at <see cref="SampleRate"/> and raise <see cref="HypothesisReceived"/>
/// with growing partial results and committed finals.
///
/// The contract is deliberately small so engines (Vosk, sherpa-onnx, a
/// simulator for testing) are interchangeable behind it.
/// </summary>
public interface ISpeechEngine : IDisposable
{
    /// <summary>Required input sample rate in Hz (typically 16000).</summary>
    int SampleRate { get; }

    /// <summary>Raised for each partial and final recognition result.</summary>
    event EventHandler<SpeechHypothesis>? HypothesisReceived;

    /// <summary>Begin a recognition session.</summary>
    void Start();

    /// <summary>End the session and flush any pending result.</summary>
    void Stop();

    /// <summary>
    /// Feed a chunk of 16-bit mono PCM. <paramref name="count"/> is the number
    /// of valid bytes in <paramref name="pcm16"/>.
    /// </summary>
    void AcceptWaveform(byte[] pcm16, int count);

    /// <summary>
    /// Bias recognition toward the given words (the script text just ahead of
    /// the reader). Engines that do not support biasing may ignore this.
    /// </summary>
    void SetHotwords(IReadOnlyList<string> words);
}
