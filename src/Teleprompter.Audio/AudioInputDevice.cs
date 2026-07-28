namespace Teleprompter.Audio;

/// <summary>
/// A selectable microphone. <see cref="WasapiId"/> addresses the device for the
/// low-latency WASAPI path (null = system default); <see cref="Index"/> is the
/// legacy WaveIn device number used by the fallback path (-1 = default).
/// </summary>
public sealed record AudioInputDevice(int Index, string Name, string? WasapiId = null)
{
    public override string ToString() => Name;
}
