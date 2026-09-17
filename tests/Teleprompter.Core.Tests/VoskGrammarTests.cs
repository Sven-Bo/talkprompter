using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Teleprompter.Speech;

namespace Teleprompter.Core.Tests;

public sealed class VoskGrammarTests
{
    [Fact]
    public void BuildGrammar_WritesNonAsciiWordsLiterally()
    {
        // Vosk's JSON reader does not decode \u escapes: an escaped "größe"
        // is looked up as the literal text "größe", is missing from
        // the vocabulary, and silently drops out of the script lock.
        string json = VoskSpeechEngine.BuildGrammar(new[] { "größe", "łódź", "привет" })!;

        json.Should().NotContain("\\u");
        json.Should().Contain("größe").And.Contain("łódź").And.Contain("привет");
    }

    [Fact]
    public void BuildGrammar_IsAJsonListOfDistinctWordsPlusUnknown()
    {
        string json = VoskSpeechEngine.BuildGrammar(new[] { "hello", "world", "hello" })!;

        JsonSerializer.Deserialize<string[]>(json)
            .Should().BeEquivalentTo("hello", "world", "[unk]");
    }

    [Fact]
    public void BuildGrammar_ReturnsNull_WhenThereIsNothingToLock()
    {
        VoskSpeechEngine.BuildGrammar(null).Should().BeNull();
        VoskSpeechEngine.BuildGrammar(Array.Empty<string>()).Should().BeNull();
    }

    [Fact]
    public void BuildGrammar_ReturnsNull_ForVocabulariesTooLargeToLock()
    {
        var words = Enumerable.Range(0, SpeechEngineFactory.GrammarWordLimit + 1).Select(i => "w" + i).ToArray();

        VoskSpeechEngine.BuildGrammar(words).Should().BeNull();
    }
}
