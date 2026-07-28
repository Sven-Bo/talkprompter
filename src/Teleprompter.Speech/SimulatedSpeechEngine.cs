using System;
using System.Collections.Generic;
using System.Timers;
using Teleprompter.Core.Speech;

namespace Teleprompter.Speech;

/// <summary>
/// A speech engine that reads the script to itself on a timer instead of
/// listening to a microphone. It lets the whole pipeline — matcher, scrolling,
/// UI — be exercised without a model or a voice, and doubles as an offline demo
/// mode. Audio input is ignored.
/// </summary>
public sealed class SimulatedSpeechEngine : ISpeechEngine
{
    private const int WordsPerUtterance = 10;

    private readonly string[] _words;
    private readonly System.Timers.Timer _timer;
    private int _position;
    private int _utteranceStart;

    public int SampleRate { get; } = 16000;

    public event EventHandler<SpeechHypothesis>? HypothesisReceived;

    public SimulatedSpeechEngine(string scriptText, double wordsPerMinute = 150)
    {
        _words = (scriptText ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        double intervalMs = 60000.0 / Math.Max(30.0, wordsPerMinute);
        _timer = new System.Timers.Timer(intervalMs) { AutoReset = true };
        _timer.Elapsed += OnTick;
    }

    public void Start()
    {
        _position = 0;
        _utteranceStart = 0;
        if (_words.Length > 0)
        {
            _timer.Start();
        }
    }

    public void Stop() => _timer.Stop();

    public void AcceptWaveform(byte[] pcm16, int count)
    {
        // Simulation is self-driven; live audio is intentionally ignored.
    }

    public void SetHotwords(IReadOnlyList<string> words)
    {
        // No biasing needed for the simulator.
    }

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        if (_position >= _words.Length)
        {
            _timer.Stop();
            return;
        }

        _position++;
        string partial = string.Join(' ', _words[_utteranceStart.._position]);
        bool boundary = _position - _utteranceStart >= WordsPerUtterance;

        HypothesisReceived?.Invoke(this, new SpeechHypothesis(partial, boundary));

        if (boundary)
        {
            _utteranceStart = _position;
        }
    }

    public void Dispose()
    {
        _timer.Elapsed -= OnTick;
        _timer.Dispose();
    }
}
