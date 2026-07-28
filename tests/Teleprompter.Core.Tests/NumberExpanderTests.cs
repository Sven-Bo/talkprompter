using FluentAssertions;
using Teleprompter.Core.Text;

namespace Teleprompter.Core.Tests;

public sealed class NumberExpanderTests
{
    [Theory]
    [InlineData("0", "zero")]
    [InlineData("5", "five")]
    [InlineData("13", "thirteen")]
    [InlineData("25", "twenty five")]
    [InlineData("100", "one hundred")]
    [InlineData("135", "one hundred thirty five")]
    [InlineData("2025", "two thousand twenty five")]
    [InlineData("007", "seven")]
    public void ToWords_ExpandsCardinals(string digits, string expected)
    {
        string.Join(' ', NumberExpander.ToWords(digits)).Should().Be(expected);
    }

    [Fact]
    public void ToWords_ReturnsOriginal_WhenNotNumeric()
    {
        NumberExpander.ToWords("abc").Should().ContainSingle().Which.Should().Be("abc");
    }

    [Fact]
    public void ToWords_ReturnsOriginal_WhenTooLong()
    {
        string big = new string('9', 20);
        NumberExpander.ToWords(big).Should().ContainSingle().Which.Should().Be(big);
    }

    [Theory]
    [InlineData("123", true)]
    [InlineData("", false)]
    [InlineData("12a", false)]
    public void IsAllDigits_Works(string input, bool expected)
    {
        NumberExpander.IsAllDigits(input).Should().Be(expected);
    }
}
