using System;
using System.IO;
using System.Linq;
using System.Threading;
using Teleprompter.Audio;
using Teleprompter.Core.Matching;
using Teleprompter.Core.Speech;
using Teleprompter.Core.Text;
using Teleprompter.Speech;

// A small diagnostic: proves the offline Vosk model loads and recognizes your
// voice, independent of the GUI.
//   --smoke        load the model in grammar mode, feed silence, confirm no error
//   (default)      listen to the default mic and print live recognition
//   --seconds N    how long to listen in live mode (default 20)
//   --no-grammar   use open-vocabulary recognition instead of script grammar
//   --wav <file>   decode a 16 kHz mono wav and report how far the matcher tracked
//   --script-file <file>  use this script (UTF-8 text) instead of the sample
//   --lang <code>  text rules for the script language (default en)
//   --realtime     feed the wav at real speed (with --trace: measures follow lag)
//   --trace        print "ADVANCE <seconds> <word index>" whenever the position moves

// Scripts and hypotheses in any language must survive the console.
Console.OutputEncoding = System.Text.Encoding.UTF8;

// --docx <file>: print the extracted script text (no model needed).
int docxIdx = Array.IndexOf(args, "--docx");
if (docxIdx >= 0 && docxIdx + 1 < args.Length)
{
    string extracted = DocxReader.ExtractText(args[docxIdx + 1]);
    Console.WriteLine(extracted);
    Console.WriteLine($"--- sections: {ScriptModel.Build(extracted).ParagraphStartTokens.Count}");
    return 0;
}

string? modelPath = FindModel();
if (modelPath is null)
{
    Console.WriteLine("No Vosk model found under any models/ folder. Run scripts/Get-VoskModel.ps1 first.");
    return 2;
}

Console.WriteLine($"Model: {modelPath}");

// The app's default sample script — its words become the recognizer grammar.
string script =
    "Welcome to TalkPrompter. As you read this text aloud, the script " +
    "scrolls itself to keep pace with your voice. If you stop speaking or wander " +
    "off script, the scrolling pauses and waits for you.";
int scriptIdx = Array.IndexOf(args, "--script-file");
if (scriptIdx >= 0 && scriptIdx + 1 < args.Length)
{
    script = File.ReadAllText(args[scriptIdx + 1]);
}

int langIdx = Array.IndexOf(args, "--lang");
string lang = langIdx >= 0 && langIdx + 1 < args.Length ? args[langIdx + 1] : "en";
var model = ScriptModel.Build(script, TextRules.ForLanguage(lang));
var vocabulary = model.MatchWords.Distinct().ToList();
bool useGrammar = !args.Contains("--no-grammar");

ISpeechEngine engine;
try
{
    bool isSherpa = SpeechEngineFactory.IsSherpaModel(modelPath);
    SpeechEngineFactory.Selection selection = SpeechEngineFactory.Create(
        isSherpa ? modelPath : null,
        isSherpa ? null : modelPath,
        script,
        useGrammar ? vocabulary : null,
        isSherpa ? EnginePreference.SherpaOnnx : EnginePreference.ScriptLockedVosk);
    engine = selection.Engine;
    Console.WriteLine($"Engine: {selection.Description}");
}
catch (Exception ex)
{
    Console.WriteLine($"FAILED to load the speech engine: {ex.Message}");
    return 3;
}

