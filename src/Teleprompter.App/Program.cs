using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Velopack;

namespace Teleprompter.App;

/// <summary>
/// Custom entry point. Velopack must run before any WPF code so install,
/// update, and uninstall events are handled; a named mutex enforces a single
/// instance (a second launch just activates the running window — two instances
/// would fight over the microphone and the global hotkey).
/// </summary>
public static class Program
{
    public const string ActivateMessageName = "TalkPrompter.Activate";

    private const int HwndBroadcast = 0xFFFF;

    private static Mutex? _singleInstanceMutex;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>System-wide message id used to activate the running instance.</summary>
    public static uint ActivateMessageId { get; private set; }

    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        // Must happen before anything reads settings or looks for voice packs.
        MigrateLegacyAppData();

        ActivateMessageId = RegisterWindowMessage(ActivateMessageName);

        _singleInstanceMutex = new Mutex(true, @"Local\TalkPrompter.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // Another instance is running: ask it to come to the front and exit.
            PostMessage(new IntPtr(HwndBroadcast), ActivateMessageId, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        try
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        finally
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
    }

    /// <summary>
    /// The app used to be called "AI Teleprompter". Carry settings, crash logs,
    /// and downloaded voice packs over from the old folders so a rename never
    /// costs anyone their data. Best-effort: a failed move just means a fresh
    /// start, never a crash.
    /// </summary>
    private static void MigrateLegacyAppData()
    {
        // %APPDATA%\AITeleprompter → %APPDATA%\TalkPrompter (settings + logs)
        try
        {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string oldDir = Path.Combine(roaming, "AITeleprompter");
            string newDir = Path.Combine(roaming, "TalkPrompter");
            if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
            {
                Directory.Move(oldDir, newDir);
            }

            // The old update feed pointed at the old repository name.
            string settingsPath = Path.Combine(newDir, "settings.json");
            if (File.Exists(settingsPath))
            {
                string json = File.ReadAllText(settingsPath);
                string patched = json.Replace(
                    "github.com/Sven-Bo/ai-teleprompter",
                    "github.com/Sven-Bo/talkprompter");
                if (!ReferenceEquals(json, patched) && json != patched)
                {
                    File.WriteAllText(settingsPath, patched);
                }
            }
        }
        catch (Exception)
        {
            // Best effort only.
        }

        // %LOCALAPPDATA%\AITeleprompter\models → %LOCALAPPDATA%\TalkPrompter\models
        // (only the models folder — the rest of the old local dir belongs to the
        // old install and its uninstaller).
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string oldModels = Path.Combine(local, "AITeleprompter", "models");
            string newRoot = Path.Combine(local, "TalkPrompter");
            string newModels = Path.Combine(newRoot, "models");
            if (Directory.Exists(oldModels) && !Directory.Exists(newModels))
            {
                Directory.CreateDirectory(newRoot);
                Directory.Move(oldModels, newModels);
            }
        }
        catch (Exception)
        {
            // Best effort only.
        }
    }
}
