using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Teleprompter.Core.Languages;

/// <summary>
/// Every language the app offers. Each pack was checked to install into
/// <see cref="VoiceLanguage.ModelFolder"/>, to load in the app's engines, and to
/// follow a native speaker reading a known script. Sizes are exact.
/// </summary>
public static class VoiceLanguageCatalog
{
    private const string VoskModels = "https://alphacephei.com/vosk/models/";

    // Community streaming zipformer for Indonesian (MIT, adult speech), pinned
    // to one revision so the files can never change under installed apps.
    private const string IndonesianModel =
        "https://huggingface.co/spacewave/sherpa-onnx-streaming-zipformer2-id/resolve/4e5a13cbe3e9cd4e3775447d86178ef51759096f/";

    public static VoiceLanguage English { get; } =
        Vosk("en", "English", "English", "vosk-model-small-en-us-0.15", 41_205_931);

    /// <summary>English first (the default), then sorted by English name.</summary>
    public static IReadOnlyList<VoiceLanguage> All { get; } = new[]
    {
        English,
        Vosk("cs", "Czech", "Čeština", "vosk-model-small-cs-0.4-rhasspy", 46_088_666),
        Vosk("nl", "Dutch", "Nederlands", "vosk-model-small-nl-0.22", 40_441_176),
        Vosk("fr", "French", "Français", "vosk-model-small-fr-0.22", 42_233_323),
        Vosk("de", "German", "Deutsch", "vosk-model-small-de-0.15", 46_499_967),
        new VoiceLanguage(
            "id", "Indonesian", "Bahasa Indonesia", VoicePackKind.SherpaOnnx,
            "sherpa-onnx-streaming-zipformer2-id",
            new[]
            {
                IndonesianFile("encoder-iter-100000-avg-15-chunk-32-left-256.int8.onnx", 70_103_186),
                IndonesianFile("decoder-iter-100000-avg-15-chunk-32-left-256.onnx", 2_093_080),
                IndonesianFile("joiner-iter-100000-avg-15-chunk-32-left-256.int8.onnx", 259_417),
                IndonesianFile("tokens.txt", 5_403)
            }),
        Vosk("it", "Italian", "Italiano", "vosk-model-small-it-0.22", 49_665_141),
        Vosk("pl", "Polish", "Polski", "vosk-model-small-pl-0.22", 52_979_372),
        Vosk("pt", "Portuguese", "Português", "vosk-model-small-pt-0.3", 32_453_112),
        Vosk("ru", "Russian", "Русский", "vosk-model-small-ru-0.22", 46_236_750),
        Vosk("es", "Spanish", "Español", "vosk-model-small-es-0.42", 39_817_833),
        Vosk("tr", "Turkish", "Türkçe", "vosk-model-small-tr-0.3", 36_855_784)
    };

    // Vosk names its models "vosk-model-[small-]<code>-…" (e.g. vosk-model-en-us-0.22).
    private static readonly Regex VoskFolderName = new(
        @"^vosk-model-(?:small-)?(?<code>[a-z]{2})(?:-|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static VoiceLanguage? Find(string? code)
        => string.IsNullOrEmpty(code)
            ? null
            : All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The language of an installed model folder: a catalog pack, or another
    /// Vosk model of a catalog language that someone installed by hand.
    /// </summary>
    public static VoiceLanguage? FromModelFolder(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        VoiceLanguage? pack = All.FirstOrDefault(l =>
            string.Equals(l.ModelFolder, folderName, StringComparison.OrdinalIgnoreCase));
        if (pack is not null)
        {
            return pack;
        }

        Match match = VoskFolderName.Match(folderName);
        return match.Success ? Find(match.Groups["code"].Value) : null;
    }

    /// <summary>
    /// The language to use: the saved choice if there is one, otherwise the
    /// first installed pack (so upgrading from a version without a language
    /// setting keeps the pack the user already has), otherwise English.
    /// </summary>
    public static VoiceLanguage ResolveActive(string? savedCode, IEnumerable<string> installedCodes)
    {
        VoiceLanguage? saved = Find(savedCode);
        if (saved is not null)
        {
            return saved;
        }

        var installed = new HashSet<string>(installedCodes, StringComparer.OrdinalIgnoreCase);
        return All.FirstOrDefault(l => installed.Contains(l.Code)) ?? English;
    }

    private static VoiceLanguage Vosk(string code, string englishName, string nativeName, string folder, long bytes)
        => new(code, englishName, nativeName, VoicePackKind.Vosk, folder,
            new[] { new VoicePackFile(folder + ".zip", VoskModels + folder + ".zip", bytes) });

    private static VoicePackFile IndonesianFile(string name, long bytes)
        => new(name, IndonesianModel + name, bytes);
}
