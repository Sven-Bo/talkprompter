using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Teleprompter.Core.Languages;
using Teleprompter.Speech;

namespace Teleprompter.App.Services;

/// <summary>
/// Installs voice packs into the per-user models folder
/// (%LOCALAPPDATA%\TalkPrompter\models). That folder is outside the
/// installation directory, so downloaded packs survive every auto-update —
/// which in turn lets the installer ship without any bundled model.
/// </summary>
public static class ModelDownloadService
{
    public static string UserModelsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TalkPrompter",
        "models");

    /// <inheritdoc cref="VoicePackInstaller.InstallAsync"/>
    public static Task<string> DownloadAsync(
        VoiceLanguage language,
        IProgress<(double Percent, string Status)> progress,
        CancellationToken cancellationToken)
        => new VoicePackInstaller(UserModelsDirectory).InstallAsync(language, progress, cancellationToken);
}
