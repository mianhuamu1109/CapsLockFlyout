using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentFlyoutWPF.CapsFlyout;

internal static partial class Program
{
    private sealed class LiveEditor : Form
    {
        private readonly TextBox _editor = new() { Multiline = true, Dock = DockStyle.Fill, ImeMode = ImeMode.NoControl };
        internal LiveEditor(string id)
        {
            Text = "CapsLockFlyout 1.1.1 window test " + id;
            Width = 440; Height = 180;
            Controls.Add(_editor);
            Shown += (_, _) => _editor.Focus();
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x8001)
            {
                _editor.Focus();
                LiveNative.ActivateKeyboardLayout(message.LParam, 0);
                return;
            }
            if (message.Msg == 0x8002)
            {
                var context = ImeNative.ImmGetContext(_editor.Handle);
                if (context != 0)
                {
                    try
                    {
                        LiveNative.ImmSetOpenStatus(context, message.WParam != 0);
                        if (message.WParam != 0)
                            LiveNative.ImmSetConversionStatus(context, (uint)message.LParam, 0);
                    }
                    finally { ImeNative.ImmReleaseContext(_editor.Handle, context); }
                }
                return;
            }
            base.WndProc(ref message);
        }
    }

    private static void RunLiveTests()
    {
        var originalForeground = ImeNative.GetForegroundWindow();
        var editors = new List<Process>();
        using var monitor = new InputMethodMonitor();
        var messages = new ConcurrentQueue<OsdMessage>();
        monitor.MessageRaised += messages.Enqueue;
        try
        {
            nint StartEditor(string id)
            {
                var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--editor " + id) { UseShellExecute = false })!;
                editors.Add(process);
                nint handle = 0;
                for (var attempt = 0; attempt < 60 && handle == 0; attempt++)
                {
                    NativeMethods.EnumWindows((window, _) =>
                    {
                        NativeMethods.GetWindowThreadProcessId(window, out var pid);
                        if (pid == process.Id && NativeMethods.IsWindowVisible(window) &&
                            NativeMethods.GetWindowTitle(window) == "CapsLockFlyout 1.1.1 window test " + id)
                            handle = window;
                        return true;
                    }, 0);
                    if (handle == 0) Thread.Sleep(100);
                }
                if (handle == 0) throw new InvalidOperationException("Test editor did not start.");
                return handle;
            }
            void Focus(nint window)
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    LiveNative.SetForegroundWindow(window);
                    Thread.Sleep(40);
                    if (ImeNative.GetForegroundWindow() == window) return;
                }
                // Background test runners may not be allowed to activate windows.
                // Click only our verified test title bar; never change system focus policy.
                var cursor = Cursor.Position;
                try
                {
                    LiveNative.SetWindowPos(window, -1, 0, 0, 0, 0, 0x13);
                    Thread.Sleep(80);
                    if (NativeMethods.GetWindowRect(window, out var rectangle))
                    {
                        var point = new System.Drawing.Point(rectangle.Left + 120, rectangle.Top + 15);
                        if (LiveNative.GetAncestor(LiveNative.WindowFromPoint(point), 2) == window)
                        {
                            Cursor.Position = point;
                            LiveNative.mouse_event(0x0002, 0, 0, 0, 0);
                            LiveNative.mouse_event(0x0004, 0, 0, 0, 0);
                            Thread.Sleep(100);
                        }
                    }
                }
                finally
                {
                    LiveNative.SetWindowPos(window, -2, 0, 0, 0, 0, 0x13);
                    Cursor.Position = cursor;
                }
                if (ImeNative.GetForegroundWindow() == window) return;
                var actual = ImeNative.GetForegroundWindow();
                throw new InvalidOperationException($"Cannot focus test window {window:X}; actual={actual:X} ({NativeMethods.GetWindowTitle(actual)}); visible={NativeMethods.IsWindowVisible(window)}. Live test is inconclusive.");
            }
            void WaitState(nint window, string language, string? mode = null)
            {
                for (var attempt = 0; attempt < 40; attempt++)
                {
                    var state = InputMethodStateReader.Read();
                    if (state.Foreground == window && state.LanguageName == language &&
                        (mode == null || state.ModeName == mode)) return;
                    Thread.Sleep(25);
                }
                throw new InvalidOperationException($"Test IME state unavailable: {language}/{mode}");
            }
            void Layout(nint window, string id, string language)
            {
                Focus(window);
                var layout = LiveNative.LoadKeyboardLayout(id, 0);
                if (layout == 0) throw new InvalidOperationException("Missing test keyboard layout.");
                LiveNative.PostMessage(window, 0x8001, 0, layout);
                WaitState(window, language);
            }
            void Mode(nint window, bool open, uint conversion, string name)
            {
                LiveNative.PostMessage(window, 0x8002, open ? 1 : 0, (nint)conversion);
                WaitState(window, "日语", name);
            }
            List<OsdMessage> Drain()
            {
                var result = new List<OsdMessage>();
                while (messages.TryDequeue(out var message)) result.Add(message);
                return result;
            }

            var a = StartEditor("A");
            var b = StartEditor("B");
            Layout(b, "00000411", "日语");
            Mode(b, false, 0, "直接输入");
            Layout(a, "00000409", "英语");
            monitor.Start();
            Thread.Sleep(800);
            Check(Drain().Count == 0, "LIVE: startup baseline is silent");

            Focus(b);
            WaitState(b, "日语", "直接输入");
            Thread.Sleep(150);
            Mode(b, true, 9, "平假名");
            Thread.Sleep(700);
            Check(Drain().Count == 0, "LIVE: window change and delayed IME restore stay silent");

            Mode(b, true, 11, "片假名");
            Thread.Sleep(500);
            var shown = Drain();
            Check(shown.Count == 1 && shown[0].Title == "日语" && shown[0].Detail == "片假名",
                "LIVE: stable-window mode change generates exactly one notification");
            var oldNotification = shown[0];
            Check(monitor.CanPresent(oldNotification), "LIVE: current notification passes UI guard");

            Focus(a);
            WaitState(a, "英语", "直接输入");
            Thread.Sleep(700);
            Check(Drain().Count == 0, "LIVE: switch back to English window stays silent");
            Check(!monitor.CanPresent(oldNotification), "LIVE: previous-window notification is rejected");

            Focus(b);
            WaitState(b, "日语", "片假名");
            Thread.Sleep(700);
            Check(Drain().Count == 0, "LIVE: returning to Japanese window does not replay its mode");
            Check(!monitor.CanPresent(oldNotification), "LIVE: old notification stays invalid after returning");

            Mode(b, false, 0, "直接输入");
            Thread.Sleep(500);
            shown = Drain();
            Check(shown.Count == 1 && shown[0].Detail == "直接输入",
                "LIVE: direct input change resumes normally");

            for (var i = 0; i < 6; i++)
            {
                Focus(i % 2 == 0 ? a : b);
                Thread.Sleep(160);
            }
            Thread.Sleep(650);
            Check(Drain().Count == 0, "LIVE: six rapid foreground changes produce no notifications");
            Mode(b, true, 9, "平假名");
            Thread.Sleep(500);
            shown = Drain();
            Check(shown.Count == 1 && shown[0].Detail == "平假名",
                "LIVE: rapid switching does not disable subsequent mode notifications");
        }
        finally
        {
            monitor.Dispose();
            foreach (var editor in editors)
            {
                if (!editor.HasExited)
                {
                    editor.CloseMainWindow();
                    if (!editor.WaitForExit(2000)) editor.Kill();
                }
                editor.Dispose();
            }
            if (originalForeground != 0) LiveNative.SetForegroundWindow(originalForeground);
        }
    }

    private static class LiveNative
    {
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern nint WindowFromPoint(System.Drawing.Point point);
        [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
        [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint LoadKeyboardLayout(string id, uint flags);
        [DllImport("user32.dll")] internal static extern nint ActivateKeyboardLayout(nint layout, uint flags);
        [DllImport("user32.dll")] internal static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
        [DllImport("imm32.dll")] internal static extern bool ImmSetOpenStatus(nint context, bool open);
        [DllImport("imm32.dll")] internal static extern bool ImmSetConversionStatus(nint context, uint conversion, uint sentence);
    }
}
