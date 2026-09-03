using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class Program
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExNoActivate = 0x08000000;
    private const int SwShowNoActivate = 4;
    private const uint PmRemove = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        internal IntPtr Window;
        internal uint Id;
        internal UIntPtr WParam;
        internal IntPtr LParam;
        internal uint Time;
        internal int X;
        internal int Y;
        internal uint Private;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Message message, IntPtr window, uint minimum, uint maximum, uint remove);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    private static int Main(string[] args)
    {
        var window = CreateWindowEx(
            WsExTopmost | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate,
            "STATIC",
            "FloatingNativeWindow",
            WsPopup,
            1290,
            1400,
            300,
            100,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        if (window == IntPtr.Zero)
            return Marshal.GetLastWin32Error();

        var watch = Stopwatch.StartNew();
        _ = ShowWindow(window, SwShowNoActivate);
        var lastVisibleAt = -1L;
        var duration = args.Length > 0 && int.TryParse(args[0], out var requestedDuration)
            ? requestedDuration
            : 1000;
        while (watch.ElapsedMilliseconds < duration)
        {
            while (PeekMessage(out var message, IntPtr.Zero, 0, 0, PmRemove))
                _ = DispatchMessage(ref message);
            if (IsWindowVisible(window))
                lastVisibleAt = watch.ElapsedMilliseconds;
            Thread.Sleep(1);
        }

        Console.WriteLine($"last-visible-ms={lastVisibleAt}");
        _ = DestroyWindow(window);
        return lastVisibleAt < 50 ? 0 : 1;
    }
}
