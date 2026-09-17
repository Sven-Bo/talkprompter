using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Teleprompter.App.Services;
using Teleprompter.Core.Languages;
using Teleprompter.Speech;

namespace Teleprompter.App.ViewModels;

/// <summary>
/// Languages and voice packs: which language the reader speaks, which packs
/// are installed, and the picker that downloads and switches them.
/// </summary>
public partial class MainViewModel
{
    private InstalledModels _models = InstalledModels.Empty;
    private CancellationTokenSource? _downloadCts;
    private bool _modelPromptDismissed;

    /// <summary>The language being read. Chooses the voice pack and the text rules.</summary>
    [ObservableProperty] private VoiceLanguage _activeLanguage = VoiceLanguageCatalog.English;

    [ObservableProperty] private IReadOnlyList<LanguageOption> _languageOptions = Array.Empty<LanguageOption>();

    /// <summary>The language highlighted in the picker; it becomes active once its pack is ready.</summary>
    [ObservableProperty] private LanguageOption? _selectedLanguageOption;

    /// <summary>The language picker overlay (also the first-run voice pack prompt).</summary>
    [ObservableProperty] private bool _showModelPrompt;

    /// <summary>First start: the picker explains what a voice pack is and offers to skip.</summary>
    [ObservableProperty] private bool _isFirstRunPrompt;

    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadStatus = string.Empty;

    /// <summary>The picker is locked while a pack downloads.</summary>
    public bool CanPickLanguage => !IsDownloading;

    partial void OnIsDownloadingChanged(bool value) => OnPropertyChanged(nameof(CanPickLanguage));

    public string ActiveLanguageSummary => _models.CanRecognize(ActiveLanguage.Code)
        ? ActiveLanguage.DisplayName
        : $"{ActiveLanguage.DisplayName} (not downloaded)";

    public string LanguageDialogTitle => IsFirstRunPrompt ? "One quick thing" : "Language";

    public string LanguageDialogText => IsFirstRunPrompt
        ? "To follow your voice while you read, TalkPrompter needs a voice pack for the language you read in. "
          + "It is a one time download of 30 to 70 MB. Everything runs on your computer. Nothing you say is sent anywhere."
        : "Pick the language you read your scripts in. Each language is a one time download "
          + "that runs entirely on your computer. You can switch any time.";

    public string LanguageDismissText => IsFirstRunPrompt ? "Skip for now" : "Close";

    public string LanguageActionText => SelectedLanguageOption switch
    {
        null => "Download",
        { IsInstalled: false } option => $"Download ({option.Language.DownloadMegabytes} MB)",
        { IsActive: true } => "Done",
        // Installed but not in use yet.
        { } option => $"Use {option.Language.EnglishName}"
    };

    partial void OnActiveLanguageChanged(VoiceLanguage value)
    {
        // Number handling and casing depend on the language.
        RebuildScript();
        RefreshLanguageState();
    }

    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (!IsDownloading)
        {
            DownloadStatus = string.Empty;
        }

