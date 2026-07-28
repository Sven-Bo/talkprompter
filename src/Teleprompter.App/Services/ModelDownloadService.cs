using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Teleprompter.App.Services;

/// <summary>A downloadable speech model, presented as a "voice pack".</summary>
public sealed record VoicePack(string Key, string DisplayName, string ZipUrl, string FolderName, int ApproxMb)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Downloads Vosk voice packs into the per-user models folder
/// (%LOCALAPPDATA%\TalkPrompter\models). That folder is outside the
/// installation directory, so downloaded packs survive every auto-update —
/// which in turn lets the installer ship without any bundled model.
/// </summary>
public static class ModelDownloadService
{
    public static string UserModelsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TalkPrompter",
        "models");

    public static IReadOnlyList<VoicePack> Packs { get; } = new[]
    {
        new VoicePack(
            "en", "English (recommended)",
            "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip",
            "vosk-model-small-en-us-0.15", 40),
        new VoicePack(
            "de", "German / Deutsch",
            "https://alphacephei.com/vosk/models/vosk-model-small-de-0.15.zip",
            "vosk-model-small-de-0.15", 45)
    };

    /// <summary>
    /// Downloads and unpacks a voice pack, reporting progress as
    /// (percent, human-readable status). Returns the model folder path.
    /// </summary>
    public static async Task<string> DownloadAsync(
        VoicePack pack,
        IProgress<(double Percent, string Status)> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(UserModelsDirectory);
        string zipPath = Path.Combine(UserModelsDirectory, pack.FolderName + ".download");

        try
        {
            using var http = new HttpClient();
            http.Timeout = Timeout.InfiniteTimeSpan; // per-read cancellation instead

            using HttpResponseMessage response = await http.GetAsync(
                pack.ZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? pack.ApproxMb * 1024L * 1024L;
            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream target = File.Create(zipPath))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    done += read;
                    progress.Report((
                        Math.Min(99.0, done * 100.0 / totalBytes),
                        $"Downloading… {done / 1048576} of {totalBytes / 1048576} MB"));
                }
            }

            progress.Report((99.5, "Unpacking…"));
            string modelDir = Path.Combine(UserModelsDirectory, pack.FolderName);
            if (Directory.Exists(modelDir))
            {
                Directory.Delete(modelDir, recursive: true);
            }

            // The official Vosk zips contain the model folder at their root.
            ZipFile.ExtractToDirectory(zipPath, UserModelsDirectory);

            if (!Directory.Exists(modelDir))
            {
                throw new InvalidOperationException("The downloaded pack did not contain the expected model folder.");
            }

            progress.Report((100, "Ready."));
            return modelDir;
        }
        finally
        {
            try
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
            catch (IOException)
            {
                // Leftover temp file is harmless.
            }
        }
    }
}
