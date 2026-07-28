using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace Teleprompter.App.Services;

public enum ThemeMode
{
    Auto,
    Dark,
    Light
}

/// <summary>
/// Runtime theming. The palette lives in Themes/Dark.xaml and Themes/Light.xaml
/// (same keys, referenced everywhere via DynamicResource), so applying a theme
/// is swapping one merged dictionary. Auto follows the Windows apps theme and
/// reacts live when it changes.
/// </summary>
public static class ThemeService
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static ThemeMode _mode = ThemeMode.Dark;
    private static bool _systemHookInstalled;

    /// <summary>Raised after a palette swap; argument is "is dark now".</summary>
    public static event Action<bool>? ThemeApplied;

    public static bool IsDarkActive { get; private set; } = true;

    public static ThemeMode Parse(string? value) => value switch
    {
        "Light" => ThemeMode.Light,
        "Auto" => ThemeMode.Auto,
        _ => ThemeMode.Dark
    };

    public static string Serialize(ThemeMode mode) => mode.ToString();

    public static void Apply(ThemeMode mode)
    {
        _mode = mode;

        if (mode == ThemeMode.Auto && !_systemHookInstalled)
        {
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
            _systemHookInstalled = true;
        }

        bool dark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => IsWindowsAppsThemeDark()
        };

        SwapPalette(dark);
    }

    private static void SwapPalette(bool dark)
    {
        var uri = new Uri($"Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        ResourceDictionary? existing = dictionaries.FirstOrDefault(d =>
            d.Source is not null && d.Source.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase));

        var replacement = new ResourceDictionary { Source = uri };
        if (existing is not null)
        {
            int index = dictionaries.IndexOf(existing);
            dictionaries[index] = replacement;
        }
        else
        {
            dictionaries.Insert(0, replacement);
        }

        IsDarkActive = dark;
        ThemeApplied?.Invoke(dark);
    }

    private static bool IsWindowsAppsThemeDark()
    {
        try
        {
            // 0 = dark apps theme, 1 = light; missing value means dark-capable
            // builds default to light.
            object? value = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1);
            return value is int lightEnabled && lightEnabled == 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode != ThemeMode.Auto || e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_mode == ThemeMode.Auto)
            {
                SwapPalette(IsWindowsAppsThemeDark());
            }
        });
    }
}
