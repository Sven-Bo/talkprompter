using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Teleprompter.App.Services;

/// <summary>
/// Auto-update via Velopack. The feed (<c>UpdateFeedUrl</c> in settings.json)
/// can be a local/network folder, a plain https feed, or a GitHub repository
/// URL (github.com/user/repo) whose Releases carry the Velopack assets;
/// <c>UpdateFeedToken</c> supplies an access token while a repo is private.
///
/// Flow: the app checks in the background, then ASKS the user with the release
/// notes shown. Only after they accept does the download run (with progress),
/// and the update applies on close or via Restart now. Runs only in the
/// installed app; dev builds and the portable zip skip updating entirely.
/// </summary>
public sealed class UpdateService
{
    private readonly UpdateManager? _manager;
    private UpdateInfo? _available;

    public UpdateService(string? feedUrlOrPath, string? accessToken = null)
    {
        if (string.IsNullOrWhiteSpace(feedUrlOrPath))
        {
            return;
        }

        try
        {
            _manager = feedUrlOrPath.Contains("github.com", StringComparison.OrdinalIgnoreCase)
                ? new UpdateManager(new GithubSource(
                    feedUrlOrPath,
                    string.IsNullOrWhiteSpace(accessToken) ? null : accessToken,
                    prerelease: false))
                : new UpdateManager(feedUrlOrPath);
        }
        catch (Exception)
        {
            _manager = null; // malformed feed must never break the app
        }
    }

    public bool IsConfigured => _manager is not null;

    public bool IsSupported
    {
        get
        {
            try
            {
                return _manager?.IsInstalled == true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public bool HasUpdate => _available is not null;

    public bool IsDownloaded { get; private set; }

    public string? AvailableVersion => _available?.TargetFullRelease?.Version?.ToString();

    public string AvailableNotes => _available?.TargetFullRelease?.NotesMarkdown ?? string.Empty;

    /// <summary>Look for a newer version. Returns true when one is available.</summary>
    public async Task<bool> CheckAsync()
    {
        if (_manager is null || !IsSupported)
        {
            return false;
        }

        _available = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _available is not null;
    }

    /// <summary>
    /// Download the accepted update, reporting 0-100 progress, and schedule it
    /// to apply when the app exits.
    /// </summary>
    public async Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        if (_manager is null || _available is null)
        {
            throw new InvalidOperationException("No update has been found to download.");
        }

        await _manager.DownloadUpdatesAsync(_available, p => progress.Report(p), cancellationToken)
            .ConfigureAwait(false);
        _manager.WaitExitThenApplyUpdates(_available, silent: true, restart: false);
        IsDownloaded = true;
    }

    /// <summary>Apply the downloaded update immediately and relaunch.</summary>
    public void RestartNow()
    {
        if (_manager is not null && _available is not null && IsDownloaded)
        {
            _manager.ApplyUpdatesAndRestart(_available);
        }
    }
}