        OnPropertyChanged(nameof(LanguageActionText));
    }

    partial void OnIsFirstRunPromptChanged(bool value)
    {
        OnPropertyChanged(nameof(LanguageDialogTitle));
        OnPropertyChanged(nameof(LanguageDialogText));
        OnPropertyChanged(nameof(LanguageDismissText));
    }

    [RelayCommand]
    private void OpenLanguagePicker()
    {
        // Pick up packs that were added or removed by hand since the last scan.
        RefreshModels();
        IsFirstRunPrompt = false;
        SelectedLanguageOption = LanguageOptions.FirstOrDefault(o => o.IsActive);
        DownloadStatus = string.Empty;
        ShowModelPrompt = true;
    }

    [RelayCommand]
    private void DismissLanguagePicker()
    {
        _downloadCts?.Cancel();
        if (IsFirstRunPrompt)
        {
            _modelPromptDismissed = true;
            SaveSettingsSnapshot();
        }

        ShowModelPrompt = false;
    }

    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();

    /// <summary>Download the highlighted language if needed, then switch to it.</summary>
    [RelayCommand]
    private async Task ApplyLanguageAsync()
    {
        LanguageOption? option = SelectedLanguageOption;
        if (option is null || IsDownloading)
        {
            return;
        }

        if (option is { IsActive: true, IsInstalled: true })
        {
            DismissLanguagePicker();
            return;
        }

        if (!option.IsInstalled && !await DownloadLanguageAsync(option.Language))
        {
            return; // the dialog stays open and shows why
        }

        if (IsRunning)
        {
            Stop();
        }

        ActiveLanguage = option.Language;
        _modelPromptDismissed = true;
        ShowModelPrompt = false;
        StatusText = $"{option.Language.DisplayName} is ready. Press Start and read.";
        SaveSettingsSnapshot();
    }

    private async Task<bool> DownloadLanguageAsync(VoiceLanguage language)
    {
        var cts = new CancellationTokenSource();
        _downloadCts = cts;
        IsDownloading = true;
        DownloadProgress = 0;
        DownloadStatus = "Starting download…";

        try
        {
            var progress = new Progress<(double Percent, string Status)>(p =>
            {
                if (IsDownloading)
                {
                    DownloadProgress = p.Percent;
                    DownloadStatus = p.Status;
                }
            });

            await ModelDownloadService.DownloadAsync(language, progress, cts.Token);
            RefreshModels();

            // Closed or cancelled at the last moment: keep the pack, but never
            // switch languages (or stop a take) behind the user's back.
            if (cts.IsCancellationRequested)
            {
                DownloadStatus = "Download cancelled.";
                return false;
            }

            if (_models.HasPackFor(language.Code))
            {
                return true;
            }

            DownloadStatus = "The voice pack downloaded, but TalkPrompter could not find it. Please try again.";
            return false;
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "Download cancelled.";
            return false;
        }
        catch (VoicePackException ex)
        {
            DownloadStatus = ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or HttpIOException or TimeoutException)
        {
            DownloadStatus = $"Download failed: {ex.Message} Check your internet connection and try again.";
            return false;
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Could not install the voice pack: {ex.Message}";
            return false;
        }
        finally
        {
            IsDownloading = false;
            _downloadCts = null;
            cts.Dispose();
        }
    }

    private void RefreshModels()
    {
        _models = ModelLocator.FindModels();
        RefreshLanguageState();
    }

    /// <summary>Re-derive everything that depends on the installed packs or the active language.</summary>
    private void RefreshLanguageState()
    {
        VoiceLanguage? highlighted = SelectedLanguageOption?.Language;
        LanguageOptions = VoiceLanguageCatalog.All
            .Select(l => new LanguageOption(l, _models.HasPackFor(l.Code), l == ActiveLanguage))
            .ToArray();
        SelectedLanguageOption = LanguageOptions.FirstOrDefault(o => o.Language == (highlighted ?? ActiveLanguage));

        EngineDescription = DescribeModels();
        OnPropertyChanged(nameof(ActiveLanguageSummary));
        OnPropertyChanged(nameof(LanguageActionText));
    }

    private string DescribeModels()
    {
        string? vosk = _models.VoskDirFor(ActiveLanguage.Code);
        bool sherpa = _models.SherpaDirFor(ActiveLanguage.Code) is not null;
        if (vosk is null && !sherpa)
        {
            return $"No {ActiveLanguage.EnglishName} voice pack yet, so Start runs a simulation.";
        }

        var parts = new List<string>();
        if (vosk is not null)
        {
            parts.Add($"Vosk ({Path.GetFileName(vosk)})");
        }

        if (sherpa)
        {
            parts.Add("sherpa-onnx");
        }

        return $"{ActiveLanguage.EnglishName} models: {string.Join(" · ", parts)}";
    }
}
