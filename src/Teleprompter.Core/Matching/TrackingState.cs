namespace Teleprompter.Core.Matching;

/// <summary>
/// The matcher's view of what the reader is doing right now. The UI maps these
/// to scrolling behavior and the status indicator.
/// </summary>
public enum TrackingState
{
    /// <summary>No speech processed yet, or the matcher was reset.</summary>
    Idle,

    /// <summary>Speech matched the script; the anchor is advancing.</summary>
    Tracking,

    /// <summary>Speech did not match near the anchor (a pause or brief off-script); holding position.</summary>
    Paused,

    /// <summary>Held for long enough that re-acquisition is active (reader may have skipped ahead).</summary>
    Lost
}
