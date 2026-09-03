using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Windows.Automation;

Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
var inspectCapsWindow = args.Contains("--caps-window", StringComparer.OrdinalIgnoreCase);
var stimulateNativeOsd = !args.Contains("--no-key", StringComparer.OrdinalIgnoreCase) && !inspectCapsWindow;
var stimulateCapsLock = args.Contains("--caps", StringComparer.OrdinalIgnoreCase);

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
         directory is not null;
         directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "README-CapsLockFlyout.md")))
            return directory.FullName;
    }

    throw new DirectoryNotFoundException("Could not locate the CapsLockFlyout repository root.");
}

if (stimulateNativeOsd)
{
    if (stimulateCapsLock)
    {
        Native.keybd_event(Native.VK_CAPITAL, 0, 0, UIntPtr.Zero);
        Native.keybd_event(Native.VK_CAPITAL, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
    else
    {
        var foreground = Native.GetForegroundWindow();
        Native.SendMessage(foreground, Native.WM_APPCOMMAND, foreground, new IntPtr(Native.APPCOMMAND_VOLUME_UP << 16));
    }
}
Thread.Sleep(120);

var monitor = Native.MonitorFromPoint(new Point(0, 0), 1);
var monitorInfo = MonitorInfo.Create();
Native.GetMonitorInfo(monitor, ref monitorInfo);

void PrintWindow(IntPtr hwnd, string hierarchy)
{
    if (!Native.GetWindowRect(hwnd, out var rect))
        return;

    Native.GetWindowThreadProcessId(hwnd, out var processId);
    string processName;
    try { processName = Process.GetProcessById((int)processId).ProcessName; }
    catch { processName = "?"; }

    var className = new StringBuilder(256);
    Native.GetClassName(hwnd, className, className.Capacity);
    var title = new StringBuilder(256);
    Native.GetWindowText(hwnd, title, title.Capacity);
    var extended = rect;
    Native.DwmGetWindowAttribute(hwnd, 9, out extended, Marshal.SizeOf<Rect>());
    Native.DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int));

    var bottomGap = monitorInfo.Monitor.Bottom - extended.Bottom;
    var centerDelta = Math.Abs((extended.Left + extended.Right) / 2 -
                               (monitorInfo.Monitor.Left + monitorInfo.Monitor.Right) / 2);
    var nearCandidate = centerDelta <= 400 && extended.Top >= monitorInfo.Monitor.Top + monitorInfo.Monitor.Bottom / 2;
    var shellWindow = processName.Equals("CapsLockFlyout", StringComparison.OrdinalIgnoreCase) ||
                      processName.Contains("Shell", StringComparison.OrdinalIgnoreCase) ||
                      processName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                      className.ToString().Contains("CoreWindow", StringComparison.OrdinalIgnoreCase) ||
                      className.ToString().Contains("Xaml", StringComparison.OrdinalIgnoreCase);
    if (nearCandidate || shellWindow)
    {
        Console.WriteLine($"{hierarchy} 0x{hwnd:X} {processName} visible={Native.IsWindowVisible(hwnd)} cloaked={cloaked} " +
                          $"class={className} title={title} " +
                          $"win={rect.Left},{rect.Top},{rect.Right},{rect.Bottom} " +
                          $"dwm={extended.Left},{extended.Top},{extended.Right},{extended.Bottom} " +
                          $"size={extended.Right - extended.Left}x{extended.Bottom - extended.Top} " +
                          $"bottomGap={bottomGap} centerDelta={centerDelta}");
    }
}

if (stimulateCapsLock || inspectCapsWindow)
{
    var capsPids = Process.GetProcessesByName("CapsLockFlyout").Select(process => (uint)process.Id).ToHashSet();
    Native.EnumWindows((hwnd, _) =>
    {
        Native.GetWindowThreadProcessId(hwnd, out var processId);
        if (capsPids.Contains(processId))
        {
            PrintWindow(hwnd, "caps");
            var assemblyPath = Path.Combine(
                FindRepositoryRoot(), "dist", "CapsLockFlyout", "CapsLockFlyout.dll");
            var assembly = Assembly.LoadFrom(assemblyPath);
            var placementType = assembly.GetType("FluentFlyoutWPF.CapsFlyout.OsdPlacementService")!;
            var placementService = Activator.CreateInstance(placementType, nonPublic: true)!;
            var calculate = placementType.GetMethod("Calculate", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Console.WriteLine("calculated=" + calculate.Invoke(placementService, [hwnd, 192d, 48d]));
        }
        return true;
    }, IntPtr.Zero);
    if (stimulateCapsLock)
    {
        Native.keybd_event(Native.VK_CAPITAL, 0, 0, UIntPtr.Zero);
        Native.keybd_event(Native.VK_CAPITAL, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
    return;
}

Native.EnumWindows((hwnd, _) =>
{
    PrintWindow(hwnd, "top");
    Native.EnumChildWindows(hwnd, (child, _) =>
    {
        PrintWindow(child, "child");
        return true;
    }, IntPtr.Zero);
    return true;
}, IntPtr.Zero);

Console.WriteLine("UIA:");
var elements = AutomationElement.RootElement.FindAll(TreeScope.Descendants, Condition.TrueCondition);
foreach (AutomationElement element in elements)
{
    try
    {
        var bounds = element.Current.BoundingRectangle;
        if (bounds.IsEmpty || element.Current.IsOffscreen || bounds.Width > 800 || bounds.Height > 400)
            continue;
        var centerDelta = Math.Abs(bounds.Left + bounds.Width / 2 -
                                   (monitorInfo.Monitor.Left + monitorInfo.Monitor.Right) / 2d);
        if (centerDelta > 500 || bounds.Bottom < monitorInfo.Monitor.Bottom / 2d)
            continue;
        Console.WriteLine($"uia pid={element.Current.ProcessId} type={element.Current.ControlType.ProgrammaticName} " +
                          $"name={element.Current.Name} id={element.Current.AutomationId} class={element.Current.ClassName} " +
                          $"bounds={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom} " +
                          $"bottomGap={monitorInfo.Monitor.Bottom - bounds.Bottom}");
    }
    catch (Exception)
    {
    }
}

if (stimulateNativeOsd)
{
    Thread.Sleep(250);
    var foreground = Native.GetForegroundWindow();
    Native.SendMessage(foreground, Native.WM_APPCOMMAND, foreground, new IntPtr(Native.APPCOMMAND_VOLUME_DOWN << 16));
}

internal static class Native
{
    internal const byte VK_VOLUME_UP = 0xAF;
    internal const byte VK_VOLUME_DOWN = 0xAE;
    internal const byte VK_CAPITAL = 0x14;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint WM_APPCOMMAND = 0x0319;
    internal const int APPCOMMAND_VOLUME_UP = 10;
    internal const int APPCOMMAND_VOLUME_DOWN = 9;

    internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hwnd, StringBuilder title, int maxCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}

[StructLayout(LayoutKind.Sequential)]
internal struct Point
{
    internal int X;
    internal int Y;
    internal Point(int x, int y) { X = x; Y = y; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MonitorInfo
{
    internal int Size;
    internal Rect Monitor;
    internal Rect Work;
    internal uint Flags;

    internal static MonitorInfo Create() => new() { Size = Marshal.SizeOf<MonitorInfo>() };
}
