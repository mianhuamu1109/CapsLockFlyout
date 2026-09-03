using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace FluentFlyoutWPF.CapsFlyout;

internal static class NativeWindowStyler
{
    private const uint AcrylicOpacity = 175;

    internal static void Apply(IntPtr hwnd)
    {
        var oldStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        var newStyle = oldStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        _ = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(newStyle));

        // Let DWM own the rounded outline and its shadow as one geometry. An
        // extra WPF border over the full-window acrylic produces doubled,
        // uneven antialiasing around the corners.
        var round = 2;                 // DWMWCP_ROUND (8-DIP overlay radius)
        var noSystemBackdrop = 1;      // DWMSBT_NONE
        var defaultDwmBorder = unchecked((int)0xFFFFFFFF); // DWMWA_COLOR_DEFAULT
        _ = NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref round,
            Marshal.SizeOf<int>());
        _ = NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE,
            ref noSystemBackdrop,
            Marshal.SizeOf<int>());
        _ = NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_BORDER_COLOR,
            ref defaultDwmBorder,
            Marshal.SizeOf<int>());

        var glass = new NativeMethods.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        _ = NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref glass);
        UpdateTheme(hwnd, !IsLightTheme());
    }

    internal static bool UpdateTheme(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var value = dark ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref value,
            Marshal.SizeOf<int>());

        var transparencyEnabled = IsTransparencyEnabled();
        ApplyAcrylic(hwnd, dark, transparencyEnabled);
        return transparencyEnabled;
    }

    internal static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyAcrylic(IntPtr hwnd, bool dark, bool enabled)
    {
        // Values mirror the Windows 11 default in-app acrylic palette.  The
        // alpha is the FluentFlyout acrylic strength; the accent policy also
        // contributes the native blur and noise layers missing from a plain
        // DWMSBT_TRANSIENTWINDOW backdrop.
        var tint = dark ? 0x002C2C2Cu : 0x00FCFCFCu;
        var policy = new NativeMethods.AccentPolicy
        {
            State = enabled
                ? NativeMethods.AccentState.AcrylicBlurBehind
                : NativeMethods.AccentState.Disabled,
            GradientColor = (AcrylicOpacity << 24) | tint
        };

        var size = Marshal.SizeOf<NativeMethods.AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new NativeMethods.WindowCompositionAttributeData
            {
                Attribute = NativeMethods.WCA_ACCENT_POLICY,
                Data = pointer,
                SizeOfData = size
            };
            _ = NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static bool IsTransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("EnableTransparency", 1)) != 0;
        }
        catch
        {
            return true;
        }
    }
}
