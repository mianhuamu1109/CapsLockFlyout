using FluentFlyoutWPF.CapsFlyout;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace FluentFlyoutWPF;

public partial class MainWindow : Window, IDisposable
{
    private const double EnterDurationMilliseconds = 250;
    private const double ExitDurationMilliseconds = 83;
    private const double InitialFlyoutWidthDip = 192;
    private const double MinimumFlyoutWidthDip = 129;
    private const double MaximumFlyoutWidthDip = 280;
    private const double MinimumProgressFlyoutWidthDip = 168;
    private const double HorizontalChromeDip = 54;
    private const double FlyoutHeightDip = 48;
    private static readonly Duration StateAnimationDuration =
        new(TimeSpan.FromMilliseconds(167));

    private enum MotionPhase
    {
        None,
        Entering,
        Exiting
    }

    private readonly DispatcherTimer _hideTimer;
    private readonly System.Threading.Timer _animationTimer;
    private readonly object _motionGate = new();
    private readonly OsdPlacementService _placement = new();
    private readonly CapsLockMonitor _capsLockMonitor = new();
    private readonly GHelperMonitor _gHelperMonitor = new();
    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _isFlyoutVisible;
    private volatile bool _disposed;
    private double _progress;
    private double _flyoutWidthDip = InitialFlyoutWidthDip;
    private readonly Stopwatch _motionClock = new();
    private MotionPhase _motionPhase;
    private double _motionStartOffset;
    private double _currentMotionOffset;
    private bool _highResolutionTimerActive;
    private bool _animationTimerRunning;
    private int _animationCallbackActive;
    private OsdStateVisual _lastStateVisual;
    private bool? _lastStateEnabled;

    public MainWindow()
    {
        InitializeComponent();

        _hideTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(2000)
        };
        _hideTimer.Tick += (_, _) => StartHideAnimation();

