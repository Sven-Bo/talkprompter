using System;
using System.Collections.Generic;
using System.Linq;

namespace Teleprompter.Speech;

/// <summary>
/// The speech models found on disk, looked up by language code. Build it with
/// <see cref="ModelScanner"/>.
/// </summary>
public sealed class InstalledModels
{
    private static readonly IReadOnlyDictionary<string, string> None =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyDictionary<string, string> _voskByLanguage;
    private readonly IReadOnlyDictionary<string, string> _sherpaByLanguage;
    private readonly string? _otherVoskDir;
    private readonly string? _overrideVoskDir;
    private readonly string? _overrideSherpaDir;

    internal InstalledModels(
        IReadOnlyDictionary<string, string> voskByLanguage,
        IReadOnlyDictionary<string, string> sherpaByLanguage,
        string? otherVoskDir = null,
        string? overrideVoskDir = null,
        string? overrideSherpaDir = null)
    {
        _voskByLanguage = voskByLanguage;
        _sherpaByLanguage = sherpaByLanguage;
        _otherVoskDir = otherVoskDir;
        _overrideVoskDir = overrideVoskDir;
        _overrideSherpaDir = overrideSherpaDir;
    }

    public static InstalledModels Empty { get; } = new(None, None);

    public bool IsEmpty => _voskByLanguage.Count == 0 && _sherpaByLanguage.Count == 0
                           && _otherVoskDir is null && _overrideVoskDir is null && _overrideSherpaDir is null;

    /// <summary>Languages with a voice pack of their own.</summary>
    public IReadOnlyCollection<string> LanguageCodes
        => _voskByLanguage.Keys.Union(_sherpaByLanguage.Keys, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// Whether the language has its own pack (what the picker shows as
    /// installed). An explicit override counts for every language.
    /// </summary>
    public bool HasPackFor(string languageCode)
        => _overrideVoskDir is not null || _overrideSherpaDir is not null
           || _voskByLanguage.ContainsKey(languageCode) || _sherpaByLanguage.ContainsKey(languageCode);

    /// <summary>
    /// The Vosk model to run for a language: an explicit override, the
    /// language's own pack, or else a model of a language the catalog does not
    /// know (older versions ran any model they found).
    /// </summary>
    public string? VoskDirFor(string languageCode)
        => _overrideVoskDir ?? Lookup(_voskByLanguage, languageCode) ?? _otherVoskDir;

    /// <summary>The sherpa-onnx model for a language (an explicit override applies to all).</summary>
    public string? SherpaDirFor(string languageCode) => _overrideSherpaDir ?? Lookup(_sherpaByLanguage, languageCode);

    /// <summary>Whether a session in this language can run a real recognizer.</summary>
    public bool CanRecognize(string languageCode)
        => VoskDirFor(languageCode) is not null || SherpaDirFor(languageCode) is not null;

    private static string? Lookup(IReadOnlyDictionary<string, string> models, string languageCode)
        => models.TryGetValue(languageCode, out string? dir) ? dir : null;
}
