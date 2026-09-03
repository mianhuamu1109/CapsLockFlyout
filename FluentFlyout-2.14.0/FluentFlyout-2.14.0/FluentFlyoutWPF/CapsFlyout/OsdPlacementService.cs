using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FluentFlyoutWPF.CapsFlyout;

internal enum ConfirmatorPosition
{
    BottomCenter = 1,
    TopLeft = 2,
    TopCenter = 3
}

internal readonly record struct PixelPlacement(int X, int Y, int Width, int Height);

internal sealed class OsdPlacementService
{
    // Windows 11 HardwareConfirmator positions the visible 48-DIP card 12 DIP
    // inside the current usable edge. This is deliberately measured from the
    // card, not from a synthetic WPF drop-shadow box.
    internal const double EdgeMarginDip = 12;
    private const string PositionKey =
        @"Software\Microsoft\Windows\CurrentVersion\SystemSettings\ConfirmatorPosition";
    private long _nextPositionRead;

    internal ConfirmatorPosition CurrentPositionKind { get; private set; } = ConfirmatorPosition.BottomCenter;

    internal PixelPlacement Calculate(IntPtr hwnd, double widthDip, double heightDip)
    {
        // HardwareConfirmator itself targets the primary display. Keep the same
        // contract instead of following the active/foreground application.
        var monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.Point(0, 0),
            NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = NativeMethods.MonitorInfo.Create();
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return new PixelPlacement(0, 0, (int)Math.Round(widthDip), (int)Math.Round(heightDip));

        var dpi = NativeMethods.GetEffectiveDpi(monitor, hwnd);
        var scale = dpi / 96d;
        var width = Math.Max(1, (int)Math.Round(widthDip * scale));
        var height = Math.Max(1, (int)Math.Round(heightDip * scale));
        var margin = Math.Max(1, (int)Math.Round(EdgeMarginDip * scale));

        // rcWork is intentionally not used: Windhawk's auto-hide implementation
        // can leave rcWork equal to the full display even while the bar is shown.
        // The visible DWM taskbar frame is the source of truth instead.
        var available = ApplyVisibleTaskbar(info.Monitor, monitor, dpi);
        var now = Stopwatch.GetTimestamp();
        if (now >= _nextPositionRead)
        {
            CurrentPositionKind = ReadConfirmatorPosition();
            _nextPositionRead = now + Stopwatch.Frequency / 2;
        }

        int x;
        int y;
        switch (CurrentPositionKind)
        {
            case ConfirmatorPosition.TopLeft:
                x = available.Left + margin;
                y = available.Top + margin;
                break;
            case ConfirmatorPosition.TopCenter:
                x = available.Left + ((available.Width - width) / 2);
                y = available.Top + margin;
                break;
            default:
                x = available.Left + ((available.Width - width) / 2);
                y = available.Bottom - height - margin;
                break;
        }

        x = Math.Clamp(x, available.Left, Math.Max(available.Left, available.Right - width));
        y = Math.Clamp(y, available.Top, Math.Max(available.Top, available.Bottom - height));
        return new PixelPlacement(x, y, width, height);
    }

    private static NativeMethods.Rect ApplyVisibleTaskbar(
        NativeMethods.Rect monitorRect,
        IntPtr targetMonitor,
        uint dpi)
    {
        var result = monitorRect;
        var minimumVisibleThickness = Math.Max(3, (int)Math.Round(3d * dpi / 96d));
        // HardwareConfirmator targets the primary monitor, whose taskbar is
        // always Shell_TrayWnd.  Looking it up directly avoids enumerating and
        // querying every top-level window 120 times per second.
        var taskbarWindow = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbarWindow == IntPtr.Zero || !NativeMethods.IsWindowVisible(taskbarWindow) ||
            NativeMethods.MonitorFromWindow(taskbarWindow, NativeMethods.MONITOR_DEFAULTTONEAREST) != targetMonitor)
            return result;

        if (NativeMethods.DwmGetWindowAttribute(
                taskbarWindow,
                NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out var taskbar,
                Marshal.SizeOf<NativeMethods.Rect>()) != 0 &&
            !NativeMethods.GetWindowRect(taskbarWindow, out taskbar))
            return result;

        var intersection = Intersect(monitorRect, taskbar);
        if (intersection.Width <= 0 || intersection.Height <= 0)
            return result;

        if (intersection.Width >= monitorRect.Width / 2)
        {
            // Ignore the one/two-pixel activation strip of a hidden bar.
            if (intersection.Height < minimumVisibleThickness)
                return result;

            var atBottom = Math.Abs(taskbar.Bottom - monitorRect.Bottom) <=
                           Math.Abs(taskbar.Top - monitorRect.Top);
            if (atBottom)
                result.Bottom = Math.Min(result.Bottom, Math.Max(result.Top, taskbar.Top));
            else
                result.Top = Math.Max(result.Top, Math.Min(result.Bottom, taskbar.Bottom));
        }
        else if (intersection.Height >= monitorRect.Height / 2)
        {
            if (intersection.Width < minimumVisibleThickness)
                return result;

            var atRight = Math.Abs(taskbar.Right - monitorRect.Right) <=
                          Math.Abs(taskbar.Left - monitorRect.Left);
            if (atRight)
                result.Right = Math.Min(result.Right, Math.Max(result.Left, taskbar.Left));
            else
                result.Left = Math.Max(result.Left, Math.Min(result.Right, taskbar.Right));
        }

        return result;
    }

    private static NativeMethods.Rect Intersect(NativeMethods.Rect a, NativeMethods.Rect b) => new()
    {
        Left = Math.Max(a.Left, b.Left),
        Top = Math.Max(a.Top, b.Top),
        Right = Math.Min(a.Right, b.Right),
        Bottom = Math.Min(a.Bottom, b.Bottom)
    };

    private static ConfirmatorPosition ReadConfirmatorPosition()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PositionKey);
            var value = Convert.ToInt32(key?.GetValue("PositionIndex", 1));
            return Enum.IsDefined(typeof(ConfirmatorPosition), value)
                ? (ConfirmatorPosition)value
                : ConfirmatorPosition.BottomCenter;
        }
        catch
        {
            return ConfirmatorPosition.BottomCenter;
        }
    }
}
