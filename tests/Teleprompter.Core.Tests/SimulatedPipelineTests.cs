using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Teleprompter.Core.Matching;
using Teleprompter.Core.Text;
using Teleprompter.Speech;

namespace Teleprompter.Core.Tests;

/// <summary>
/// End-to-end check of the runtime pipeline: the simulated speech engine emits
/// growing hypotheses on its own thread, which drive the matcher and advance the
/// reading position — exactly the path the live app uses, minus the microphone.
/// </summary>
public sealed class SimulatedPipelineTests
{
    [Fact]
    public async Task SimulatedEngine_AdvancesTheMatcherThroughTheScript()
    {
        string script = string.Join(' ', Enumerable.Range(0, 20).Select(i => $"word{i}"));
        var model = ScriptModel.Build(script);
        var matcher = new ScriptMatcher(model);

        int reached = -1;
        using var engine = new SimulatedSpeechEngine(script, wordsPerMinute: 900);
        engine.HypothesisReceived += (_, hypothesis) =>
        {
            int index = matcher.Process(hypothesis.Text).TokenIndex;
            Volatile.Write(ref reached, Math.Max(Volatile.Read(ref reached), index));
        };

        engine.Start();
        await Task.Delay(TimeSpan.FromSeconds(2));
        engine.Stop();

        Volatile.Read(ref reached).Should().BeGreaterThan(
            5, "the simulated reader should have moved the position well into the script");
    }
}
