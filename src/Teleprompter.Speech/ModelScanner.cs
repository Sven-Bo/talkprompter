using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Teleprompter.Core.Languages;

namespace Teleprompter.Speech;

/// <summary>
/// Finds installed speech models and works out which language each one is for,
/// from its folder name.
/// </summary>
public static class ModelScanner
{
    private const string EnglishCode = "en";

    /// <summary>
    /// Scans model folders in priority order: for each language the first root
    /// that has a model wins. Within a root the catalog pack is preferred, then
    /// "small" Vosk models, whose runtime word list powers the script lock.
    /// Hidden folders (".unpack-…" staging) are never models.
    /// </summary>
    public static InstalledModels Scan(IEnumerable<string> modelRoots)
    {
        var vosk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sherpa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? otherVosk = null;

        foreach (string root in modelRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            string[] dirs = Directory.GetDirectories(root)
                .Where(dir => !Path.GetFileName(dir).StartsWith('.'))
                .OrderBy(dir => dir, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var voskModels = dirs
                .Where(IsVoskModel)
                .Select(dir => (Dir: dir, Language: VoiceLanguageCatalog.FromModelFolder(Path.GetFileName(dir))))
                .ToArray();

            foreach (var models in voskModels.Where(m => m.Language is not null).GroupBy(m => m.Language!.Code))
            {
                vosk.TryAdd(models.Key, models.OrderBy(m => Rank(m.Dir, m.Language)).First().Dir);
            }

            otherVosk ??= voskModels
                .Where(m => m.Language is null)
                .OrderBy(m => Rank(m.Dir, language: null))
                .Select(m => m.Dir)
                .FirstOrDefault();

            foreach (string dir in dirs.Where(IsSherpaModel))
            {
                sherpa.TryAdd(SherpaLanguage(dir), dir);
            }
        }

        return new InstalledModels(vosk, sherpa, otherVosk);
    }

    /// <summary>An explicitly chosen model folder (VOSK_MODEL_PATH) used for every language.</summary>
    public static InstalledModels FromOverride(string dir)
    {
        var none = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return IsSherpaModel(dir)
            ? new InstalledModels(none, none, overrideSherpaDir: dir)
            : new InstalledModels(none, none, overrideVoskDir: dir);
    }

    /// <summary>
    /// A complete Vosk model in either layout — current (am/, conf/, graph/)
    /// or the older flat one — with its acoustic model, features config and a
    /// decoding graph (runtime HCLr + Gr, or a static HCLG). Anything less is
    /// a half-extracted folder that would fail inside the native recognizer.
    /// </summary>
    public static bool IsVoskModel(string dir)
        => HasVoskFiles(Path.Combine(dir, "am"), Path.Combine(dir, "conf"), Path.Combine(dir, "graph"))
           || HasVoskFiles(dir, dir, dir);

    public static bool IsSherpaModel(string dir)
        => File.Exists(Path.Combine(dir, "tokens.txt"))
           && Directory.EnumerateFiles(dir, "encoder*.onnx").Any();

    private static bool HasVoskFiles(string modelDir, string confDir, string graphDir)
        => File.Exists(Path.Combine(modelDir, "final.mdl"))
           && File.Exists(Path.Combine(confDir, "mfcc.conf"))
           && (File.Exists(Path.Combine(graphDir, "HCLG.fst"))
               || (File.Exists(Path.Combine(graphDir, "HCLr.fst")) && File.Exists(Path.Combine(graphDir, "Gr.fst"))));

    // Catalog packs name their language; any other sherpa-onnx model is one of
    // the English models the app has always run.
    private static string SherpaLanguage(string dir)
    {
        VoiceLanguage? language = VoiceLanguageCatalog.FromModelFolder(Path.GetFileName(dir));
        return language is { Kind: VoicePackKind.SherpaOnnx } ? language.Code : EnglishCode;
    }

    private static int Rank(string dir, VoiceLanguage? language)
    {
        string name = Path.GetFileName(dir);
        if (language is not null && string.Equals(name, language.ModelFolder, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return name.Contains("small", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }
}
