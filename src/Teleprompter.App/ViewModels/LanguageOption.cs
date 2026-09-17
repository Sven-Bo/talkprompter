using Teleprompter.Core.Languages;

namespace Teleprompter.App.ViewModels;

/// <summary>One row of the language picker: a language plus its install state.</summary>
public sealed record LanguageOption(VoiceLanguage Language, bool IsInstalled, bool IsActive)
{
    public string Label => !IsInstalled
        ? $"{Language.DisplayName}  —  {Language.DownloadMegabytes} MB"
        : IsActive
            ? $"{Language.DisplayName}  —  in use"
            : $"{Language.DisplayName}  —  installed";

    public override string ToString() => Label;
}
