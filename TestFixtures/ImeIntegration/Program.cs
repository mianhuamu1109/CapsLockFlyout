using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentFlyoutWPF.CapsFlyout;

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadKeyboardLayout(string id, uint flags);
    [DllImport("user32.dll")] private static extern nint ActivateKeyboardLayout(nint layout, uint flags);
    [DllImport("imm32.dll")] private static extern bool ImmSetOpenStatus(nint context, bool open);
    [DllImport("imm32.dll")] private static extern bool ImmSetConversionStatus(nint context, uint conversion, uint sentence);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    private static void Down(byte key) => keybd_event(key, (byte)MapVirtualKey(key, 0), key == 0x5B ? 1u : 0u, 0);
    private static void Up(byte key) => keybd_event(key, (byte)MapVirtualKey(key, 0), key == 0x5B ? 3u : 2u, 0);
    private static void Tap(byte key) { Down(key); Thread.Sleep(35); Up(key); }
    private static void Combo(byte modifier) { Down(modifier); Thread.Sleep(35); Tap(0x14); Thread.Sleep(35); Up(modifier); }
    private sealed class TestEditor : Form
    {
        internal readonly TextBox Editor = new() { Multiline = true, Dock = DockStyle.Fill, ImeMode = ImeMode.NoControl };
        internal TestEditor()
        {
            Text = "CapsLockFlyout IME test editor";
            Width = 650; Height = 350;
            Controls.Add(Editor);
            Shown += (_, _) => Editor.Focus();
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x8001)
            {
                Editor.Focus();
                ActivateKeyboardLayout(message.LParam, 0);
                return;
            }
            // Test target only: change its own IME without any keyboard event.
            if (message.Msg == 0x8002)
            {
                var context = ImeNative.ImmGetContext(Editor.Handle);
                if (context != 0)
                {
                    try
                    {
                        ImmSetOpenStatus(context, message.WParam != 0);
                        if (message.WParam != 0)
                            ImmSetConversionStatus(context, (uint)message.LParam, 0);
                    }
                    finally { ImeNative.ImmReleaseContext(Editor.Handle, context); }
                }
                return;
            }
            base.WndProc(ref message);
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        if (args.Contains("--editor"))
        {
            Application.Run(new TestEditor());
            return 0;
        }
        if (args.Contains("--probe"))
        {
            var s = InputMethodStateReader.Read();
            Console.WriteLine(JsonSerializer.Serialize(new { s.Valid, s.LanguageName, s.ModeName, s.Open, s.Conversion }));
            return 0;
        }
        if (args.Length < 2) throw new ArgumentException("Pass app executable and evidence directory.");
        var appExe = Path.GetFullPath(args[0]);
        var evidence = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(evidence);
        var log = Path.Combine(Path.GetDirectoryName(appExe)!, "Data", "ime-diagnostics.jsonl");
        using var editorProcess = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--editor") { UseShellExecute = false })!;
        var oldForeground = ImeNative.GetForegroundWindow();
        Process? app = null;
        var failures = 0;
        var checks = new List<object>();
        try
        {
            nint window = 0;
            for (var i = 0; i < 80 && window == 0; i++)
            {
                NativeMethods.EnumWindows((hwnd, _) =>
                {
                    NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                    if (pid == editorProcess.Id &&
                        NativeMethods.GetWindowTitle(hwnd) == "CapsLockFlyout IME test editor")
                        window = hwnd;
                    return true;
                }, 0);
                if (window == 0) Thread.Sleep(100);
            }
            if (window == 0) throw new InvalidOperationException("Test editor failed to start.");
            var edit = FindWindowEx(window, 0, null, null);
            SetForegroundWindow(window);
            Thread.Sleep(250);
            app = Process.Start(new ProcessStartInfo(appExe, "--startup --ime-diagnostics") { UseShellExecute = false })!;
            Thread.Sleep(800);
            if (app.HasExited) throw new InvalidOperationException("Flyout exited (existing instance or startup failure).");

            long LogLength() => File.Exists(log) ? new FileInfo(log).Length : 0;
            List<JsonElement> NewMessages(long offset)
            {
                var result = new List<JsonElement>();
                if (!File.Exists(log)) return result;
                using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is string line)
                    try { result.Add(JsonDocument.Parse(line).RootElement.Clone()); } catch (JsonException) { }
                return result;
            }
            void Check(string name, Action action, string language, string mode, bool expectMessage = true,
                bool expectSilent = false)
            {
                SetForegroundWindow(window);
                Thread.Sleep(100);
                var offset = LogLength();
                action();
                Thread.Sleep(650);
                var state = InputMethodStateReader.Read();
                var messages = NewMessages(offset);
                var correctState = state.LanguageName == language && state.ModeName == mode;
                var shown = messages.LastOrDefault();
                var correctMessage = !expectMessage || shown.ValueKind == JsonValueKind.Object &&
                    shown.GetProperty("Title").GetString() == language && shown.GetProperty("Detail").GetString() == mode;
                var noWrongCaps = !messages.Any(m => m.GetProperty("Title").GetString() == "大写锁定");
                var passed = correctState && correctMessage && noWrongCaps && (!expectSilent || messages.Count == 0);
                if (!passed) failures++;
                checks.Add(new { name, passed, actualLanguage = state.LanguageName, actualMode = state.ModeName,
                    state.Open, state.Conversion, messages });
                Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}: {state.LanguageName}/{state.ModeName}; messages={messages.Count}");
                if (passed && expectMessage && name.StartsWith("Japanese"))
                {
                    nint flyout = 0;
                    NativeMethods.EnumWindows((hwnd, _) =>
                    {
                        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                        if (pid == app.Id && NativeMethods.IsWindowVisible(hwnd) &&
                            NativeMethods.GetWindowTitle(hwnd) == "Caps Lock Flyout") flyout = hwnd;
                        return true;
                    }, 0);
                    if (NativeMethods.GetWindowRect(flyout, out var rect) && rect.Width > 0)
                    {
                        using var image = new Bitmap(rect.Width + 32, rect.Height + 32);
                        using var graphics = Graphics.FromImage(image);
                        graphics.CopyFromScreen(rect.Left - 16, rect.Top - 16, 0, 0, image.Size);
                        image.Save(Path.Combine(evidence, name.Replace(' ', '-') + ".png"));
                    }
                }
            }
            void Layout(string id)
            {
                var layout = LoadKeyboardLayout(id, 0);
                if (layout == 0) throw new InvalidOperationException("Requested layout missing: " + id);
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    SetForegroundWindow(window);
                    PostMessage(window, 0x8001, 0, layout);
                    for (var wait = 0; wait < 10; wait++)
                    {
                        Thread.Sleep(100);
                        var current = InputMethodStateReader.Read();
                        if (current.Foreground == window &&
                            ((long)current.Layout & 0xffff) == ((long)layout & 0xffff))
                        { Thread.Sleep(300); return; }
                    }
                }
                var actual = InputMethodStateReader.Read();
                throw new InvalidOperationException($"Test setup did not activate layout {id}: target={window:X}, foreground={actual.Foreground:X}, HKL={actual.Layout:X}");
            }
            // Warm up the IME and establish an observed, known starting mode.
            Layout("00000409");
            Layout("00000804");
            Thread.Sleep(500);
            if (InputMethodStateReader.Read().ModeName == "英语模式")
            { Tap(0x10); Thread.Sleep(500); }
            Check("Chinese baseline", () => { }, "汉语", "中文模式", false);
            // Microsoft Pinyin's TSF implementation need not honor legacy IMM
            // setters. Exercise its real Shift action rather than forcing a
            // legacy context out of sync. API-only mutation is tested for JP.
            var chineseTransitions = 0;
            for (var tap = 0; tap < 4; tap++)
            {
                var offset = LogLength();
                var beforeShift = InputMethodStateReader.Read();
                Tap(0x10);
                Thread.Sleep(750);
                var afterShift = InputMethodStateReader.Read();
                var messages = NewMessages(offset);
                var changed = !beforeShift.SameDisplay(afterShift);
                if (changed) chineseTransitions++;
                var shown = messages.LastOrDefault();
                var passed = afterShift.LanguageName == "汉语" && afterShift.Known &&
                    (changed ? shown.ValueKind == JsonValueKind.Object &&
                        shown.GetProperty("Detail").GetString() == afterShift.ModeName :
                        messages.Count == 0 || shown.GetProperty("Detail").GetString() == afterShift.ModeName);
                if (!passed) failures++;
                checks.Add(new { name = "Chinese Shift observed " + tap, passed, changed,
                    afterShift.ModeName, messages });
                Console.WriteLine($"{(passed ? "PASS" : "FAIL")} Chinese Shift observed {tap}: {afterShift.ModeName}; changed={changed}");
            }
            if (chineseTransitions < 2) failures++;
            checks.Add(new { name = "Chinese Shift changes observed", passed = chineseTransitions >= 2, chineseTransitions });
            Check("Japanese layout", () => Layout("00000411"), "日语", "直接输入");
            Check("Japanese API-only Hiragana", () => PostMessage(window, 0x8002, 1, 9), "日语", "平假名");
            Check("Japanese API-only Katakana", () => PostMessage(window, 0x8002, 1, 11), "日语", "片假名");
            Check("Japanese API-only Direct", () => PostMessage(window, 0x8002, 0, 0), "日语", "直接输入");
            for (var i = 0; i < 3; i++)
            {
                Check("Japanese Hiragana " + i, () => Combo(0x11), "日语", "平假名");
                Check("Japanese Katakana " + i, () => Combo(0x12), "日语", "片假名");
                Check("Japanese Direct " + i, () => Combo(0x10), "日语", "直接输入");
            }
            Check("Japanese Shift unchanged stays silent", () => Tap(0x10), "日语", "直接输入", false, true);
            Check("Japanese Caps unchanged stays silent", () => Tap(0x14), "日语", "直接输入", false, true);
            Check("English layout", () => Layout("00000409"), "英语", "直接输入");
            Check("English Shift unchanged stays silent", () => Tap(0x10), "英语", "直接输入", false, true);
            Layout("00000804");
            Thread.Sleep(500);
            var chineseBefore = InputMethodStateReader.Read().ModeName;
            Check("Chinese Shift after Japanese", () => Tap(0x10), "汉语",
                chineseBefore == "中文模式" ? "英语模式" : "中文模式");

            var before = LogLength();
            Down(0x10); Tap(0x41); Up(0x10);
            Thread.Sleep(400);
            var spurious = NewMessages(before);
            if (spurious.Count != 0) failures++;
            checks.Add(new { name = "Shift+A must not trigger", passed = spurious.Count == 0, messages = spurious });

            // Actual committed state is the oracle, not an assumed layout order.
            var beforeWin = InputMethodStateReader.Read();
            Down(0x5B);
            for (var i = 0; i < 4; i++)
            {
                var offset = LogLength();
                Tap(0x20);
                Thread.Sleep(600);
                var state = InputMethodStateReader.Read();
                checks.Add(new { name = "Win held Space " + i, actualLanguage = state.LanguageName,
                    actualMode = state.ModeName, messages = NewMessages(offset) });
            }
            var commitOffset = LogLength();
            Up(0x5B);
            Thread.Sleep(700);
            var final = InputMethodStateReader.Read();
            var commitMessages = NewMessages(commitOffset);
            var correctCommit = beforeWin.SameDisplay(final) ? commitMessages.Count == 0 : commitMessages.Any(m =>
                m.GetProperty("Title").GetString() == final.LanguageName &&
                m.GetProperty("Detail").GetString() == final.ModeName);
            if (!correctCommit) failures++;
            checks.Add(new { name = "Win release committed", passed = correctCommit,
                final.LanguageName, final.ModeName, messages = commitMessages });

            // A second, single-step switch exercises a committed change without
            // assuming how many keyboard layouts the user's system contains.
            Thread.Sleep(200);
            SetForegroundWindow(window);
            var singleBefore = InputMethodStateReader.Read();
            var singleOffset = LogLength();
            Down(0x5B); Thread.Sleep(100); Tap(0x20); Thread.Sleep(300); Up(0x5B);
            Thread.Sleep(900);
            var singleAfter = InputMethodStateReader.Read();
            var singleMessages = NewMessages(singleOffset);
            var singlePassed = singleBefore.SameDisplay(singleAfter) ? singleMessages.Count == 0 :
                singleMessages.Any(m => m.GetProperty("Title").GetString() == singleAfter.LanguageName &&
                    m.GetProperty("Detail").GetString() == singleAfter.ModeName);
            if (!singlePassed) failures++;
            checks.Add(new { name = "Single Win Space commit", passed = singlePassed,
                changed = !singleBefore.SameDisplay(singleAfter),
                singleAfter.LanguageName, singleAfter.ModeName, messages = singleMessages });
        }
        finally
        {
            foreach (byte key in new byte[] { 0x10, 0x11, 0x12, 0x14, 0x20, 0x5B }) Up(key);
            if (app is not null && !app.HasExited)
            {
                using var exit = Process.Start(new ProcessStartInfo(appExe, "--exit") { UseShellExecute = false });
                exit?.WaitForExit(6000);
            }
            editorProcess.CloseMainWindow();
            SetForegroundWindow(oldForeground);
            File.WriteAllText(Path.Combine(evidence, "results.json"),
                JsonSerializer.Serialize(new { failures, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failures == 0 ? 0 : 1;
    }
}
