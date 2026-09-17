using System;
using System.Collections.Generic;
using System.Linq;
using Teleprompter.Core.Text;

namespace Teleprompter.Core.Languages;

/// <summary>Which recognizer a voice pack is for, which also decides how it downloads.</summary>
public enum VoicePackKind
{
    /// <summary>A Vosk model: one zip whose root folder is the model.</summary>
    Vosk,

    /// <summary>A sherpa-onnx streaming model: loose files saved into the model folder.</summary>
    SherpaOnnx
}

/// <summary>One file of a voice pack download, with its exact size.</summary>
public sealed record VoicePackFile(string Name, string Url, long Bytes);

/// <summary>
/// A language TalkPrompter can follow, together with the downloadable voice
/// pack (offline speech model) that recognizes it.
/// </summary>
/// <param name="Code">ISO 639-1 code, e.g. "pl".</param>
/// <param name="EnglishName">Name in English; the picker sorts by it.</param>
/// <param name="NativeName">Name in the language itself.</param>
/// <param name="Kind">The recognizer the pack is for.</param>
/// <param name="ModelFolder">Folder the pack is installed as (for zips, the root folder inside).</param>
/// <param name="Files">What to download.</param>
public sealed record VoiceLanguage(
    string Code,
    string EnglishName,
    string NativeName,
    VoicePackKind Kind,
    string ModelFolder,
    IReadOnlyList<VoicePackFile> Files)
{
    private const double BytesPerMegabyte = 1024d * 1024d;

    /// <summary>"Polish · Polski", or just "English" when both names are the same.</summary>
    public string DisplayName => string.Equals(EnglishName, NativeName, StringComparison.Ordinal)
        ? EnglishName
        : $"{EnglishName} · {NativeName}";

    public long DownloadBytes => Files.Sum(f => f.Bytes);

    public int DownloadMegabytes => (int)Math.Ceiling(DownloadBytes / BytesPerMegabyte);

    /// <summary>How script and speech text are normalized for this language.</summary>
    public TextRules TextRules => TextRules.ForLanguage(Code);

    public override string ToString() => DisplayName;
}
