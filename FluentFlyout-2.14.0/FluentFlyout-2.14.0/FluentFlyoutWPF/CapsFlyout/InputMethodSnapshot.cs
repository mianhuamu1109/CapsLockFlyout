using System.Runtime.InteropServices;

namespace FluentFlyoutWPF.CapsFlyout;

// Read-only port of ReadForegroundSnapshot / QueryLegacyIme in the supplied
// keyboard-ime-fluent-indicator-v2.wh.cpp (MIT). Keep the full conversion bits.
internal readonly record struct InputMethodSnapshot(
    nint Foreground, nint Focus, nint Layout, bool? Open, uint? Conversion)
{
    internal bool Valid => Foreground != 0 && Layout != 0;
    internal int PrimaryLanguage => (int)((long)Layout & 0x3ff);
    internal bool Japanese => PrimaryLanguage == 0x11;
    internal string LanguageName => PrimaryLanguage switch
    {
        0x04 => "汉语", 0x09 => "英语", 0x11 => "日语", _ => "其他"
    };
    internal string ModeName => PrimaryLanguage switch
    {
        0x09 => "直接输入",
        0x04 when Open == false => "英语模式",
        0x11 when Open == false => "直接输入",
        0x04 when Open == true && Conversion.HasValue =>
            (Conversion.Value & 1) != 0 ? "中文模式" : "英语模式",
        0x11 when Open == true && Conversion.HasValue =>
            (Conversion.Value & 1) == 0 ? "直接输入" :
            (Conversion.Value & 2) != 0 ? "片假名" : "平假名",
        _ => "状态未知"
    };
    internal bool Known => ModeName != "状态未知";
    internal bool SameDisplay(InputMethodSnapshot other) =>
        Layout == other.Layout && ModeName == other.ModeName;

    internal OsdMessage ToMessage() =>
        new("\uE765", LanguageName, ModeName,
            FixedDetailReference: "中文模式", CenterDetail: true,
            InputMethod: true);
}

internal static class InputMethodStateReader
{
    internal static InputMethodSnapshot Read()
    {
        var foreground = ImeNative.GetForegroundWindow();
        if (foreground == 0) return default;
        var thread = NativeMethods.GetWindowThreadProcessId(foreground, out var pid);
        // Our settings/preview windows must not become the queried input target.
        if (thread == 0 || pid == Environment.ProcessId) return default;
        var gui = new ImeNative.GuiThreadInfo { Size = Marshal.SizeOf<ImeNative.GuiThreadInfo>() };
        var focus = ImeNative.GetGUIThreadInfo(thread, ref gui) && gui.Focus != 0
            ? gui.Focus : foreground;
        var layout = ImeNative.GetKeyboardLayout(thread);
        bool? open = null;
        uint? conversion = null;
        if (((long)layout & 0x3ff) != 0x09)
        {
            NativeMethods.GetWindowThreadProcessId(focus, out var focusPid);
            if (focusPid == Environment.ProcessId)
            {
                var context = ImeNative.ImmGetContext(focus);
                if (context != 0)
                {
                    try
                    {
                        open = ImeNative.ImmGetOpenStatus(context);
                        if (ImeNative.ImmGetConversionStatus(context, out var value, out _))
                            conversion = value;
                    }
                    finally { ImeNative.ImmReleaseContext(focus, context); }
                }
            }
            else
            {
                var ime = ImeNative.ImmGetDefaultIMEWnd(focus);
                if (ime != 0)
                {
                    if (Query(ime, 5, out var value)) open = value != 0;
                    if (Query(ime, 1, out value)) conversion = (uint)value;
                }
            }
        }
        // Discard a sample taken across an application/layout change.
        if (foreground != ImeNative.GetForegroundWindow() ||
            layout != ImeNative.GetKeyboardLayout(thread)) return default;
        var after = new ImeNative.GuiThreadInfo { Size = Marshal.SizeOf<ImeNative.GuiThreadInfo>() };
        if (ImeNative.GetGUIThreadInfo(thread, ref after) &&
            after.Focus != 0 && after.Focus != focus) return default;
        return new(foreground, focus, layout, open, conversion);
    }

    private static bool Query(nint ime, nuint command, out nuint value) =>
        ImeNative.SendMessageTimeout(ime, 0x0283, command, 0,
            0x0001 | 0x0002, 50, out value) != 0;
}

internal static class ImeNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        internal int Size;
        internal uint Flags;
        internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        internal NativeMethods.Rect CaretRect;
    }
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    internal static extern nint SendMessageTimeout(nint window, uint message, nuint wParam,
        nint lParam, uint flags, uint timeout, out nuint result);
    [DllImport("imm32.dll")] internal static extern nint ImmGetDefaultIMEWnd(nint window);
    [DllImport("imm32.dll")] internal static extern nint ImmGetContext(nint window);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ImmGetOpenStatus(nint context);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ImmGetConversionStatus(nint context, out uint conversion, out uint sentence);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ImmReleaseContext(nint window, nint context);
}
