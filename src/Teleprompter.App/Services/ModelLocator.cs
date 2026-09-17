using System;
using System.Collections.Generic;
using System.IO;
using Teleprompter.Speech;

namespace Teleprompter.App.Services;

/// <summary>
/// Finds the installed voice packs, per language, so the engine for a session
/// matches the language the user reads in.
///
/// The <c>VOSK_MODEL_PATH</c> environment variable, if set, overrides discovery
/// with one model folder for every language (either engine type).
/// </summary>
public static class ModelLocator
{
    private const int MaxParentLevels = 8;

    public static InstalledModels FindModels()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("VOSK_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return ModelScanner.FromOverride(fromEnv);
        }

        return ModelScanner.Scan(CandidateModelDirectories());
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
}
