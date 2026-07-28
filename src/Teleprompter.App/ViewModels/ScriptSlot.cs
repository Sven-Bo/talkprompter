using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Teleprompter.App.Services;

namespace Teleprompter.App.ViewModels;

/// <summary>A script in the library dropdown, with its remembered position.</summary>
public sealed partial class ScriptSlot : ObservableObject
{
    [ObservableProperty] private string _name = "Untitled";

    public string? FilePath { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>Last read word (token index), -1 for "start from the top".</summary>
    public int LastTokenIndex { get; set; } = -1;

    public DateTime LastOpenedUtc { get; set; } = DateTime.UtcNow;

    public static ScriptSlot From(ScriptEntry entry) => new()
    {
        Name = entry.Name,
        FilePath = entry.FilePath,
        Text = entry.Text,
        LastTokenIndex = entry.LastTokenIndex,
        LastOpenedUtc = entry.LastOpenedUtc
    };

    public ScriptEntry ToEntry() => new()
    {
        Name = Name,
        FilePath = FilePath,
        Text = Text,
        LastTokenIndex = LastTokenIndex,
        LastOpenedUtc = LastOpenedUtc
    };

    /// <summary>Derive a display name from the first line of pasted text.</summary>
    public static string DeriveName(string text)
    {
        foreach (string line in (text ?? string.Empty).Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed.Length <= 30 ? trimmed : trimmed[..30] + "…";
            }
        }

        return "Untitled";
    }
}
