using System.Linq;
using FluentAssertions;
using Teleprompter.Core.Matching;
using Teleprompter.Core.Text;

namespace Teleprompter.Core.Tests;

public sealed class ScriptMatcherTests
{
    private const string Fox = "The quick brown fox jumps over the lazy dog.";

    private static string WordAt(ScriptModel model, int tokenIndex)
        => tokenIndex >= 0 ? model.Tokens[tokenIndex].Normalized : "<none>";

    /// <summary>Feeds a hypothesis as a growing partial, word by word, like a live engine.</summary>
    private static MatchUpdate FeedGrowing(ScriptMatcher matcher, string utterance)
    {
        var words = utterance.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        MatchUpdate update = null!;
        for (int i = 1; i <= words.Length; i++)
        {
            update = matcher.Process(string.Join(' ', words.Take(i)));
        }

        return update;
    }

    [Fact]
    public void FirstSpokenWord_AdvancesImmediately_WhenItIsTheExpectedWord()
    {
        var model = ScriptModel.Build(Fox);
        var matcher = new ScriptMatcher(model);

        MatchUpdate update = matcher.Process("the");

        update.State.Should().Be(TrackingState.Tracking, "the exact next word should not wait for a second word");
        WordAt(model, update.TokenIndex).Should().Be("the");
    }

    [Fact]
    public void FirstSpokenWord_DoesNotAdvance_WhenItIsNotTheExpectedWord()
    {
        var model = ScriptModel.Build(Fox);
        var matcher = new ScriptMatcher(model);

        MatchUpdate update = matcher.Process("banana");

        update.TokenIndex.Should().Be(-1, "a lone off-script word must not move the anchor");
    }

    [Fact]
    public void FirstWordAfterPause_AdvancesImmediately()
    {
        var model = ScriptModel.Build(Fox);
        var matcher = new ScriptMatcher(model);

        FeedGrowing(matcher, "the quick brown fox");
        int before = matcher.CurrentTokenIndex;

        // New utterance after a pause: the very first word is the expected one.
        MatchUpdate update = matcher.Process("jumps");

        update.State.Should().Be(TrackingState.Tracking);
        update.TokenIndex.Should().Be(before + 1);
    }

    [Fact]
    public void CleanRead_AdvancesToEnd_Monotonically()
    {
        var model = ScriptModel.Build(Fox);
        var matcher = new ScriptMatcher(model);

        int last = -1;
        foreach (string prefix in new[]
                 {
                     "the quick brown",
                     "the quick brown fox jumps",
                     "the quick brown fox jumps over the lazy dog"
                 })
        {
            MatchUpdate update = matcher.Process(prefix);
            update.TokenIndex.Should().BeGreaterThanOrEqualTo(last, "position must never move backward during a clean read");
            last = update.TokenIndex;
        }

        matcher.State.Should().Be(TrackingState.Tracking);
        WordAt(model, matcher.CurrentTokenIndex).Should().Be("dog");
    }

    [Fact]
    public void Pause_HoldsPosition()
    {
        var model = ScriptModel.Build(Fox);
        var matcher = new ScriptMatcher(model);

        matcher.Process("the quick brown");
        int held = matcher.CurrentTokenIndex;

        // A couple of non-matching hypotheses (a pause filled with "um").
        matcher.Process("um");
        MatchUpdate update = matcher.Process("um well");

        update.Advanced.Should().BeFalse();
        matcher.CurrentTokenIndex.Should().Be(held);
        matcher.State.Should().Be(TrackingState.Paused);
    }

    [Fact]
    public void OffScript_ThenResume_ContinuesTracking()
    {
        var model = ScriptModel.Build(Fox);
        var matcher = new ScriptMatcher(model);

        matcher.Process("the quick brown");
        int beforeAdlib = matcher.CurrentTokenIndex;

        // Ad-lib that is not in the script: must not scroll.
        matcher.Process("so anyway let me think");
        matcher.CurrentTokenIndex.Should().Be(beforeAdlib);

        // Back on script: must resume and move forward.
        MatchUpdate resumed = matcher.Process("brown fox jumps over");
        resumed.State.Should().Be(TrackingState.Tracking);
        matcher.CurrentTokenIndex.Should().BeGreaterThan(beforeAdlib);
        WordAt(model, matcher.CurrentTokenIndex).Should().BeOneOf("jumps", "over");
    }

    [Fact]
    public void RepeatedWords_TrackTheCorrectOccurrence()
    {
        var model = ScriptModel.Build("the cat and the dog and the bird");
        var matcher = new ScriptMatcher(model);

        FeedGrowing(matcher, "the cat and the dog");
        WordAt(model, matcher.CurrentTokenIndex).Should().Be("dog");

        matcher.Process("and the bird");
        WordAt(model, matcher.CurrentTokenIndex).Should().Be("bird");
        matcher.CurrentTokenIndex.Should().Be(7, "it must match the third 'the', not an earlier one");
    }

