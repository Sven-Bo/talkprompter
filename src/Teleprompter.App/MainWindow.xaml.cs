using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Teleprompter.App.Services;
using Teleprompter.App.ViewModels;
using Teleprompter.Core.Text;

namespace Teleprompter.App;

/// <summary>
/// The single, teleprompter-first window: the scrolling script fills the view,
/// a slim toolbar drives it, and the editor slides in over the top on demand.
/// </summary>
public partial class MainWindow : Window
{
    // Where the current word rides, as a fraction of viewport height. Must
    // match the guide-line row split in MainWindow.xaml (0.33* / Auto / 0.67*).
    private const double ReadingFraction = 0.33;

    private ScrollController? _scroll;
    private Run[] _tokenRuns = Array.Empty<Run>();
    private Run? _currentRun;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;
    private bool _isFullscreen;
    private Rect _boundsBeforeCamera;
    private bool _topmostBeforeCamera;
    private bool _inCameraMode;
    private DispatcherTimer? _countdownTimer;
    private int _countdownValue;

    public MainWindow()
    {
        InitializeComponent();

        ViewModel = new MainViewModel();
        DataContext = ViewModel;

        RestoreWindowPlacement(ViewModel.InitialSettings);

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        SizeChanged += OnWindowSizeChanged;
        Closing += OnClosing;
        Closed += OnClosed;
        ThemeService.ThemeApplied += OnThemeApplied;
    }

    private void OnThemeApplied(bool isDark)
    {
        // Title bar chrome + the current word highlight use resolved brushes,
        // so both must react to a palette swap.
        ApplyTitleBarTheme(isDark);
        if (_currentRun is not null)
        {
            HighlightAndScroll(ViewModel.CurrentTokenIndex);
        }
    }

