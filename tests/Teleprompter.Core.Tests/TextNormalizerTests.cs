using FluentAssertions;
using Teleprompter.Core.Text;

namespace Teleprompter.Core.Tests;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("Hello,", "hello")]
    [InlineData("WORLD!", "world")]
    [InlineData("don't", "dont")]
    [InlineData("...", "")]
    public void NormalizeWord_StripsPunctuationAndLowercases(string raw, string expected)
    {
        TextNormalizer.NormalizeWord(raw).Should().Be(expected);
    }

    [Fact]
    public void ToMatchWords_ExpandsNumbersInline()
    {
        TextNormalizer.ToMatchWords("I have 25 cats")
            .Should().Equal("i", "have", "twenty", "five", "cats");
    }

    [Fact]
    public void ToMatchWords_ReturnsEmpty_ForBlankInput()
    {
        TextNormalizer.ToMatchWords("   ").Should().BeEmpty();
    }

    [Fact]
    public void Tokenize_PreservesCharacterSpans()
    {
        const string text = "The quick, brown fox.";
        var tokens = TextNormalizer.Tokenize(text);

        tokens.Should().HaveCount(4);
        tokens[0].Normalized.Should().Be("the");
        text.Substring(tokens[1].Start, tokens[1].Length).Should().Be("quick");
        text.Substring(tokens[3].Start, tokens[3].Length).Should().Be("fox");
        tokens[3].Index.Should().Be(3);
    }
}
