using System.Collections.Generic;
using System.IO;
using System.Linq;
using Teleprompter.Core.Speech;

namespace Teleprompter.Speech;

/// <summary>Which recognizer to run for a session.</summary>
public enum EnginePreference
{
    /// <summary>Script-locked Vosk when the script's vocabulary allows it, else sherpa-onnx.</summary>
    Auto,

    /// <summary>Vosk constrained to the script's words. Best for reading a known script.</summary>
    ScriptLockedVosk,

    /// <summary>sherpa-onnx streaming zipformer, open vocabulary.</summary>
    SherpaOnnx
}

/// <summary>
/// Builds the speech engine for a session from the installed models and the
/// user's preference.
///
/// Field experience: for reading a KNOWN script aloud — especially with a
/// non-native accent — Vosk locked to the script's vocabulary out-tracks the
/// stronger open-vocabulary sherpa model, because the recognizer can only emit
/// words the matcher will accept. Auto therefore prefers the script lock and
/// uses sherpa when the lock is not possible (huge vocabulary, no Vosk model).
/// </summary>
public static class SpeechEngineFactory
{
    /// <summary>Must match VoskSpeechEngine's grammar limit.</summary>
    public const int GrammarWordLimit = 1200;

    public sealed record Selection(ISpeechEngine Engine, bool IsSimulated, string Description);

    public static Selection Create(
        string? sherpaDir,
        string? voskDir,
        string scriptText,
        IReadOnlyList<string>? vocabulary,
        EnginePreference preference = EnginePreference.Auto)
    {
        bool grammarFeasible = voskDir is not null
            && vocabulary is not null
            && vocabulary.Count > 0
            && vocabulary.Count <= GrammarWordLimit;

        return preference switch
        {
            EnginePreference.ScriptLockedVosk when voskDir is not null => Vosk(voskDir, vocabulary),
            EnginePreference.SherpaOnnx when sherpaDir is not null => Sherpa(sherpaDir),
            _ when grammarFeasible => Vosk(voskDir!, vocabulary),
            _ when sherpaDir is not null => Sherpa(sherpaDir),
            _ when voskDir is not null => Vosk(voskDir, vocabulary),
            _ => new Selection(
                new SimulatedSpeechEngine(scriptText),
                IsSimulated: true,
                Description: "Simulation (no speech model installed)")
        };
    }

    private static Selection Vosk(string dir, IReadOnlyList<string>? vocabulary)
    {
        var engine = new VoskSpeechEngine(dir, vocabulary);
        string name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        string mode = engine.UsingGrammar ? " · locked to your script" : string.Empty;
        return new Selection(engine, IsSimulated: false, Description: $"Vosk: {name}{mode}");
    }

    private static Selection Sherpa(string dir)
    {
        string name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        return new Selection(
            new SherpaOnnxSpeechEngine(dir),
            IsSimulated: false,
            Description: $"sherpa-onnx: {name}");
    }

    public static bool IsSherpaModel(string dir)
        => File.Exists(Path.Combine(dir, "tokens.txt"))
           && Directory.GetFiles(dir, "encoder*.onnx").Any();
}
