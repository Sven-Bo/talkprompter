using System;

namespace Teleprompter.App.Services;

/// <summary>
/// One remembered script in the library. Text is stored as a snapshot so the
/// script keeps working even if the original file moves; the reading position
/// is remembered per script so a session can be resumed mid-lesson.
/// </summary>
public sealed record ScriptEntry
{
    public string? FilePath { get; init; }
    public string Name { get; init; } = "Untitled";
    public string Text { get; init; } = string.Empty;
    public int LastTokenIndex { get; init; } = -1;
    public DateTime LastOpenedUtc { get; init; }
}