        // A WPF DispatcherTimer is throttled when this no-activate window is
        // otherwise idle. Drive the HWND directly from a high-resolution
        // ThreadPool timer so 120 Hz means an actual 8.33 ms cadence.
        _animationTimer = new System.Threading.Timer(
            _ => OnAnimationTick(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        ProgressHost.SizeChanged += (_, _) => UpdateProgressWidth();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        if (_source?.CompositionTarget is not null)
            _source.CompositionTarget.BackgroundColor = Colors.Transparent;

        _source?.AddHook(WindowMessageHook);
        NativeWindowStyler.Apply(_hwnd);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyTheme();
        _capsLockMonitor.StateChanged += OnCapsLockChanged;
        _gHelperMonitor.MessageRaised += ShowMessage;
        _gHelperMonitor.SessionReset += OnSessionReset;
        _capsLockMonitor.Start();
        _gHelperMonitor.Start();

    }

    internal void ShowPreview()
    {
        var isOn = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) != 0;
        ShowMessage(OsdMessage.WithState(
            "\uE72E",
            "大写锁定",
            isOn ? "已开启" : "已关闭",
            isOn,
            OsdStateVisual.Lock));
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    private void OnCapsLockChanged(bool isOn)
    {
        ShowMessage(OsdMessage.WithState(
            "\uE72E",
            "大写锁定",
            isOn ? "已开启" : "已关闭",
            isOn,
            OsdStateVisual.Lock));
    }

    private void OnSessionReset()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(OnSessionReset));
            return;
        }

        HideImmediately();
    }

    private void ShowMessage(OsdMessage message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowMessage(message));
            return;
        }

        ApplyTheme();
        ApplyMessageVisual(message);
        TitleText.Text = message.Title;
        DetailText.Text = message.Detail;
        DetailText.HorizontalAlignment = message.CenterDetail
            ? System.Windows.HorizontalAlignment.Center
            : System.Windows.HorizontalAlignment.Left;
        DetailText.TextAlignment = message.CenterDetail
            ? System.Windows.TextAlignment.Center
            : System.Windows.TextAlignment.Left;
        _progress = Math.Clamp(message.Progress ?? 0, 0, 1);
        ProgressHost.Visibility = message.Progress.HasValue ? Visibility.Visible : Visibility.Collapsed;
        UpdateFlyoutWidth(message);
        UpdateProgressWidth();

        lock (_motionGate)
        {
            var target = _placement.Calculate(_hwnd, _flyoutWidthDip, FlyoutHeightDip);
            if (!_isFlyoutVisible)
            {
                _motionStartOffset = MotionDirection * MotionTravelDistance(target);
                _currentMotionOffset = _motionStartOffset;
                PositionWindow(target, _currentMotionOffset, show: false);
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
                _isFlyoutVisible = true;
                _motionPhase = MotionPhase.Entering;
                _motionClock.Restart();
            }
            else if (_motionPhase == MotionPhase.Exiting)
            {
                // Reverse an interrupted exit from its current physical position,
                // avoiding a jump when another hardware key is pressed rapidly.
                _motionStartOffset = _currentMotionOffset;
                _motionPhase = MotionPhase.Entering;
                _motionClock.Restart();
            }

            RepositionCore();
            StartRendering();
        }

        _hideTimer.Stop();
        _hideTimer.Interval = message.Duration ?? TimeSpan.FromMilliseconds(2000);
        _hideTimer.Start();
    }

    private void ApplyTheme()
    {
        var isLight = NativeWindowStyler.IsLightTheme();
        var foreground = isLight ? Color.FromArgb(0xE4, 0, 0, 0) : Colors.White;
        var track = isLight ? Color.FromArgb(0x0F, 0, 0, 0) : Color.FromArgb(0x0B, 255, 255, 255);
        var accent = SystemParameters.WindowGlassColor;

        Foreground = new SolidColorBrush(foreground);
        var transparencyEnabled = NativeWindowStyler.UpdateTheme(_hwnd, !isLight);
        Surface.Background = transparencyEnabled
            ? Brushes.Transparent
            : new SolidColorBrush(isLight ? Color.FromRgb(0xF9, 0xF9, 0xF9) : Color.FromRgb(0x2C, 0x2C, 0x2C));
        ProgressTrack.Background = new SolidColorBrush(track);
        ProgressFill.Background = new SolidColorBrush(accent);
        StateIndicator.Background = new SolidColorBrush(accent);
    }

    private void ApplyMessageVisual(OsdMessage message)
    {
        IconText.Text = message.Glyph;
        var isLock = message.StateVisual == OsdStateVisual.Lock;
        LockSymbol.Visibility = isLock ? Visibility.Visible : Visibility.Collapsed;
        IconText.Visibility = isLock ? Visibility.Collapsed : Visibility.Visible;
        StateIndicator.Visibility = message.IsEnabled.HasValue
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (message.IsEnabled is bool isEnabled)
        {
            AnimateState(message.StateVisual, isEnabled);
            _lastStateVisual = message.StateVisual;
            _lastStateEnabled = isEnabled;
        }
        else
        {
            ResetStateAnimations();
            _lastStateVisual = OsdStateVisual.None;
            _lastStateEnabled = null;
        }
    }

    private void AnimateState(OsdStateVisual visual, bool isEnabled)
    {
        var newVisual = _lastStateVisual != visual || !_lastStateEnabled.HasValue;
        if (newVisual)
        {
            ResetStateAnimations();
            SetStateBaseValues(visual, !isEnabled);
        }

        var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        StateIndicator.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = isEnabled ? 1 : 0.2,
            Duration = StateAnimationDuration,
            EasingFunction = easeOut
        }, HandoffBehavior.SnapshotAndReplace);
        StateIndicator.BeginAnimation(WidthProperty, new DoubleAnimation
        {
            To = isEnabled ? 60 : 36,
            Duration = StateAnimationDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);

        var cubicOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (visual == OsdStateVisual.Lock)
        {
            ShackleRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation
            {
                To = isEnabled ? 0 : 25,
                Duration = StateAnimationDuration,
                EasingFunction = cubicOut
            }, HandoffBehavior.SnapshotAndReplace);
            BeginBounce(ShackleBounce);
        }
        else
        {
            IconText.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                To = isEnabled ? 1 : 0.52,
                Duration = StateAnimationDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
            GenericIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation
            {
                To = isEnabled ? 1 : 0.88,
                Duration = StateAnimationDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
            GenericIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
            {
                To = isEnabled ? 1 : 0.88,
                Duration = StateAnimationDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
            BeginBounce(GenericIconBounce);
        }
    }

    private static void BeginBounce(TranslateTransform transform)
    {
        var bounce = new DoubleAnimationUsingKeyFrames
        {
            Duration = StateAnimationDuration
        };
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(
            1,
            KeyTime.FromPercent(0.1),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(
            0,
            KeyTime.FromPercent(1),
            new CubicEase { EasingMode = EasingMode.EaseInOut }));
        transform.BeginAnimation(TranslateTransform.YProperty, bounce, HandoffBehavior.SnapshotAndReplace);
    }

    private void SetStateBaseValues(OsdStateVisual visual, bool isEnabled)
    {
        StateIndicator.Opacity = isEnabled ? 1 : 0.2;
        StateIndicator.Width = isEnabled ? 60 : 36;
        if (visual == OsdStateVisual.Lock)
        {
            ShackleRotation.Angle = isEnabled ? 0 : 25;
            ShackleBounce.Y = 0;
        }
        else
        {
            IconText.Opacity = isEnabled ? 1 : 0.52;
            GenericIconScale.ScaleX = isEnabled ? 1 : 0.88;
            GenericIconScale.ScaleY = isEnabled ? 1 : 0.88;
            GenericIconBounce.Y = 0;
        }
    }

    private void ResetStateAnimations()
    {
        StateIndicator.BeginAnimation(OpacityProperty, null);
        StateIndicator.BeginAnimation(WidthProperty, null);
        ShackleRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        ShackleBounce.BeginAnimation(TranslateTransform.YProperty, null);
        IconText.BeginAnimation(OpacityProperty, null);
        GenericIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        GenericIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        GenericIconBounce.BeginAnimation(TranslateTransform.YProperty, null);

        StateIndicator.Opacity = 1;
        StateIndicator.Width = 60;
        ShackleRotation.Angle = 0;
        ShackleBounce.Y = 0;
        IconText.Opacity = 1;
        GenericIconScale.ScaleX = 1;
        GenericIconScale.ScaleY = 1;
        GenericIconBounce.Y = 0;
    }

    private void UpdateProgressWidth()
    {
        ProgressFill.Width = Math.Max(0, ProgressTrack.ActualWidth * _progress);
    }

    private void UpdateFlyoutWidth(OsdMessage message)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (!string.IsNullOrEmpty(message.FixedDetailReference))
        {
            var fixedTitleWidth = Math.Min(
                TitleText.MaxWidth,
                MeasureTextWidth(TitleText, message.Title, pixelsPerDip));
            var fixedDetailWidth = MeasureTextWidth(
                DetailText,
                message.FixedDetailReference,
                pixelsPerDip);
            var fixedTextGap = string.IsNullOrWhiteSpace(message.FixedDetailReference)
                ? 0
                : DetailText.Margin.Left;

            _flyoutWidthDip = Math.Clamp(
                Math.Ceiling(HorizontalChromeDip + fixedTitleWidth + fixedTextGap + fixedDetailWidth),
                MinimumFlyoutWidthDip,
                MaximumFlyoutWidthDip);
            Width = _flyoutWidthDip;
            UpdateLayout();
            return;
        }

        if (message.FixedWidth is double fixedWidth)
        {
            _flyoutWidthDip = Math.Clamp(
                fixedWidth,
                MinimumFlyoutWidthDip,
                MaximumFlyoutWidthDip);
            Width = _flyoutWidthDip;
            UpdateLayout();
            return;
        }

        var titleWidth = Math.Min(TitleText.MaxWidth, MeasureTextWidth(TitleText, message.Title, pixelsPerDip));
        var detailWidth = MeasureTextWidth(DetailText, message.Detail, pixelsPerDip);
        var textGap = string.IsNullOrWhiteSpace(message.Detail) ? 0 : DetailText.Margin.Left;
        var desiredWidth = HorizontalChromeDip + titleWidth + textGap + detailWidth + 2;

        if (message.Progress.HasValue)
            desiredWidth = Math.Max(desiredWidth, MinimumProgressFlyoutWidthDip);

        _flyoutWidthDip = Math.Clamp(
            Math.Ceiling(desiredWidth),
            MinimumFlyoutWidthDip,
            MaximumFlyoutWidthDip);

        Width = _flyoutWidthDip;
        UpdateLayout();
    }

    private static double MeasureTextWidth(TextBlock textBlock, string text, double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var typeface = new Typeface(
            textBlock.FontFamily,
            textBlock.FontStyle,
            textBlock.FontWeight,
            textBlock.FontStretch);
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            textBlock.FlowDirection,
            typeface,
            textBlock.FontSize,
            Brushes.Black,
            pixelsPerDip);
        return formattedText.WidthIncludingTrailingWhitespace;
    }

    private void OnAnimationTick()
    {
        if (_disposed || Interlocked.Exchange(ref _animationCallbackActive, 1) != 0)
            return;

        try
        {
            lock (_motionGate)
            {
                if (!_disposed)
                    RepositionCore();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _animationCallbackActive, 0);
        }
    }

    private void RepositionCore()
    {
        if (_hwnd == IntPtr.Zero || !_isFlyoutVisible)
            return;

        var target = _placement.Calculate(_hwnd, _flyoutWidthDip, FlyoutHeightDip);
        var elapsed = _motionClock.Elapsed.TotalMilliseconds;

        if (_motionPhase == MotionPhase.Entering)
        {
            var progress = Math.Clamp(elapsed / EnterDurationMilliseconds, 0, 1);
            _currentMotionOffset = _motionStartOffset * (1 - CubicBezier(progress, 0, 0, 0, 1));
            if (progress >= 1)
            {
                _motionPhase = MotionPhase.None;
                _currentMotionOffset = 0;
                _motionClock.Stop();
            }
        }
        else if (_motionPhase == MotionPhase.Exiting)
        {
            var progress = Math.Clamp(elapsed / ExitDurationMilliseconds, 0, 1);
            _currentMotionOffset = MotionDirection * MotionTravelDistance(target) *
                                   CubicBezier(progress, 1, 0, 1, 1);
            if (progress >= 1)
            {
                CompleteHide();
                return;
            }
        }
        else
        {
            _currentMotionOffset = 0;
        }

        PositionWindow(target, _currentMotionOffset, show: true);
    }

    private int MotionDirection =>
        _placement.CurrentPositionKind == ConfirmatorPosition.BottomCenter ? 1 : -1;

    private double MotionTravelDistance(PixelPlacement placement)
    {
        // Include the native edge gap so the DWM shadow also clears the screen.
        var scale = placement.Width / _flyoutWidthDip;
        return placement.Height + Math.Round(OsdPlacementService.EdgeMarginDip * scale);
    }

    private void PositionWindow(PixelPlacement placement, double offsetY, bool show)
    {
        var commonFlags = NativeMethods.SWP_NOACTIVATE |
                          NativeMethods.SWP_ASYNCWINDOWPOS |
                          (show ? NativeMethods.SWP_SHOWWINDOW : 0);

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            placement.X,
            placement.Y + (int)Math.Round(offsetY),
            placement.Width,
            placement.Height,
            commonFlags);
    }

    private void StartHideAnimation()
    {
        _hideTimer.Stop();
        lock (_motionGate)
        {
            if (!_isFlyoutVisible)
                return;

            _motionPhase = MotionPhase.Exiting;
            _currentMotionOffset = 0;
            _motionClock.Restart();
            StartRendering();
        }
    }

    private void CompleteHide()
    {
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
        _isFlyoutVisible = false;
        _motionPhase = MotionPhase.None;
        _currentMotionOffset = 0;
        _motionClock.Stop();
        StopRendering();
    }

    private void StartRendering()
    {
        if (_animationTimerRunning)
            return;

        if (!_highResolutionTimerActive)
        {
            _highResolutionTimerActive = NativeMethods.TimeBeginPeriod(1) == 0;
        }
        _animationTimerRunning = true;
        _ = _animationTimer.Change(
            TimeSpan.Zero,
            TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 120));
    }

    private void StopRendering()
    {
        if (_animationTimerRunning)
        {
            _ = _animationTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _animationTimerRunning = false;
        }
        if (_highResolutionTimerActive)
        {
            _ = NativeMethods.TimeEndPeriod(1);
            _highResolutionTimerActive = false;
        }
    }

    private static double CubicBezier(double x, double x1, double y1, double x2, double y2)
    {
        // CSS/Fluent cubic-bezier curves describe y as a function of x, so
        // first solve the Bezier parameter for the requested time fraction.
        var low = 0d;
        var high = 1d;
        for (var i = 0; i < 18; i++)
        {
            var parameter = (low + high) / 2;
            var curveX = BezierCoordinate(parameter, x1, x2);
            if (curveX < x)
                low = parameter;
            else
                high = parameter;
        }

        return BezierCoordinate((low + high) / 2, y1, y2);
    }

    private static double BezierCoordinate(double t, double control1, double control2)
    {
        var inverse = 1 - t;
        return 3 * inverse * inverse * t * control1 +
               3 * inverse * t * t * control2 +
               t * t * t;
    }

    public void HideImmediately()
    {
        _hideTimer.Stop();
        lock (_motionGate)
        {
            StopRendering();
            _motionClock.Stop();
            _motionPhase = MotionPhase.None;
            _currentMotionOffset = 0;
            if (_hwnd != IntPtr.Zero)
                NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
            _isFlyoutVisible = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _hideTimer.Stop();
        lock (_motionGate)
        {
            _disposed = true;
            StopRendering();
        }
        _animationTimer.Dispose();
        _capsLockMonitor.Dispose();
        _gHelperMonitor.SessionReset -= OnSessionReset;
        _gHelperMonitor.Dispose();
        if (_source is not null)
        {
            _source.RemoveHook(WindowMessageHook);
            _source = null;
        }
    }
}
