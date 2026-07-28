using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Teleprompter.Audio;

/// <summary>Enumerates available microphones.</summary>
public static class AudioDevices
{
    public static IReadOnlyList<AudioInputDevice> List()
    {
        var devices = new List<AudioInputDevice>
        {
            new(-1, "Default microphone")
        };

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    devices.Add(new AudioInputDevice(
                        MatchWaveInIndex(device.FriendlyName),
                        device.FriendlyName,
                        device.ID));
                }
            }
        }
        catch (Exception)
        {
            // WASAPI enumeration unavailable — fall back to WaveIn's device list.
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                string name = WaveInEvent.GetCapabilities(i).ProductName;
                devices.Add(new AudioInputDevice(i, string.IsNullOrWhiteSpace(name) ? $"Microphone {i}" : name));
            }
        }

        return devices;
    }

    /// <summary>
    /// Best-effort map from a WASAPI friendly name to a WaveIn device number for
    /// the fallback path. WaveIn product names are truncated to 31 characters,
    /// so match on the truncated prefix; -1 (default device) when nothing fits.
    /// </summary>
    private static int MatchWaveInIndex(string friendlyName)
    {
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            string product = WaveInEvent.GetCapabilities(i).ProductName;
            if (!string.IsNullOrWhiteSpace(product)
                && friendlyName.StartsWith(product, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
