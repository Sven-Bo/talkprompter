using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using SherpaOnnx;
using Teleprompter.Core.Speech;

namespace Teleprompter.Speech;

/// <summary>
/// Offline streaming recognition using a sherpa-onnx zipformer transducer.
/// Modern streaming architecture with strong open-vocabulary accuracy, fully
/// on-device.
///
/// Audio arrives on the microphone callback thread and is only queued there;
/// all neural decoding runs on a dedicated worker thread. Decoding inline on
/// the audio callback would back audio up whenever a chunk decodes slower than
/// real time, accumulating latency — the queue decouples the two, and if the
/// machine truly cannot keep up, whole chunks are dropped (a bounded glitch)
/// instead of lag growing without bound.
/// </summary>
public sealed class SherpaOnnxSpeechEngine : ISpeechEngine
{
    // ~4 seconds of audio at 30 ms per chunk before chunks get dropped.
    private const int QueueCapacity = 128;

    private readonly OnlineRecognizer _recognizer;
    private readonly OnlineStream _stream;
    private readonly BlockingCollection<float[]> _queue = new(QueueCapacity);
    private readonly Thread _worker;
    private readonly object _lock = new();
    private string _lastPartial = string.Empty;
    private bool _disposed;

    public int SampleRate { get; } = 16000;

    public event EventHandler<SpeechHypothesis>? HypothesisReceived;

    public SherpaOnnxSpeechEngine(string modelDir)
    {
        ModelFiles files = DiscoverModel(modelDir);

        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = files.Encoder;
        config.ModelConfig.Transducer.Decoder = files.Decoder;
        config.ModelConfig.Transducer.Joiner = files.Joiner;
        config.ModelConfig.Tokens = files.Tokens;
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = 2;
        config.ModelConfig.Debug = 0;
        config.DecodingMethod = "greedy_search";
        config.EnableEndpoint = 1;
        // Endpointing: commit after 1.2s of trailing silence mid-utterance.
        config.Rule1MinTrailingSilence = 2.4f;
        config.Rule2MinTrailingSilence = 1.2f;
        config.Rule3MinUtteranceLength = 30.0f;

        _recognizer = new OnlineRecognizer(config);
        _stream = _recognizer.CreateStream();

        _worker = new Thread(DecodeLoop)
        {
            IsBackground = true,
            Name = "sherpa-decode"
        };
        _worker.Start();
    }

    public void Start()
    {
        // Recognizer, stream, and worker are ready on construction.
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_lastPartial))
            {
                HypothesisReceived?.Invoke(this, new SpeechHypothesis(_lastPartial, IsFinal: true));
                _lastPartial = string.Empty;
            }
        }
    }

    public void AcceptWaveform(byte[] pcm16, int count)
    {
        if (_disposed || _queue.IsAddingCompleted)
        {
            return;
        }

        float[] samples = ToFloatSamples(pcm16, count);
        if (samples.Length == 0)
        {
            return;
        }

        // Never block the audio callback: if the decoder is hopelessly behind,
        // drop this chunk rather than stall capture or grow latency forever.
        _queue.TryAdd(samples);
    }

    public void SetHotwords(System.Collections.Generic.IReadOnlyList<string> words)
    {
        // Greedy-search decoding does not support hotword biasing; the matcher
        // absorbs recognition errors instead. (Biasing needs modified_beam_search
        // plus a tokenized hotwords file - a future upgrade.)
    }

    private void DecodeLoop()
    {
        try
        {
            foreach (float[] samples in _queue.GetConsumingEnumerable())
            {
                lock (_lock)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    DecodeChunk(samples);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Shutdown race; nothing to do.
        }
        catch (InvalidOperationException)
        {
            // Queue completed during enumeration; normal shutdown.
        }
    }

    private void DecodeChunk(float[] samples)
    {
        _stream.AcceptWaveform(SampleRate, samples);
        while (_recognizer.IsReady(_stream))
        {
            _recognizer.Decode(_stream);
        }

        string text = _recognizer.GetResult(_stream).Text?.Trim() ?? string.Empty;
        bool endpoint = _recognizer.IsEndpoint(_stream);

        if (endpoint)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                HypothesisReceived?.Invoke(this, new SpeechHypothesis(text, IsFinal: true));
            }

            _recognizer.Reset(_stream);
            _lastPartial = string.Empty;
        }
        else if (text.Length > 0 && !string.Equals(text, _lastPartial, StringComparison.Ordinal))
        {
            _lastPartial = text;
            HypothesisReceived?.Invoke(this, new SpeechHypothesis(text, IsFinal: false));
        }
    }

    private static float[] ToFloatSamples(byte[] pcm16, int count)
    {
        int sampleCount = count / 2;
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short s = (short)(pcm16[2 * i] | (pcm16[2 * i + 1] << 8));
            samples[i] = s / 32768f;
        }

        return samples;
    }

    private sealed record ModelFiles(string Encoder, string Decoder, string Joiner, string Tokens);

    /// <summary>
    /// Finds encoder/decoder/joiner/tokens in a model folder regardless of the
    /// exact checkpoint naming, preferring int8-quantized files (smaller and
    /// faster on CPU at negligible accuracy cost).
    /// </summary>
    private static ModelFiles DiscoverModel(string modelDir)
    {
        if (!Directory.Exists(modelDir))
        {
            throw new DirectoryNotFoundException($"sherpa-onnx model folder not found: {modelDir}");
        }

        string tokens = Path.Combine(modelDir, "tokens.txt");
        if (!File.Exists(tokens))
        {
            throw new FileNotFoundException("tokens.txt missing from sherpa-onnx model folder.", tokens);
        }

        return new ModelFiles(
            PickOnnx(modelDir, "encoder"),
            PickOnnx(modelDir, "decoder"),
            PickOnnx(modelDir, "joiner"),
            tokens);
    }

    private static string PickOnnx(string dir, string role)
    {
        var candidates = Directory.GetFiles(dir, $"{role}*.onnx")
            .OrderBy(f => f.Contains("int8", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();

        return candidates.FirstOrDefault()
            ?? throw new FileNotFoundException($"No {role}*.onnx found in sherpa-onnx model folder '{dir}'.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _queue.CompleteAdding();
        _worker.Join(1500);

        lock (_lock)
        {
            _disposed = true;
            _stream.Dispose();
            _recognizer.Dispose();
        }

        _queue.Dispose();
    }
}
