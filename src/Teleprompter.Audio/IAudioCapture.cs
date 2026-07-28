using System;

namespace Teleprompter.Audio;

/// <summary>
/// Captures microphone audio as 16-bit mono PCM at <see cref="SampleRate"/> and
/// pushes raw buffers to subscribers. Buffers are owned by the capture and
/// valid only for the duration of the callback, so consumers must process them
/// synchronously (which the speech engines do — they copy/queue internally).
/// </summary>
public interface IAudioCapture : IDisposable
{
    int SampleRate { get; }

    /// <summary>Fired on the audio thread with (buffer, valid byte count).</summary>
    event Action<byte[], int>? DataAvailable;

    /// <summary>Start capturing from the given device (null for the system default).</summary>
    void Start(AudioInputDevice? device);

    void Stop();
}
