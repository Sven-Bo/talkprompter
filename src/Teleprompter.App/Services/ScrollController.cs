using System;
using System.Windows.Controls;
using System.Windows.Media;
using Teleprompter.Core.Scrolling;

namespace Teleprompter.App.Services;

/// <summary>
/// Drives a <see cref="ScrollViewer"/> smoothly toward a target offset each
/// frame using a critically-damped spring, turning the matcher's discrete
/// position jumps into continuous, natural scrolling.
/// </summary>
public sealed class ScrollController
{
    private readonly ScrollViewer _scrollViewer;
    private double _current;
    private double _target;
    private double _velocity;
    private TimeSpan _lastRenderTime;
    private bool _attached;

    /// <summary>Approximate seconds to reach a new target; higher is slower/smoother.</summary>
    public double SmoothTime { get; set; } = 0.35;

    /// <summary>
    /// When enabled, brief reader pauses do not hard-stop the motion: the view
    /// keeps drifting at a fraction of the measured reading pace, capped a
    /// short distance past the last confirmed position. Off by default — the
    /// classic behavior is "pause means stop".
    /// </summary>
    public bool FlowMode { get; set; }

    private const double FlowSpeedFactor = 0.4;   // fraction of measured pace
    private const double FlowMaxSeconds = 1.6;    // drift only through short pauses
    private const double FlowMaxOverrunPx = 90;   // never far past confirmed text

    private bool _readerPaused;
    private DateTime _pausedSinceUtc;
    private double _paceEma;                      // px/s of recent confirmed motion
    private double _lastConfirmedTarget;
    private DateTime _lastTargetUtc;

    /// <summary>The view reports reader state so drift only happens during short pauses.</summary>
    public void SetReaderPaused(bool paused)
    {
        if (_readerPaused == paused)
        {
            return;
        }

        _readerPaused = paused;
        _pausedSinceUtc = DateTime.UtcNow;
    }

    public ScrollController(ScrollViewer scrollViewer)
    {
        _scrollViewer = scrollViewer;
        _current = scrollViewer.VerticalOffset;
        _target = _current;
    }

    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _lastRenderTime = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
        _attached = true;
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _attached = false;
    }

    /// <summary>Smoothly scroll toward the given absolute vertical offset.</summary>
    public void SetTarget(double offset)
    {
        offset = Math.Max(0.0, offset);

        // Track the pace of confirmed forward motion for flow-mode drift.
        DateTime now = DateTime.UtcNow;
        if (offset > _lastConfirmedTarget && _lastTargetUtc != default)
        {
            double dt = (now - _lastTargetUtc).TotalSeconds;
            if (dt is > 0.05 and < 3.0)
            {
                double rate = Math.Clamp((offset - _lastConfirmedTarget) / dt, 0.0, 300.0);
                _paceEma = _paceEma <= 0 ? rate : 0.7 * _paceEma + 0.3 * rate;
            }
        }

        _lastConfirmedTarget = offset;
        _lastTargetUtc = now;
        _target = offset;
    }

    /// <summary>
    /// User wheel input moves the target itself — otherwise the spring would
    /// drag the view straight back to wherever it last was told to go, which
    /// feels like scrolling is broken. The next voice update re-takes control.
    /// </summary>
    public void UserScroll(double deltaPixels)
    {
        double limit = Math.Max(0.0, _scrollViewer.ScrollableHeight);
        _target = Math.Clamp(_target + deltaPixels, 0.0, limit);
    }

    /// <summary>Jump immediately, cancelling any in-flight motion.</summary>
    public void JumpTo(double offset)
    {
        _target = Math.Max(0.0, offset);
        _current = _target;
        _velocity = 0.0;
        _scrollViewer.ScrollToVerticalOffset(_current);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args)
        {
            return;
        }

        double deltaTime = _lastRenderTime == TimeSpan.Zero
            ? 1.0 / 60.0
            : (args.RenderingTime - _lastRenderTime).TotalSeconds;
        _lastRenderTime = args.RenderingTime;

        if (deltaTime <= 0.0)
        {
            return;
        }

        // Flow mode: glide through short pauses at reduced pace instead of a
        // hard stop, but never run far past the last confirmed position.
        if (FlowMode && _readerPaused && _paceEma > 1.0)
        {
            double pausedFor = (DateTime.UtcNow - _pausedSinceUtc).TotalSeconds;
            if (pausedFor < FlowMaxSeconds)
            {
                _target = Math.Min(
                    _target + _paceEma * FlowSpeedFactor * deltaTime,
                    _lastConfirmedTarget + FlowMaxOverrunPx);
            }
        }

        double target = Math.Min(_target, Math.Max(0.0, _scrollViewer.ScrollableHeight));

        // Resync if something else (user drag) moved the viewport.
        if (Math.Abs(_scrollViewer.VerticalOffset - _current) > 4.0)
        {
            _current = _scrollViewer.VerticalOffset;
        }

        if (Math.Abs(target - _current) < 0.25)
        {
            return;
        }

        _current = SmoothDamp.Step(_current, target, ref _velocity, SmoothTime, deltaTime);
        _scrollViewer.ScrollToVerticalOffset(_current);
    }
}