    /// <summary>Restore the last session's window placement if it is still on-screen.</summary>
    private void RestoreWindowPlacement(AppSettings settings)
    {
        if (!settings.HasWindowPlacement)
        {
            return;
        }

        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var saved = new Rect(settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight);

        if (!virtualScreen.IntersectsWith(saved))
        {
            return; // monitor layout changed; keep the default centered position
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = saved.Left;
        Top = saved.Top;
        Width = saved.Width;
        Height = saved.Height;
        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        Topmost = settings.Topmost;
    }

    public MainViewModel ViewModel { get; }

    // ----- Dark native title bar (Windows 10 20H1+ / Windows 11) -----

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ----- Global start/stop hotkey (Ctrl+Alt+Space, works while OBS etc. has focus) -----

    private const int HotkeyMessage = 0x0312;
    private const int HotkeyId = 0xA17E;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkSpace = 0x20;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private HwndSource? _hwndSource;

    private void ApplyTitleBarTheme(bool dark)
    {
        const int DwmwaUseImmersiveDarkMode = 20;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int enabled = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyTitleBarTheme(ThemeService.IsDarkActive);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(OnWindowMessage);
        // Best-effort: if another app owns the combo, local shortcuts still work.
        _ = RegisterHotKey(hwnd, HotkeyId, ModControl | ModAlt, VkSpace);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyMessage && wParam.ToInt32() == HotkeyId)
        {
            ViewModel.ToggleCommand.Execute(null);
            handled = true;
        }
        else if (Program.ActivateMessageId != 0 && msg == (int)Program.ActivateMessageId)
        {
            // A second launch asked us to come to the front.
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scroll = new ScrollController(Scroller)
        {
            SmoothTime = ViewModel.ScrollSmoothness,
            FlowMode = ViewModel.FlowModeEnabled
        };
        _scroll.Attach();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.ScriptRebuilt += OnScriptRebuilt;

        TopmostToggle.IsChecked = Topmost;
        ApplyMirror();
        BuildDocument();

        // Show the remembered per-script position right away (no property
        // change fires for the value seeded at construction).
        if (ViewModel.CurrentTokenIndex >= 0)
        {
            HighlightAndScroll(ViewModel.CurrentTokenIndex);
        }

        // Gentle fade-in on first render.
        BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220)));

        // Silent background update check (installed app only), off the UI path.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(3));
            await ViewModel.AutoCheckUpdatesAsync();
        });

        // Optional demo/test hook: launch with --autostart-sim to begin the
        // simulated reader immediately (no mic needed).
        if (Environment.GetCommandLineArgs().Contains("--autostart-sim"))
        {
            ViewModel.ForceSimulation = true;
            ViewModel.ToggleCommand.Execute(null);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Persist the pre-camera/pre-fullscreen placement, never the transient one.
        Rect bounds = _inCameraMode
            ? _boundsBeforeCamera
            : WindowState == WindowState.Normal && !_isFullscreen
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;

        bool maximized = _isFullscreen
            ? _stateBeforeFullscreen == WindowState.Maximized
            : WindowState == WindowState.Maximized;

        bool topmost = _inCameraMode ? _topmostBeforeCamera : Topmost;

        ViewModel.SaveSettings(bounds, maximized, topmost);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_hwndSource is not null)
        {
            _ = UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            _hwndSource.RemoveHook(OnWindowMessage);
            _hwndSource = null;
        }

        ThemeService.ThemeApplied -= OnThemeApplied;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.ScriptRebuilt -= OnScriptRebuilt;
        _countdownTimer?.Stop();
        _scroll?.Detach();
        ViewModel.Dispose();
    }

    private void OnScriptRebuilt(object? sender, EventArgs e) => Dispatcher.Invoke(BuildDocument);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CurrentTokenIndex):
                HighlightAndScroll(ViewModel.CurrentTokenIndex);
                break;
            case nameof(MainViewModel.ScrollSmoothness):
                if (_scroll is not null)
                {
                    _scroll.SmoothTime = ViewModel.ScrollSmoothness;
                }

                break;
            case nameof(MainViewModel.MirrorHorizontal):
                ApplyMirror();
                break;
            case nameof(MainViewModel.ColumnWidth):
                if (_currentRun is not null)
                {
                    HighlightAndScroll(ViewModel.CurrentTokenIndex);
                }

                break;
            case nameof(MainViewModel.FlowModeEnabled):
                if (_scroll is not null)
                {
                    _scroll.FlowMode = ViewModel.FlowModeEnabled;
                }

                break;
            case nameof(MainViewModel.TrackingStateText):
                _scroll?.SetReaderPaused(ViewModel.TrackingStateText == "Paused");
                break;
            case nameof(MainViewModel.IsRunning):
                if (ViewModel.IsRunning)
                {
                    HideEditor();
                    if (ViewModel.CountdownEnabled && !ViewModel.ForceSimulation)
                    {
                        StartCountdown();
                    }
                }
                else
                {
                    CancelCountdown();
                }

                break;
        }
    }

    // ----- 3-2-1 countdown -----

    private void StartCountdown()
    {
        CancelCountdown();
        _countdownValue = 3;
        CountdownText.Text = "3";
        CountdownOverlay.Visibility = Visibility.Visible;
        CountdownOverlay.BeginAnimation(
            OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(120)));
        PulseCountdown();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        _countdownValue--;
        if (_countdownValue <= 0)
        {
            CancelCountdown();
            return;
        }

        CountdownText.Text = _countdownValue.ToString();
        PulseCountdown();
    }

    /// <summary>Scale + fade pop for each countdown digit.</summary>
    private void PulseCountdown()
    {
        var scale = new DoubleAnimation(0.82, 1.0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
        };
        CountdownScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        CountdownScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        CountdownText.BeginAnimation(
            OpacityProperty, new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(200)));
    }

    private void OnCountdownClicked(object sender, RoutedEventArgs e) => CancelCountdown();

    private void CancelCountdown()
    {
        if (_countdownTimer is not null)
        {
            _countdownTimer.Tick -= OnCountdownTick;
            _countdownTimer.Stop();
            _countdownTimer = null;
        }

        CountdownOverlay.Visibility = Visibility.Collapsed;
    }

    // ----- Camera mode -----

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>
    /// Work area of the monitor this window is on, in WPF device-independent
    /// units. Camera mode must target the current monitor — the webcam is
    /// wherever the user put the window, not necessarily on the primary screen.
    /// </summary>
    private Rect GetCurrentMonitorWorkArea()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info)
                && PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            {
                Matrix fromDevice = target.TransformFromDevice;
                Point topLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
                Point bottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
                return new Rect(topLeft, bottomRight);
            }
        }
        catch (Exception)
        {
            // Fall through to the primary work area.
        }

        return SystemParameters.WorkArea;
    }

    private void OnToggleCameraMode(object sender, RoutedEventArgs e)
    {
        if (!_inCameraMode)
        {
            if (_isFullscreen)
            {
                ToggleFullscreen();
            }

            _boundsBeforeCamera = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;
            _topmostBeforeCamera = Topmost;

            Rect work = GetCurrentMonitorWorkArea();
            double width = 520;
            WindowState = WindowState.Normal;
            Width = width;
            Height = Math.Max(MinHeight, work.Height * 0.72);
            Left = work.Left + (work.Width - width) / 2;
            Top = work.Top;
            Topmost = true;
            TopmostToggle.IsChecked = true;
            _inCameraMode = true;
        }
        else
        {
            Left = _boundsBeforeCamera.Left;
            Top = _boundsBeforeCamera.Top;
            Width = _boundsBeforeCamera.Width;
            Height = _boundsBeforeCamera.Height;
            Topmost = _topmostBeforeCamera;
            TopmostToggle.IsChecked = _topmostBeforeCamera;
            _inCameraMode = false;
        }

        if (CameraToggle.IsChecked != _inCameraMode)
        {
            CameraToggle.IsChecked = _inCameraMode;
        }
    }

    // ----- Keyboard shortcuts -----

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        // Don't hijack keys while typing in the script editor.
        bool editing = Keyboard.FocusedElement is TextBox;

        switch (e.Key)
        {
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape when _isFullscreen:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape when EditorPanel.Visibility == Visibility.Visible:
                OnDoneEditing(sender, e);
                e.Handled = true;
                break;
            case Key.Space when !editing:
                ViewModel.ToggleCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.PageDown when !editing
                && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control:
                ViewModel.JumpParagraph(1);
                e.Handled = true;
                break;
            case Key.PageUp when !editing
                && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control:
                ViewModel.JumpParagraph(-1);
                e.Handled = true;
                break;
            case Key.PageDown when !editing:
                ViewModel.NudgeWords(8);
                e.Handled = true;
                break;
            case Key.PageUp when !editing:
                ViewModel.NudgeWords(-8);
                e.Handled = true;
                break;
            case Key.Home when !editing:
                ViewModel.ResetPositionCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ----- Click a word to re-anchor -----

    private void OnPromptClicked(object sender, MouseButtonEventArgs e)
    {
        int tokenIndex = HitTestToken(e.GetPosition(Prompt));
        if (tokenIndex >= 0)
        {
            ViewModel.SeekToToken(tokenIndex);
        }
    }

    /// <summary>
    /// Finds the token whose rendered rect contains the click point. Runs are in
    /// document order, so the scan can stop once it is past the clicked line.
    /// </summary>
    private int HitTestToken(Point point)
    {
        for (int i = 0; i < _tokenRuns.Length; i++)
        {
            Rect start = _tokenRuns[i].ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (start.IsEmpty)
            {
                continue;
            }

            if (start.Top > point.Y + start.Height)
            {
                return -1; // past the clicked line
            }

            Rect end = _tokenRuns[i].ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            var rect = new Rect(start.TopLeft, end.BottomRight);
            rect.Inflate(2, 2);
            if (rect.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    private void OnToggleFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _stateBeforeFullscreen = WindowState;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal; // reset first so Maximized covers the taskbar
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _stateBeforeFullscreen;
            _isFullscreen = false;
        }

        if (FullToggle.IsChecked != _isFullscreen)
        {
            FullToggle.IsChecked = _isFullscreen;
        }
    }

    // ----- Document build + tracking display -----

    /// <summary>
    /// Rebuilds the flowing text, keeping a Run per token so the current word can
    /// be recolored and located. Gaps between tokens (spaces, punctuation, line
    /// breaks) are copied verbatim from the original text.
    /// </summary>
    private void BuildDocument()
    {
        ScriptModel model = ViewModel.Script ?? ScriptModel.Build(ViewModel.ScriptText ?? string.Empty);
        string text = model.Text;

        Prompt.Inlines.Clear();
        _tokenRuns = new Run[model.TokenCount];
        _currentRun = null;

        int cursor = 0;
        foreach (ScriptToken token in model.Tokens)
        {
            if (token.Start > cursor)
            {
                Prompt.Inlines.Add(new Run(text.Substring(cursor, token.Start - cursor)));
            }

            var run = new Run(text.Substring(token.Start, token.Length));
            _tokenRuns[token.Index] = run;
            Prompt.Inlines.Add(run);
            cursor = token.End;
        }

        if (cursor < text.Length)
        {
            Prompt.Inlines.Add(new Run(text.Substring(cursor)));
        }

        _scroll?.JumpTo(0);
    }

    private void HighlightAndScroll(int tokenIndex)
    {
        if (_currentRun is not null)
        {
            _currentRun.Background = null;
            _currentRun.ClearValue(TextElement.ForegroundProperty);
        }

        if (tokenIndex < 0 || tokenIndex >= _tokenRuns.Length)
        {
            // Position was reset (Top button, Home, script switch): the view
            // must actually go back to the top, not just drop the highlight.
            _currentRun = null;
            _scroll?.SetTarget(0.0);
            return;
        }

        Run run = _tokenRuns[tokenIndex];
        // Resolved per call so a live theme switch recolors the highlight.
        run.Background = TryFindResource("Highlight") as Brush;
        run.Foreground = TryFindResource("HighlightText") as Brush;
        _currentRun = run;

        Prompt.UpdateLayout();
        Rect rect = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty)
        {
            return;
        }

        // rect.Top is content-relative (fixed position within the text, not
        // affected by scrolling), so the target offset is simply its position
        // minus where we want it — the reading line. Adding the live scroll
        // offset here would create a runaway feedback loop.
        double viewport = Scroller.ViewportHeight > 0 ? Scroller.ViewportHeight : Scroller.ActualHeight;
        double readingLine = viewport * ReadingFraction;
        double target = rect.Top - readingLine;
        _scroll?.SetTarget(target);
    }

    // One wheel notch (delta 120) moves ~120px — about one prompter line.
    private void OnPromptMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _scroll?.UserScroll(-e.Delta);
        e.Handled = true;
    }

    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Use the new size directly: a ScrollViewer's ViewportHeight is not yet
        // updated when SizeChanged fires, so reading it here yields a stale value
        // and too little head-room, leaving the first lines stuck above the line.
        double viewport = e.NewSize.Height;
        if (viewport <= 0)
        {
            return;
        }

        double top = viewport * ReadingFraction;
        double bottom = viewport * (1.0 - ReadingFraction);

        // Line length is governed by the centered column (ColumnWidth); the
        // side padding is just breathing room inside it.
        Prompt.Padding = new Thickness(24, top, 24, bottom);

        // Re-center the current word after a resize.
        if (_currentRun is not null)
        {
            HighlightAndScroll(ViewModel.CurrentTokenIndex);
        }
    }

    private void ApplyMirror()
    {
        Scroller.RenderTransformOrigin = new Point(0.5, 0.5);
        Scroller.RenderTransform = ViewModel.MirrorHorizontal
            ? new ScaleTransform(-1, 1)
            : Transform.Identity;

        if (MirrorToggle.IsChecked != ViewModel.MirrorHorizontal)
        {
            MirrorToggle.IsChecked = ViewModel.MirrorHorizontal;
        }
    }

    // ----- Editor overlay -----

    private void OnEditScript(object sender, RoutedEventArgs e)
    {
        EditorPanel.Visibility = Visibility.Visible;
        EditorPanel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160)));

        var slide = new TranslateTransform(0, 14);
        EditorPanel.RenderTransform = slide;
        slide.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void OnDoneEditing(object sender, RoutedEventArgs e)
    {
        HideEditor();
        // Recompile through the view model so its ScriptModel stays in sync —
        // rendering from a stale model showed old text after the first Start.
        ViewModel.RebuildScript();
    }

    private void HideEditor() => EditorPanel.Visibility = Visibility.Collapsed;

    private void OnFontSmaller(object sender, RoutedEventArgs e)
        => ViewModel.FontSize = Math.Max(24, ViewModel.FontSize - 4);

    private void OnFontLarger(object sender, RoutedEventArgs e)
        => ViewModel.FontSize = Math.Min(120, ViewModel.FontSize + 4);

    // The mirror/topmost handlers serve two callers: the toolbar toggle itself
    // (state already flipped by the click) and the compact ☰ menu buttons
    // (plain buttons, so flip the state here and sync the toggle).
    private void OnToggleMirror(object sender, RoutedEventArgs e)
    {
        bool mirrored = ReferenceEquals(sender, MirrorToggle)
            ? MirrorToggle.IsChecked == true
            : !ViewModel.MirrorHorizontal;
        ViewModel.MirrorHorizontal = mirrored;
        MirrorToggle.IsChecked = mirrored;
        ApplyMirror();
    }

    private void OnToggleTopmost(object sender, RoutedEventArgs e)
    {
        bool onTop = ReferenceEquals(sender, TopmostToggle)
            ? TopmostToggle.IsChecked == true
            : !Topmost;
        Topmost = onTop;
        TopmostToggle.IsChecked = onTop;
    }

    /// <summary>Any button chosen in the ☰ menu closes it (combo clicks stay open).</summary>
    private void OnCompactMenuItemClicked(object sender, RoutedEventArgs e)
    {
        if (e.Source is Button)
        {
            MenuToggle.IsChecked = false;
        }
    }

    private const double CompactToolbarWidth = 760;

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        => ViewModel.IsCompactToolbar = e.NewSize.Width < CompactToolbarWidth;

    private void OnNavigateLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser/mail handler is not our problem to solve.
        }

        e.Handled = true;
    }

    // ----- Drag a script file (.docx / .txt) onto the window -----

    private static bool TryGetDroppedFile(DragEventArgs e, out string path)
    {
        path = string.Empty;
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            path = files[0];
            return true;
        }

        return false;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (TryGetDroppedFile(e, out _))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (TryGetDroppedFile(e, out string path))
        {
            ViewModel.LoadScriptFromPath(path);
            e.Handled = true;
        }
    }

    /// <summary>The picker opens over the window, so the settings popup must get out of the way.</summary>
    private void OnOpenLanguagePicker(object sender, RoutedEventArgs e) => SettingsToggle.IsChecked = false;

    private void OnSettingsPopupOpened(object sender, EventArgs e)
    {
        // Never taller than the window it opens over — scroll instead of
        // clipping options off-screen (the panel outgrew small windows).
        SettingsScroll.MaxHeight = Math.Clamp(ActualHeight - 150, 280, 640);
    }
}
