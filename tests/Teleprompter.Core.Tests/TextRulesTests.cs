using FluentAssertions;
using Teleprompter.Core.Matching;
using Teleprompter.Core.Text;

namespace Teleprompter.Core.Tests;

public sealed class TextRulesTests
{
    private static readonly TextRules Polish = TextRules.ForLanguage("pl");
    private static readonly TextRules Turkish = TextRules.ForLanguage("tr");

    [Theory]
    [InlineData("en", true)]
    [InlineData("de", false)]
    [InlineData("pl", false)]
    [InlineData("id", false)]
    public void ForLanguage_ExpandsNumbersOnlyForEnglish(string code, bool expected)
    {
        TextRules.ForLanguage(code).ExpandNumbers.Should().Be(expected);
    }

    [Fact]
    public void ForLanguage_UnknownCode_BehavesLikeOtherNonEnglishLanguages()
    {
        TextRules.ForLanguage("xx").ExpandNumbers.Should().BeFalse();
    }

    [Theory]
    [InlineData("ZAŻÓŁĆ", "zażółć")]
    [InlineData("Gęślą,", "gęślą")]
    [InlineData("jaźń!", "jaźń")]
    public void NormalizeWord_KeepsPolishLetters(string raw, string expected)
    {
        TextNormalizer.NormalizeWord(raw, Polish).Should().Be(expected);
    }

    [Theory]
    [InlineData("IŞIK", "ışık")]
    [InlineData("İstanbul", "istanbul")]
    public void NormalizeWord_UsesTurkishCasing(string raw, string expected)
    {
        TextNormalizer.NormalizeWord(raw, Turkish).Should().Be(expected);
    }

    [Fact]
    public void Tokenize_KeepsDecomposedAccentsInTheWord_AndComposesThem()
    {
        // "café" typed as e + combining acute (common in text from macOS and the web).
        const string text = "Un café noir";

        var tokens = TextNormalizer.Tokenize(text, TextRules.ForLanguage("fr"));

        tokens.Should().HaveCount(3);
        tokens[1].Normalized.Should().Be("café");
        text.Substring(tokens[1].Start, tokens[1].Length).Should().Be("café");
    }

    [Fact]
    public void ToMatchWords_ComposesDecomposedLetters()
    {
        const string decomposed = "Zażółć"; // "Zażółć" with combining marks

        TextNormalizer.ToMatchWords(decomposed, Polish).Should().Equal("zażółć");
    }

    [Fact]
    public void ToMatchWords_KeepsDigits_WhenTheLanguageHasNoNumberWords()
    {
        TextNormalizer.ToMatchWords("Mam 25 lat", Polish)
            .Should().Equal("mam", "25", "lat");
    }

    [Fact]
    public void ToMatchWords_DefaultsToEnglishRules()
    {
        TextNormalizer.ToMatchWords("I have 25 cats")
            .Should().Equal(TextNormalizer.ToMatchWords("I have 25 cats", TextRules.English));
    }

    [Fact]
    public void ScriptModel_KeepsANumberAsOneMatchWord_ForNonEnglishScripts()
    {
        var model = ScriptModel.Build("Mamy 25 nowych klientów", Polish);

        model.MatchWords.Should().Equal("mamy", "25", "nowych", "klientów");
        model.TextRules.Should().Be(Polish);
    }

    [Fact]
    public void ScriptModel_DefaultsToEnglishRules()
    {
        ScriptModel.Build("we have 25 cats").TextRules.Should().Be(TextRules.English);
    }

    [Fact]
    public void Matcher_FollowsANonEnglishReader_PastAWrittenNumber()
    {
        // The recognizer never emits "25" and has no English number words for
        // Polish, so the reader's spoken number simply goes unmatched — the
        // prompter must still keep up with the words around it.
        var model = ScriptModel.Build("W tym roku mamy 25 nowych klientów i dwa nowe biura", Polish);
        var matcher = new ScriptMatcher(model);

        string spoken = string.Empty;
        foreach (string word in "w tym roku mamy dwadzieścia pięć nowych klientów i dwa nowe".Split(' '))
        {
            spoken = spoken.Length == 0 ? word : spoken + " " + word;
            matcher.Process(spoken);
        }

        model.Tokens[matcher.CurrentTokenIndex].Normalized.Should().Be("nowe");
    }

    [Fact]
    public void Matcher_MatchesTurkishSpeech_AgainstCapitalizedScriptWords()
    {
        var model = ScriptModel.Build("IŞIK geldi ve oda aydınlandı", Turkish);
        var matcher = new ScriptMatcher(model);

        matcher.Process("ışık geldi ve oda");

        model.Tokens[matcher.CurrentTokenIndex].Normalized.Should().Be("oda");
    }
}
