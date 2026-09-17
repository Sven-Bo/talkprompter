using System.Globalization;

namespace Teleprompter.Core.Text;

/// <summary>
/// Language-specific rules for turning text into match words. The same rules
/// must be applied to the script and to what the recognizer hears, so both
/// sides normalize identically.
/// </summary>
/// <param name="Casing">Culture used to lower-case words (Turkish has its own dotted and dotless i).</param>
/// <param name="ExpandNumbers">Spell digits out as English number words ("25" → "twenty five").</param>
public sealed record TextRules(CultureInfo Casing, bool ExpandNumbers)
{
    /// <summary>The historical default: invariant casing plus English number words.</summary>
    public static TextRules English { get; } = new(CultureInfo.InvariantCulture, ExpandNumbers: true);

    private static readonly TextRules Neutral = new(CultureInfo.InvariantCulture, ExpandNumbers: false);
    private static readonly TextRules Turkish = new(CultureInfo.GetCultureInfo("tr-TR"), ExpandNumbers: false);

    /// <summary>
    /// Rules for a language code. Number words exist only for English, so other
    /// languages keep a written number as one word: the reader's spoken number
    /// goes unmatched and the matcher steps over a single word instead of
    /// several English ones that could never be heard.
    /// </summary>
    public static TextRules ForLanguage(string? code) => code?.ToLowerInvariant() switch
    {
        null or "en" => English,
        "tr" => Turkish,
        _ => Neutral
    };
}