    [Fact]
    public void SkipAhead_ReacquiresAfterHolding()
    {
        string script = string.Join(' ', Enumerable.Range(0, 40).Select(i => $"word{i}"));
        var model = ScriptModel.Build(script);
        var matcher = new ScriptMatcher(model);

        matcher.Process("word0 word1 word2 word3 word4");
        matcher.CurrentTokenIndex.Should().Be(4);

        // Reader jumps far ahead (beyond the local window). It should hold at
        // first, then re-acquire once the hold persists.
        MatchUpdate update = null!;
        for (int i = 0; i < 8; i++)
        {
            update = matcher.Process("word25 word26 word27");
        }

        update.State.Should().Be(TrackingState.Tracking);
        matcher.CurrentTokenIndex.Should().BeInRange(25, 27);
    }

    [Fact]
    public void GoingLost_ReportsLostState()
    {
        string script = string.Join(' ', Enumerable.Range(0, 40).Select(i => $"word{i}"));
        var matcher = new ScriptMatcher(ScriptModel.Build(script));

        matcher.Process("word0 word1 word2");

        MatchUpdate update = null!;
        for (int i = 0; i < 8; i++)
        {
            update = matcher.Process("completely different nonsense phrase here");
        }

        update.State.Should().Be(TrackingState.Lost);
    }

    [Fact]
    public void NumbersInScript_MatchSpokenWords()
    {
        var model = ScriptModel.Build("we raised 25 million dollars last year");
        var matcher = new ScriptMatcher(model);

        matcher.Process("we raised twenty five million");
        WordAt(model, matcher.CurrentTokenIndex).Should().Be("million");
    }

    [Fact]
    public void SeekToToken_MakesThatWordCurrent()
    {
        var model = ScriptModel.Build(Fox);
        var matcher = new ScriptMatcher(model);

        matcher.SeekToToken(4);
        WordAt(model, matcher.CurrentTokenIndex).Should().Be("jumps");
    }

    [Fact]
    public void Reset_ReturnsToStart()
    {
        var matcher = new ScriptMatcher(ScriptModel.Build(Fox));
        matcher.Process("the quick brown fox");
        matcher.Reset();

        matcher.CurrentTokenIndex.Should().Be(-1);
        matcher.State.Should().Be(TrackingState.Idle);
    }

    [Fact]
    public void ReRead_OfEarlierSentence_ReacquiresBackward()
    {
        // Three sentences with distinct vocabularies so a re-read of sentence
        // one cannot fuzzily match anything near the end of the script.
        const string script =
            "alpha bravo charlie delta echo. " +
            "foxtrot golf hotel india juliet. " +
            "kilo lima mike november oscar.";
        var model = ScriptModel.Build(script);
        var matcher = new ScriptMatcher(model);

        FeedGrowing(matcher, "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar");
        WordAt(model, matcher.CurrentTokenIndex).Should().Be("oscar");

        // Flubbed take: the reader goes back and re-reads sentence one. The
        // repeated non-matching partials trip re-acquisition, which must find
        // the match BEHIND the anchor.
        MatchUpdate update = null!;
        for (int repeat = 0; repeat < 3; repeat++)
        {
            update = FeedGrowing(matcher, "alpha bravo charlie delta echo");
        }

        update.State.Should().Be(TrackingState.Tracking, "re-reading an earlier sentence is a retake, not going off script");
        WordAt(model, update.TokenIndex).Should().Be("echo");
    }

    [Fact]
    public void ShortBackwardEcho_DoesNotYankThePositionBack()
    {
        const string script =
            "alpha bravo charlie delta echo. " +
            "foxtrot golf hotel india juliet. " +
            "kilo lima mike november oscar.";
        var matcher = new ScriptMatcher(ScriptModel.Build(script));

        FeedGrowing(matcher, "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar");
        int atEnd = matcher.CurrentTokenIndex;

        // A brief two-word echo of earlier text (below ReacquireBackMinWords)
        // must never rewind the anchor, no matter how often it repeats.
        for (int i = 0; i < 12; i++)
        {
            matcher.Process("alpha bravo");
        }

        matcher.CurrentTokenIndex.Should().Be(atEnd, "two words are not enough evidence for a backward jump");
    }

    [Fact]
    public void SingleWord_DoesNotTriggerJump_UnlessItIsTheExpectedWord()
    {
        var matcher = new ScriptMatcher(ScriptModel.Build(Fox));

        // "lazy" is in the script but is not the next expected word — a lone
        // occurrence elsewhere must not yank the anchor forward. (The exact
        // next word, by contrast, advances immediately via the fast path.)
        MatchUpdate update = matcher.Process("lazy");

        update.Advanced.Should().BeFalse("a single non-expected word is not enough evidence to scroll");
        matcher.CurrentTokenIndex.Should().Be(-1);
    }
}