using (engine)
{
    engine.Start();

    if (args.Contains("--smoke"))
    {
        byte[] silence = new byte[3200]; // 0.1s of 16 kHz / 16-bit mono
        for (int i = 0; i < 10; i++)
        {
            engine.AcceptWaveform(silence, silence.Length);
        }

        engine.Stop();
        Console.WriteLine("SMOKE OK: model loaded and processed audio with no error.");
        return 0;
    }

    // --wav <file>: decode a 16 kHz mono 16-bit PCM wav and print what the
    // engine hears. Proves real recognition without a microphone.
    int wavIdx = Array.IndexOf(args, "--wav");
    if (wavIdx >= 0 && wavIdx + 1 < args.Length)
    {
        string wavPath = args[wavIdx + 1];
        byte[] wav = File.ReadAllBytes(wavPath);
        const int headerBytes = 44;

        string last = string.Empty;
        var wavMatcher = new ScriptMatcher(model);
        bool trace = args.Contains("--trace");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        engine.HypothesisReceived += (_, h) =>
        {
            last = h.Text;
            int before = wavMatcher.CurrentTokenIndex;
            wavMatcher.Process(h.Text);
            if (trace && wavMatcher.CurrentTokenIndex != before)
            {
                Console.WriteLine($"ADVANCE {clock.Elapsed.TotalSeconds:F2} {wavMatcher.CurrentTokenIndex}");
            }
            if (h.IsFinal)
            {
                Console.WriteLine($"FINAL: {h.Text}");
            }
        };

        // sherpa-onnx decodes on a bounded worker queue that drops audio when
        // flooded, so feed it at ~4x real time and let it drain before Stop.
        bool queued = engine is SherpaOnnxSpeechEngine;
        bool realtime = args.Contains("--realtime");
        const int chunk = 3200; // 0.1 s
        long fedBytes = 0;
        clock.Restart();

        // Real time paces against the clock (Thread.Sleep alone drifts), so
        // trace timestamps line up with positions in the audio.
        void Feed(byte[] buf, int len)
        {
            engine.AcceptWaveform(buf, len);
            fedBytes += len;
            if (realtime)
            {
                int ahead = (int)(fedBytes * 1000 / 32000 - clock.ElapsedMilliseconds);
                if (ahead > 0)
                {
                    Thread.Sleep(ahead);
                }
            }
            else if (queued)
            {
                Thread.Sleep(25);
            }
        }

        for (int off = headerBytes; off < wav.Length; off += chunk)
        {
            int len = Math.Min(chunk, wav.Length - off);
            byte[] buf = new byte[len];
            Array.Copy(wav, off, buf, 0, len);
            Feed(buf, len);
        }

        // Trailing silence so the endpoint detector commits the utterance.
        byte[] pad = new byte[chunk];
        for (int i = 0; i < 30; i++)
        {
            Feed(pad, pad.Length);
        }

        if (queued)
        {
            Thread.Sleep(3000);
        }

        engine.Stop();
        Console.WriteLine($"LAST HYPOTHESIS: {last}");
        int reached = wavMatcher.CurrentTokenIndex;
        string reachedWord = reached >= 0 ? model.Tokens[reached].Normalized : "-";
        Console.WriteLine($"TRACKED: word {reached + 1} of {model.TokenCount} (\"{reachedWord}\")");
        return string.IsNullOrWhiteSpace(last) ? 5 : 0;
    }

    int seconds = 20;
    int idx = Array.IndexOf(args, "--seconds");
    if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int s))
    {
        seconds = s;
    }

    var matcher = new ScriptMatcher(model);

    engine.HypothesisReceived += (_, h) =>
    {
        if (h.IsFinal)
        {
            if (!string.IsNullOrWhiteSpace(h.Text))
            {
                Console.WriteLine($"\nheard: {h.Text}");
            }
        }
        else
        {
            MatchUpdate u = matcher.Process(h.Text);
            string word = u.TokenIndex >= 0 ? model.Tokens[u.TokenIndex].Normalized : "-";
            Console.Write($"\r[{u.State,-8}] at \"{word}\"   (heard: {h.Text})".PadRight(90));
        }
    };

    using var mic = new MicrophoneCapture();
    mic.DataAvailable += (buffer, count) => engine.AcceptWaveform(buffer, count);

    Console.WriteLine($"Listening for {seconds}s on the default mic — read the sample script aloud now:\n");
    Console.WriteLine($"  \"{script}\"\n");

    try
    {
        mic.Start(null);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Could not open the microphone: {ex.Message}");
        return 4;
    }

    Thread.Sleep(seconds * 1000);
    mic.Stop();
    engine.Stop();
    Console.WriteLine("\n\nDone.");
}

return 0;

static string? FindModel()
{
    string? fromEnv = Environment.GetEnvironmentVariable("VOSK_MODEL_PATH");
    if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
    {
        return fromEnv;
    }

    DirectoryInfo? dir = new(AppContext.BaseDirectory);
    for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
    {
        string models = Path.Combine(dir.FullName, "models");
        if (!Directory.Exists(models))
        {
            continue;
        }

        var candidates = Directory.GetDirectories(models);

        // Prefer sherpa-onnx models (better accuracy), then Vosk.
        string? sherpa = candidates.FirstOrDefault(SpeechEngineFactory.IsSherpaModel);
        if (sherpa is not null)
        {
            return sherpa;
        }

        foreach (string candidate in candidates)
        {
            if (ModelScanner.IsVoskModel(candidate))
            {
                return candidate;
            }
        }
    }

    return null;
}
