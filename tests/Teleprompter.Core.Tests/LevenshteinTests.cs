using FluentAssertions;
using Teleprompter.Core.Matching;

namespace Teleprompter.Core.Tests;

public sealed class LevenshteinTests
{
    [Fact]
    public void Distance_IsZero_ForIdenticalSequences()
    {
        Levenshtein.Distance(new[] { "a", "b", "c" }, new[] { "a", "b", "c" }).Should().Be(0);
    }

    [Fact]
    public void Distance_CountsSubstitution()
    {
        Levenshtein.Distance(new[] { "a", "b", "c" }, new[] { "a", "x", "c" }).Should().Be(1);
    }

    [Fact]
    public void Distance_CountsInsertionAndDeletion()
    {
        Levenshtein.Distance(new[] { "a", "b" }, new[] { "a", "b", "c" }).Should().Be(1);
        Levenshtein.Distance(new[] { "a", "b", "c" }, new[] { "a", "c" }).Should().Be(1);
    }

    [Fact]
    public void Distance_EqualsLength_AgainstEmpty()
    {
        Levenshtein.Distance(new[] { "a", "b", "c" }, System.Array.Empty<string>()).Should().Be(3);
    }

    [Fact]
    public void Similarity_IsOne_ForIdentical()
    {
        Levenshtein.Similarity(new[] { "a", "b" }, new[] { "a", "b" }).Should().Be(1.0);
    }

    [Fact]
    public void Similarity_IsHalf_ForOneOfTwoWrong()
    {
        Levenshtein.Similarity(new[] { "a", "b" }, new[] { "a", "x" }).Should().Be(0.5);
    }
}
