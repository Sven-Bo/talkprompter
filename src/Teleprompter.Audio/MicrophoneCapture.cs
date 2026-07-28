using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Teleprompter.Audio;

/// <summary>
/// Microphone capture producing 16 kHz / 16-bit / mono PCM for the speech
/// engines.
///
/// Primary path: event-driven <b>WASAPI</b> shared-mode capture with a 20 ms
/// buffer — roughly 40–60 ms less end-to-end latency than the legacy WaveIn
/// API. The device's native mix format (typically 44.1/48 kHz float stereo) is
/// mixed down to mono and resampled to 16 kHz in managed code.
///
/// Fallback path: WaveIn requesting 16 kHz mono directly, used when WASAPI
/// cannot start (exotic drivers, remote sessions).
/// </summary>
public sealed class MicrophoneCapture : IAudioCapture
{
    private const int TargetRate = 16000;

    public int SampleRate { get; } = TargetRate;

    public event Action<byte[], int>? DataAvailable;

    /// <summary>
    /// Fired when capture ends without <see cref="Stop"/> being called — the
    /// device was unplugged, went to sleep, or the driver failed. The exception
    /// is null when the device simply vanished.
    /// </summary>
    public event Action<Exception?>? CaptureStopped;

    private bool _stopRequested;

    private WasapiCapture? _wasapi;
    private BufferedWaveProvider? _buffered;
    private ISampleProvider? _pipeline;
    private float[] _floatBuffer = new float[3200];
    private byte[] _byteBuffer = new byte[6400];

    private WaveInEvent? _waveIn;

    public void Start(AudioInputDevice? device)
    {
        Stop();
        _stopRequested = false;

        try
        {
            StartWasapi(device);
        }
        catch (Exception)
        {
            _wasapi?.Dispose();
            _wasapi = null;
            StartWaveIn(device?.Index ?? -1);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (!_stopRequested)
        {
            CaptureStopped?.Invoke(e.Exception);
        }
    }

    private void StartWasapi(AudioInputDevice? device)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice mmDevice = device?.WasapiId is null
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
            : enumerator.GetDevice(device.WasapiId);

        _wasapi = new WasapiCapture(mmDevice, useEventSync: true, audioBufferMillisecondsLength: 20);

        // Push captured bytes into a buffer and pull them through a
        // mono-mixdown + resample pipeline sized for our 16 kHz target.
        _buffered = new BufferedWaveProvider(_wasapi.WaveFormat)
        {
            ReadFully = false,
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2)
        };

        ISampleProvider samples = _buffered.ToSampleProvider();
        if (_wasapi.WaveFormat.Channels > 1)
        {
            samples = new MonoMixdownSampleProvider(samples);
        }

        _pipeline = new WdlResamplingSampleProvider(samples, TargetRate);

        _wasapi.DataAvailable += OnWasapiData;
        _wasapi.RecordingStopped += OnRecordingStopped;
        _wasapi.StartRecording();
    }

    private void StartWaveIn(int deviceIndex)
    {
        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceIndex,
            WaveFormat = new WaveFormat(TargetRate, 16, 1),
            BufferMilliseconds = 30
        };
        _waveIn.DataAvailable += OnWaveInData;
        _waveIn.RecordingStopped += OnRecordingStopped;
        _waveIn.StartRecording();
    }

    private void OnWasapiData(object? sender, WaveInEventArgs e)
    {
        BufferedWaveProvider? buffered = _buffered;
        ISampleProvider? pipeline = _pipeline;
        if (buffered is null || pipeline is null || e.BytesRecorded == 0)
        {
            return;
        }

        buffered.AddSamples(e.Buffer, 0, e.BytesRecorded);

        // Drain everything the resampler can produce from what is buffered.
        while (true)
        {
            int got = pipeline.Read(_floatBuffer, 0, _floatBuffer.Length);
            if (got <= 0)
            {
                return;
            }

            EmitAsPcm16(got);

            if (got < _floatBuffer.Length)
            {
                return;
            }
        }
    }

    private void EmitAsPcm16(int sampleCount)
    {
        int bytesNeeded = sampleCount * 2;
        if (_byteBuffer.Length < bytesNeeded)
        {
            _byteBuffer = new byte[bytesNeeded];
        }

        for (int i = 0; i < sampleCount; i++)
        {
            float clamped = Math.Clamp(_floatBuffer[i], -1f, 1f);
            short value = (short)(clamped * short.MaxValue);
            _byteBuffer[2 * i] = (byte)(value & 0xFF);
            _byteBuffer[2 * i + 1] = (byte)((value >> 8) & 0xFF);
        }

        DataAvailable?.Invoke(_byteBuffer, bytesNeeded);
    }

    private void OnWaveInData(object? sender, WaveInEventArgs e)
        => DataAvailable?.Invoke(e.Buffer, e.BytesRecorded);

    public void Stop()
    {
        _stopRequested = true;

        if (_wasapi is not null)
        {
            _wasapi.DataAvailable -= OnWasapiData;
            _wasapi.RecordingStopped -= OnRecordingStopped;
            try
            {
                _wasapi.StopRecording();
            }
            catch (Exception)
            {
                // Device may already be gone; nothing actionable.
            }

            _wasapi.Dispose();
            _wasapi = null;
            _buffered = null;
            _pipeline = null;
        }

        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnWaveInData;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            try
            {
                _waveIn.StopRecording();
            }
            catch (Exception)
            {
                // Device may already be gone; nothing actionable.
            }

            _waveIn.Dispose();
            _waveIn = null;
        }
    }

    public void Dispose() => Stop();

    /// <summary>Averages any number of channels down to mono.</summary>
    private sealed class MonoMixdownSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _sourceBuffer = Array.Empty<float>();

        public MonoMixdownSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            WaveFormat = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int needed = count * _channels;
            if (_sourceBuffer.Length < needed)
            {
                _sourceBuffer = new float[needed];
            }

            int read = _source.Read(_sourceBuffer, 0, needed);
            int frames = read / _channels;
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0f;
                for (int ch = 0; ch < _channels; ch++)
                {
                    sum += _sourceBuffer[frame * _channels + ch];
                }

                buffer[offset + frame] = sum / _channels;
            }

            return frames;
        }
    }
}
