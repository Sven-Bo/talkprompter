using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Teleprompter.App.Services;

/// <summary>
/// Finds installed speech models of both kinds so the engine choice can be made
/// per-session (Auto / script-locked Vosk / sherpa-onnx).
///
/// The <c>VOSK_MODEL_PATH</c> environment variable, if set, overrides discovery
/// for whichever engine type the folder contains.
///
/// For Vosk, "small" models are preferred: in grammar mode (locked to the
/// script's words) the constraint does the accuracy work, so the small model's
/// faster construction and decode win — validated by real use.
/// </summary>
public static class ModelLocator
{
    private const int MaxParentLevels = 8;

    public sealed record ModelInventory(string? SherpaDir, string? VoskDir)
    {
        public bool IsEmpty => SherpaDir is null && VoskDir is null;
    }

    public static ModelInventory FindModels()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("VOSK_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return IsSherpaModel(fromEnv)
                ? new ModelInventory(fromEnv, null)
                : new ModelInventory(null, fromEnv);
        }

        string? sherpa = null;
        string? vosk = null;

        foreach (string modelsDir in CandidateModelDirectories())
        {
            if (!Directory.Exists(modelsDir))
            {
                continue;
            }

            var dirs = Directory.GetDirectories(modelsDir);

            sherpa ??= dirs.FirstOrDefault(IsSherpaModel);

            vosk ??= dirs
                .Where(IsVoskModel)
                .OrderBy(d => Path.GetFileName(d).Contains("small", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault();

            if (sherpa is not null && vosk is not null)
            {
                break;
            }
        }

        return new ModelInventory(sherpa, vosk);
    }

    private static IEnumerable<string> CandidateModelDirectories()
    {
        // Downloaded voice packs live per-user, outside the install folder, so
        // they survive auto-updates. They take priority over bundled models.
        yield return ModelDownloadService.UserModelsDirectory;

        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        for (int i = 0; i < MaxParentLevels && dir is not null; i++, dir = dir.Parent)
        {
            yield return Path.Combine(dir.FullName, "models");
        }
    }

    private static bool IsSherpaModel(string dir)
        => File.Exists(Path.Combine(dir, "tokens.txt"))
           && Directory.GetFiles(dir, "encoder*.onnx").Length > 0;

    private static bool IsVoskModel(string dir)
        => Directory.Exists(Path.Combine(dir, "am"))
           || Directory.Exists(Path.Combine(dir, "conf"));
}
