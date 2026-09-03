using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FluentFlyoutWPF.CapsFlyout;

internal sealed class GHelperMonitor : IDisposable
{
    private static readonly Regex KeyRegex = new(@"Key:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex KeyboardLevelRegex = new(
        @"HotKey\s+.*C4-([0-9A-Fa-f]{2})\s*$",
        RegexOptions.Compiled);
    private static readonly Regex PerformanceRegex = new(
        @"Hotkey mode:\s*(-?\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AuraModeRegex = new(
        @"AuraMode:\s*([A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TouchpadRegex = new(
        @"Touchpad status:\s*([01])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BooleanTailRegex = new(
        @":(True|False)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _folder;
    private readonly bool _usesDefaultFolder;
    private readonly object _stateGate = new();
    private readonly Dictionary<string, PendingConfirmation> _pendingConfirmations = new(StringComparer.Ordinal);
    private System.Threading.Timer? _timer;
    private Dictionary<string, string>? _config;
    private DateTime _configWriteTime;
    private long _logPosition;
    private string _logCarry = string.Empty;
    private int _pendingKey = -1;
    private DateTime _pendingKeyAt;
    private byte _lastAcState = byte.MaxValue;
    private int? _performanceMode;
    private int? _keyboardLevel;
    private int? _auraMode;
    private bool? _touchpadEnabled;
    private bool? _microphoneMuted;
    private FileSystemWatcher? _logWatcher;
    private int _logReadScheduled;
    private int _polling;
    private NativeMethods.WinEventDelegate? _winEventCallback;
    private IntPtr _winEventHook;
    private bool _started;
    private bool _systemEventsRegistered;
    private bool _sessionLocked;
    private DateTime _suppressMessagesUntil;
    private volatile bool _disposed;

    internal event Action<OsdMessage>? MessageRaised;
    internal event Action? SessionReset;

    private readonly record struct PendingConfirmation(string Value, DateTime CreatedAt);

    internal GHelperMonitor()
    {
        var overrideFolder = Environment.GetEnvironmentVariable("CAPSLOCK_FLYOUT_GHELPER_DIR");
        _usesDefaultFolder = string.IsNullOrWhiteSpace(overrideFolder);
        _folder = _usesDefaultFolder
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GHelper")
            : overrideFolder!;

    }

    internal void Start()
    {
        if (_started)
            return;
        _started = true;

        EnsureGHelperOsdDisabled();
        RebaselineCurrentState();
        RegisterSystemEvents();

        _winEventCallback = OnWindowEvent;
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_SHOW,
            NativeMethods.EVENT_OBJECT_SHOW,
            IntPtr.Zero,
            _winEventCallback,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        SuppressExistingGHelperOsd();
        StartLogWatcher();
        // File parsing, JSON reads and window enumeration must not compete with
        // the 120 Hz animation dispatcher.  Keep this maintenance work on a
        // ThreadPool timer; MessageRaised is marshalled by MainWindow.
        _timer = new System.Threading.Timer(
            Poll,
            null,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100));
    }

    private void RegisterSystemEvents()
    {
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _systemEventsRegistered = true;
        }
        catch (InvalidOperationException)
        {
            // Session notifications are unavailable in some non-interactive
            // test environments. The log-replay guard below still applies.
        }
        catch (ExternalException)
        {
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        lock (_stateGate)
        {
            if (_disposed)
                return;

            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                BeginSessionSuppression();
                return;
            }

            if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon)
                ResumeSessionMonitoring();
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        lock (_stateGate)
        {
            if (_disposed)
                return;

            if (e.Mode == PowerModes.Suspend)
                BeginSessionSuppression();
            else if (e.Mode == PowerModes.Resume)
                ResumeSessionMonitoring();
        }
    }

    private void BeginSessionSuppression()
    {
        _sessionLocked = true;
        _pendingKey = -1;
        _pendingConfirmations.Clear();
        SessionReset?.Invoke();
    }

    private void ResumeSessionMonitoring()
    {
        _sessionLocked = false;
        _suppressMessagesUntil = DateTime.UtcNow.AddSeconds(2);
        _pendingKey = -1;
        _pendingConfirmations.Clear();
        RebaselineCurrentState();
        SessionReset?.Invoke();
    }

    private void RebaselineCurrentState()
    {
        if (NativeMethods.GetSystemPowerStatus(out var power))
            _lastAcState = power.AcLineStatus;

        _config = TryReadConfig();
        InitializePredictedStateFromConfig();

        RebaselineLog();

        var configPath = Path.Combine(_folder, "config.json");
        if (File.Exists(configPath))
        {
            try
            {
                _configWriteTime = File.GetLastWriteTimeUtc(configPath);
            }
            catch (IOException)
            {
                _configWriteTime = default;
            }
            catch (UnauthorizedAccessException)
            {
                _configWriteTime = default;
            }
        }
    }

    private void RebaselineLog()
    {
        _logCarry = string.Empty;
        var logPath = Path.Combine(_folder, "log.txt");
        if (!File.Exists(logPath))
        {
            _logPosition = 0;
            return;
        }

        InitializePredictedStateFromLog(logPath);
        try
        {
            _logPosition = new FileInfo(logPath).Length;
        }
        catch (IOException)
        {
            _logPosition = 0;
        }
        catch (UnauthorizedAccessException)
        {
            _logPosition = 0;
        }
    }

    private void StartLogWatcher()
    {
        if (!Directory.Exists(_folder))
            return;

        try
        {
            _logWatcher = new FileSystemWatcher(_folder, "log.txt")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _logWatcher.Changed += ScheduleLogRead;
            _logWatcher.Created += ScheduleLogRebaseline;
            _logWatcher.Renamed += ScheduleLogRebaseline;
        }
        catch (IOException)
        {
            _logWatcher?.Dispose();
            _logWatcher = null;
        }
        catch (UnauthorizedAccessException)
        {
            _logWatcher?.Dispose();
            _logWatcher = null;
        }
    }

    private void ScheduleLogRead(object sender, FileSystemEventArgs e)
    {
        if (_disposed || Interlocked.Exchange(ref _logReadScheduled, 1) != 0)
            return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lock (_stateGate)
                {
                    if (!_disposed)
                        ReadNewLogLines();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _logReadScheduled, 0);
            }
        });
    }

    private void ScheduleLogRebaseline(object sender, FileSystemEventArgs e)
    {
        if (_disposed || Interlocked.Exchange(ref _logReadScheduled, 1) != 0)
            return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lock (_stateGate)
                {
                    if (!_disposed)
                        RebaselineLog();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _logReadScheduled, 0);
            }
        });
    }

    private void Poll(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) != 0)
            return;

        try
        {
            lock (_stateGate)
            {
                if (_disposed)
                    return;
                ReadNewLogLines();
                CheckConfigChanges();
                CheckPowerSource();
                SuppressExistingGHelperOsd();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private void ReadNewLogLines()
    {
        var path = Path.Combine(_folder, "log.txt");
        if (!File.Exists(path))
            return;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (_logPosition > stream.Length)
            {
                RebaselineLog();
                return;
            }
            if (_logPosition == stream.Length)
                return;

            stream.Seek(_logPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
            var appended = reader.ReadToEnd();
            _logPosition = stream.Position;
            if (appended.Length == 0)
                return;

            var text = _logCarry + appended;
            var lines = text.Split('\n');
            if (!text.EndsWith('\n'))
            {
                _logCarry = lines[^1];
                lines = lines[..^1];
            }
            else
            {
                _logCarry = string.Empty;
            }

            foreach (var line in lines)
                ProcessLogLine(line.TrimEnd('\r'));
        }
        catch (IOException)
        {
            // G-Helper replaces or briefly locks this file while writing. The
            // next 50 ms tick retries from the same byte position.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ProcessLogLine(string line)
    {
        var keyMatch = KeyRegex.Match(line);
        if (keyMatch.Success && int.TryParse(keyMatch.Groups[1].Value, out var key))
        {
            _pendingKey = key;
            _pendingKeyAt = DateTime.UtcNow;
            HandleKeyImmediately(key);
            return;
        }

        var performance = PerformanceRegex.Match(line);
        if (performance.Success && int.TryParse(performance.Groups[1].Value, out var mode))
        {
            _performanceMode = mode;
            SetCachedValue("performance_mode", mode);
            if (IsPendingKey(174))
                RaisePerformanceMode(mode, predicted: false);
            return;
        }

        var aura = AuraModeRegex.Match(line);
        if (aura.Success && TryParseAuraMode(aura.Groups[1].Value, out var auraMode))
        {
            _auraMode = auraMode;
            SetCachedValue("aura_mode", auraMode);
            if (IsPendingKey(179))
                RaiseAuraMode(auraMode, predicted: false);
            return;
        }

        var keyboard = KeyboardLevelRegex.Match(line);
        if (keyboard.Success && IsPendingKey(196, 197) &&
            int.TryParse(keyboard.Groups[1].Value, NumberStyles.HexNumber, null, out var level))
        {
            RaiseKeyboardLevel(level, predicted: false);
            return;
        }

        var touchpad = TouchpadRegex.Match(line);
        if (touchpad.Success)
        {
            var enabled = touchpad.Groups[1].Value == "1";
            _touchpadEnabled = enabled;
            if (IsPendingKey(107))
            {
                PublishConfirmed("touchpad", enabled.ToString(), OsdMessage.WithState(
                    "\uE7F4", "触控板", enabled ? "已开启" : "已关闭", enabled, OsdStateVisual.Generic));
            }
            return;
        }

        var booleanTail = BooleanTailRegex.Match(line);
        if (booleanTail.Success && IsPendingKey(124))
        {
            var muted = booleanTail.Groups[1].Value.Equals("True", StringComparison.OrdinalIgnoreCase);
            _microphoneMuted = muted;
            PublishConfirmed("microphone", muted.ToString(), OsdMessage.WithState(
                muted ? "\uEC54" : "\uE720",
                "麦克风",
                muted ? "已静音" : "已开启",
                !muted,
                OsdStateVisual.Generic));
        }
    }

    private void HandleKeyImmediately(int key)
    {
        // A key record appended after the unlock baseline is a fresh user
        // action, so it may safely end the short post-unlock quiet period.
        if (!_sessionLocked)
            _suppressMessagesUntil = default;

        switch (key)
        {
            case 174:
                PredictNextPerformanceMode();
                break;
            case 196:
                PredictKeyboardLevel(+1);
                break;
            case 197:
                PredictKeyboardLevel(-1);
                break;
            case 179:
                PredictNextAuraMode();
                break;
            case 107:
                if (_touchpadEnabled.HasValue)
                {
                    _touchpadEnabled = !_touchpadEnabled.Value;
                    PublishPredicted("touchpad", _touchpadEnabled.Value.ToString(), OsdMessage.WithState(
                        "\uE7F4", "触控板", _touchpadEnabled.Value ? "已开启" : "已关闭",
                        _touchpadEnabled.Value, OsdStateVisual.Generic));
                }
                break;
            case 124:
                if (_microphoneMuted.HasValue)
                {
                    _microphoneMuted = !_microphoneMuted.Value;
                    PublishPredicted("microphone", _microphoneMuted.Value.ToString(), OsdMessage.WithState(
                        _microphoneMuted.Value ? "\uEC54" : "\uE720",
                        "麦克风",
                        _microphoneMuted.Value ? "已静音" : "已开启",
                        !_microphoneMuted.Value,
                        OsdStateVisual.Generic));
                }
                break;
            case 158:
                if (_config is not null && TryInt(_config, "fn_lock", out var currentFnLock))
                {
                    var nextFnLock = currentFnLock == 1 ? 0 : 1;
                    SetCachedValue("fn_lock", nextFnLock);
                    PublishPredicted("fn_lock", nextFnLock.ToString(CultureInfo.InvariantCulture),
                        OsdMessage.WithState(
                            "\uE765",
                            "Fn 锁定",
                            nextFnLock == 1 ? "已开启" : "已关闭",
                            nextFnLock == 1,
                            OsdStateVisual.Generic));
                }
                break;
        }
    }

    private void PredictNextPerformanceMode()
    {
        if (!_performanceMode.HasValue)
            return;

        var modes = new List<int> { 2, 0, 1 };
        if (_config is not null)
        {
            for (var mode = 3; mode <= 19; mode++)
            {
                if (_config.ContainsKey($"mode_base_{mode}"))
                    modes.Add(mode);
            }
        }

        var currentIndex = modes.IndexOf(_performanceMode.Value);
        if (currentIndex < 0)
            currentIndex = 0;
        var backwards = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
        var nextIndex = backwards
            ? (currentIndex - 1 + modes.Count) % modes.Count
            : (currentIndex + 1) % modes.Count;
        var next = modes[nextIndex];
        _performanceMode = next;
        SetCachedValue("performance_mode", next);
        RaisePerformanceMode(next, predicted: true);
    }

    private void PredictKeyboardLevel(int delta)
    {
        if (!_keyboardLevel.HasValue)
            return;

        var maximum = GetMaximumKeyboardBrightness();
        var next = Math.Clamp(_keyboardLevel.Value + delta, 0, maximum);
        _keyboardLevel = next;
        SetCachedValue(CurrentKeyboardBrightnessKey(), next);
        RaiseKeyboardLevel(next, predicted: true);
    }

    private void PredictNextAuraMode()
    {
        if (!_auraMode.HasValue)
            return;

        var modes = GetAvailableAuraModes();
        var currentIndex = modes.IndexOf(_auraMode.Value);
        if (currentIndex < 0)
            currentIndex = 0;
        var backwards = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
        var nextIndex = backwards
            ? (currentIndex - 1 + modes.Count) % modes.Count
            : (currentIndex + 1) % modes.Count;
        RaiseAuraMode(modes[nextIndex], predicted: true);
    }

    private List<int> GetAvailableAuraModes()
    {
        if (_config is not null && TryInt(_config, "no_rgb", out var noRgb) && noRgb == 1)
            return [0, 1, 10];

        var backlightType = 0;
        if (_config is not null)
            _ = TryInt(_config, "backlight_type", out backlightType);
        return backlightType switch
        {
            // Multi-zone keyboards expose Rainbow and G-Helper's software modes.
            2 => [0, 1, 2, 3, 10, 20, 21, 22, 23, 26, 27, 24, 25],
            // Per-key keyboards additionally expose the five reactive effects.
            3 => [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 20, 21, 22, 23, 26, 27, 24, 25],
            // This machine reports SingleZone (4).
            _ => [0, 1, 2, 10, 20, 21, 22, 23, 26, 27]
        };
    }

    private void RaiseAuraMode(int mode, bool predicted)
    {
        _auraMode = mode;
        SetCachedValue("aura_mode", mode);
        var value = mode.ToString(CultureInfo.InvariantCulture);
        var message = new OsdMessage(
            "\uE706",
            "键盘灯效",
            AuraName(mode),
            FixedDetailReference: "GPU 模式",
            CenterDetail: true);
        if (predicted)
            PublishPredicted("aura", value, message);
        else
            PublishConfirmed("aura", value, message);
    }

    private void RaiseKeyboardLevel(int level, bool predicted)
    {
        var maximum = GetMaximumKeyboardBrightness();
        level = Math.Clamp(level, 0, maximum);
        _keyboardLevel = level;
        var value = level.ToString(CultureInfo.InvariantCulture);
        var message = OsdMessage.WithProgress(
            "\uE765",
            "键盘背光",
            level == 0 ? "已关闭" : $"亮度 {level}/{maximum}",
            maximum == 0 ? 0 : level / (double)maximum);
        if (predicted)
            PublishPredicted("keyboard", value, message);
        else
            PublishConfirmed("keyboard", value, message);
    }

    private void PublishPredicted(string category, string value, OsdMessage message)
    {
        _pendingConfirmations[category] = new PendingConfirmation(value, DateTime.UtcNow);
        RaiseMessage(message);
    }

    private void PublishConfirmed(string category, string value, OsdMessage message)
    {
        if (_pendingConfirmations.TryGetValue(category, out var pending) &&
            DateTime.UtcNow - pending.CreatedAt < TimeSpan.FromSeconds(3))
        {
            _pendingConfirmations.Remove(category);
            if (string.Equals(pending.Value, value, StringComparison.Ordinal))
                return;
        }

        RaiseMessage(message);
    }

    private void RaiseMessage(OsdMessage message)
    {
        if (_sessionLocked || DateTime.UtcNow < _suppressMessagesUntil)
            return;

        MessageRaised?.Invoke(message);
    }

    private bool IsPendingKey(params int[] keys) =>
        DateTime.UtcNow - _pendingKeyAt < TimeSpan.FromSeconds(1.8) && keys.Contains(_pendingKey);

    private void InitializePredictedStateFromConfig()
    {
        if (_config is null)
            return;

        if (TryInt(_config, "performance_mode", out var mode))
            _performanceMode = mode;

        if (TryInt(_config, "aura_mode", out var aura))
            _auraMode = aura;

        var brightnessKey = CurrentKeyboardBrightnessKey();
        if (TryInt(_config, brightnessKey, out var brightness) ||
            TryInt(_config, "keyboard_brightness", out brightness))
            _keyboardLevel = Math.Clamp(brightness, 0, GetMaximumKeyboardBrightness());
    }

    private void InitializePredictedStateFromLog(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            const int tailLength = 256 * 1024;
            if (stream.Length > tailLength)
                stream.Seek(-tailLength, SeekOrigin.End);

            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
            var lines = reader.ReadToEnd().Split('\n');
            var recentKey = -1;
            var linesAfterKey = int.MaxValue;
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                var keyMatch = KeyRegex.Match(line);
                if (keyMatch.Success && int.TryParse(keyMatch.Groups[1].Value, out recentKey))
                {
                    linesAfterKey = 0;
                    continue;
                }

                linesAfterKey++;
                var performance = PerformanceRegex.Match(line);
                if (performance.Success && int.TryParse(performance.Groups[1].Value, out var mode))
                    _performanceMode = mode;

                var aura = AuraModeRegex.Match(line);
                if (aura.Success && TryParseAuraMode(aura.Groups[1].Value, out var auraMode))
                    _auraMode = auraMode;

                var keyboard = KeyboardLevelRegex.Match(line);
                if (keyboard.Success && int.TryParse(
                        keyboard.Groups[1].Value,
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out var brightness))
                    _keyboardLevel = Math.Clamp(brightness, 0, GetMaximumKeyboardBrightness());

                var touchpad = TouchpadRegex.Match(line);
                if (touchpad.Success)
                    _touchpadEnabled = touchpad.Groups[1].Value == "1";

                var boolean = BooleanTailRegex.Match(line);
                if (recentKey == 124 && linesAfterKey <= 8 && boolean.Success)
                    _microphoneMuted = boolean.Groups[1].Value.Equals("True", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private int GetMaximumKeyboardBrightness()
    {
        if (_config is not null && TryInt(_config, "max_brightness", out var maximum))
            return Math.Clamp(maximum, 1, 10);
        return 3;
    }

    private string CurrentKeyboardBrightnessKey() =>
        _lastAcState == 0 ? "keyboard_brightness_ac" : "keyboard_brightness";

    private void SetCachedValue(string key, int value)
    {
        if (_config is not null)
            _config[key] = value.ToString(CultureInfo.InvariantCulture);
    }

    private void CheckConfigChanges()
    {
        var path = Path.Combine(_folder, "config.json");
        if (!File.Exists(path))
            return;

        DateTime writeTime;
        try
        {
            writeTime = File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return;
        }

        if (writeTime == _configWriteTime)
            return;

        EnsureGHelperOsdDisabled();
        var next = TryReadConfig();
        if (next is null)
            return;

        var previous = _config;
        _config = next;
        _configWriteTime = writeTime;
        if (previous is null)
            return;

        // G-Helper rewrites several settings while restoring hardware after
        // logon/resume. That is a state synchronization, not several user
        // actions, so accept it as the new baseline without replaying OSDs.
        if (CountDisplayCategoriesChanged(previous, next) >= 3)
        {
            InitializePredictedStateFromConfig();
            return;
        }

        if (Changed(previous, next, "performance_mode") && TryInt(next, "performance_mode", out var mode))
        {
            _performanceMode = mode;
            RaisePerformanceMode(mode, predicted: false);
        }

        if (Changed(previous, next, "keyboard_brightness") && TryInt(next, "keyboard_brightness", out var level))
            RaiseKeyboardLevel(level, predicted: false);

        if (Changed(previous, next, "keyboard_brightness_ac") &&
            TryInt(next, "keyboard_brightness_ac", out var batteryLevel))
            RaiseKeyboardLevel(batteryLevel, predicted: false);

        if (Changed(previous, next, "gpu_mode") || Changed(previous, next, "gpu_auto"))
        {
            var automatic = TryInt(next, "gpu_auto", out var auto) && auto == 1;
            _ = TryInt(next, "gpu_mode", out var gpuMode);
            var detail = automatic ? "优化模式" : gpuMode switch
            {
                0 => "Eco（仅集成显卡）",
                1 => "标准（混合显卡）",
                2 => "Ultimate（独显直连）",
                _ => $"模式 {gpuMode}"
            };
            RaiseMessage(new OsdMessage("\uE7F4", "GPU 模式", detail));
        }

        if (Changed(previous, next, "frequency") && TryInt(next, "frequency", out var frequency))
            RaiseMessage(new OsdMessage("\uE72C", "屏幕刷新率", $"{frequency} Hz"));

        if (Changed(previous, next, "overdrive") && TryInt(next, "overdrive", out var overdrive))
            RaiseMessage(OsdMessage.WithState(
                "\uE7F4",
                "屏幕响应加速",
                overdrive == 1 ? "已开启" : "已关闭",
                overdrive == 1,
                OsdStateVisual.Generic));

        if (Changed(previous, next, "fn_lock") && TryInt(next, "fn_lock", out var fnLock))
            PublishConfirmed("fn_lock", fnLock.ToString(CultureInfo.InvariantCulture),
                OsdMessage.WithState(
                    "\uE765",
                    "Fn 锁定",
                    fnLock == 1 ? "已开启" : "已关闭",
                    fnLock == 1,
                    OsdStateVisual.Generic));

        if (Changed(previous, next, "aura_mode") && TryInt(next, "aura_mode", out var aura))
            RaiseAuraMode(aura, predicted: false);

        if (Changed(previous, next, "visual") && TryInt(next, "visual", out var visual))
            RaiseMessage(new OsdMessage("\uE7F4", "屏幕色彩模式", $"预设 {visual}"));

        if (Changed(previous, next, "matrix_brightness") && TryInt(next, "matrix_brightness", out var matrix))
            RaiseMessage(OsdMessage.WithProgress("\uE706", "AniMe Matrix", $"亮度 {matrix}/3", matrix / 3d));

        if (Changed(previous, next, "slash_dim") && TryInt(next, "slash_dim", out var slash))
            RaiseMessage(OsdMessage.WithProgress("\uE706", "Slash 灯效", $"亮度 {slash}%", slash / 100d));

        if (Changed(previous, next, "screenpad_brightness") && TryInt(next, "screenpad_brightness", out var screenPad))
            RaiseMessage(OsdMessage.WithProgress("\uE706", "ScreenPad", $"亮度 {screenPad}%", screenPad / 100d));

        foreach (var key in next.Keys.Where(k => k.Contains("miniled", StringComparison.OrdinalIgnoreCase)))
        {
            if (Changed(previous, next, key))
            {
                var miniLedEnabled = next[key] == "1";
                RaiseMessage(OsdMessage.WithState(
                    "\uE7F4",
                    "Mini LED",
                    miniLedEnabled ? "已开启" : "已关闭",
                    miniLedEnabled,
                    OsdStateVisual.Generic));
            }
        }
    }

    private void RaisePerformanceMode(int mode, bool predicted)
    {
        _performanceMode = mode;
        var customName = _config is not null && _config.TryGetValue($"mode_name_{mode}", out var configuredName)
            ? configuredName
            : null;
        var detail = mode switch
        {
            0 => "平衡模式",
            1 => "增强模式",
            2 => "安静模式",
            _ when !string.IsNullOrWhiteSpace(customName) => customName,
            _ => $"自定义模式 {mode}"
        };
        var value = mode.ToString(CultureInfo.InvariantCulture);
        var message = new OsdMessage("\uE945", "性能模式", detail);
        if (predicted)
            PublishPredicted("performance", value, message);
        else
            PublishConfirmed("performance", value, message);
    }

    private static string AuraName(int mode) => mode switch
    {
        0 => "静态",
        1 => "呼吸",
        2 => "彩色循环",
        3 => "彩虹",
        4 => "星空",
        5 => "雨滴",
        6 => "按键高亮",
        7 => "激光",
        8 => "涟漪",
        10 => "频闪",
        11 => "彗星",
        12 => "闪烁",
        20 => "温度图",
        21 => "GPU 模式",
        22 => "环境光",
        23 => "电量",
        24 => "渐变",
        25 => "区域测试",
        26 => "音频频谱",
        27 => "音频脉冲",
        _ => $"灯效 {mode}"
    };

    private static bool TryParseAuraMode(string name, out int mode)
    {
        mode = name.ToUpperInvariant() switch
        {
            "AURASTATIC" => 0,
            "AURABREATHE" => 1,
            "AURACOLORCYCLE" => 2,
            "AURARAINBOW" => 3,
            "STAR" => 4,
            "RAIN" => 5,
            "HIGHLIGHT" => 6,
            "LASER" => 7,
            "RIPPLE" => 8,
            "AURASTROBE" => 10,
            "COMET" => 11,
            "FLASH" => 12,
            "HEATMAP" => 20,
            "GPUMODE" => 21,
            "AMBIENT" => 22,
            "BATTERY" => 23,
            "GRADIENT" => 24,
            "ZONETEST" => 25,
            "AUDIO" => 26,
            "AUDIOPULSE" => 27,
            _ => -1
        };
        return mode >= 0;
    }

    private void CheckPowerSource()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var power) || power.AcLineStatus == byte.MaxValue)
            return;

        if (_lastAcState == byte.MaxValue)
        {
            _lastAcState = power.AcLineStatus;
            return;
        }
        if (power.AcLineStatus == _lastAcState)
            return;

        _lastAcState = power.AcLineStatus;
        var connected = power.AcLineStatus == 1;
        RaiseMessage(OsdMessage.WithState(
            "\uE7E8",
            "电源",
            connected ? "已连接电源" : "正在使用电池",
            connected,
            OsdStateVisual.Generic));
    }

    private Dictionary<string, string>? TryReadConfig()
    {
        var path = Path.Combine(_folder, "config.json");
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void EnsureGHelperOsdDisabled()
    {
        // Test fixtures opt out. On the real G-Helper profile this makes the
        // takeover persistent: G-Helper reads disable_osd on its next launch,
        // while the WinEvent hook below covers the current session.
        if (!_usesDefaultFolder)
            return;

        var path = Path.Combine(_folder, "config.json");
        if (!File.Exists(path))
            return;

        var temporaryPath = path + ".capslock-flyout.tmp";
        try
        {
            var options = new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            };
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: options) as JsonObject;
            if (root is null)
                return;

            if (string.Equals(root["disable_osd"]?.ToString(), "1", StringComparison.Ordinal))
                return;

            root["disable_osd"] = 1;
            File.WriteAllText(
                temporaryPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
    }

    private static bool Changed(
        IReadOnlyDictionary<string, string> oldValues,
        IReadOnlyDictionary<string, string> newValues,
        string key)
    {
        oldValues.TryGetValue(key, out var oldValue);
        newValues.TryGetValue(key, out var newValue);
        return !string.Equals(oldValue, newValue, StringComparison.Ordinal);
    }

    private static int CountDisplayCategoriesChanged(
        IReadOnlyDictionary<string, string> oldValues,
        IReadOnlyDictionary<string, string> newValues)
    {
        var count = 0;
        if (Changed(oldValues, newValues, "performance_mode"))
            count++;
        if (ChangedAny(oldValues, newValues, "keyboard_brightness", "keyboard_brightness_ac"))
            count++;
        if (ChangedAny(oldValues, newValues, "gpu_mode", "gpu_auto"))
            count++;
        if (Changed(oldValues, newValues, "frequency"))
            count++;
        if (Changed(oldValues, newValues, "overdrive"))
            count++;
        if (Changed(oldValues, newValues, "fn_lock"))
            count++;
        if (Changed(oldValues, newValues, "aura_mode"))
            count++;
        if (Changed(oldValues, newValues, "visual"))
            count++;
        if (Changed(oldValues, newValues, "matrix_brightness"))
            count++;
        if (Changed(oldValues, newValues, "slash_dim"))
            count++;
        if (Changed(oldValues, newValues, "screenpad_brightness"))
            count++;

        var miniLedKeys = oldValues.Keys
            .Concat(newValues.Keys)
            .Where(key => key.Contains("miniled", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal);
        if (miniLedKeys.Any(key => Changed(oldValues, newValues, key)))
            count++;

        return count;
    }

    private static bool ChangedAny(
        IReadOnlyDictionary<string, string> oldValues,
        IReadOnlyDictionary<string, string> newValues,
        params string[] keys) =>
        keys.Any(key => Changed(oldValues, newValues, key));

    private static bool TryInt(IReadOnlyDictionary<string, string> values, string key, out int value)
    {
        value = 0;
        return values.TryGetValue(key, out var raw) &&
               int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private void OnWindowEvent(
        IntPtr eventHook,
        uint eventType,
        IntPtr hwnd,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (hwnd != IntPtr.Zero && objectId == NativeMethods.OBJID_WINDOW)
            TrySuppressCompetingOsd(hwnd);
    }

    private static void SuppressExistingGHelperOsd()
    {
        _ = NativeMethods.EnumWindows((hwnd, _) =>
        {
            TrySuppressCompetingOsd(hwnd);
            return true;
        }, IntPtr.Zero);
    }

    private static void TrySuppressCompetingOsd(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd))
            return;

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
            return;

        var className = NativeMethods.GetWindowClass(hwnd);
        var title = NativeMethods.GetWindowTitle(hwnd);
        var mayBeGHelperOsd = title.Equals("FloatingNativeWindow", StringComparison.Ordinal);
        var mayBeOldLockOsd = className.StartsWith("HwndWrapper[FluentFlyout", StringComparison.Ordinal);
        if (!mayBeGHelperOsd && !mayBeOldLockOsd)
            return;

        string processName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch
        {
            return;
        }

        // Current G-Helper creates its OSD through NativeWindow with this exact
        // caption. It is no longer a blank-titled WindowsForms10 window.
        if (mayBeGHelperOsd && processName.Equals("GHelper", StringComparison.OrdinalIgnoreCase))
        {
            _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            return;
        }

        if (!string.IsNullOrEmpty(title) || !NativeMethods.GetWindowRect(hwnd, out var rect))
            return;

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = NativeMethods.MonitorInfo.Create();
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return;

        var scale = NativeMethods.GetEffectiveDpi(monitor) / 96d;
        var monitorCenter = info.Monitor.Left + info.Monitor.Width / 2;
        var windowCenter = rect.Left + rect.Width / 2;
        var centered = Math.Abs(windowCenter - monitorCenter) <= 110 * scale;
        var distanceFromBottom = info.Monitor.Bottom - rect.Top;
        // The Microsoft Store build of upstream FluentFlyout may still be in
        // startup while the replacement is tested. Hide only its compact lock
        // window (160 x 50 DIP), never its settings window.
        var isOldLockOsd =
            processName.Equals("FluentFlyout", StringComparison.OrdinalIgnoreCase) &&
            mayBeOldLockOsd &&
            InScaledOrPhysicalRange(rect.Width, 145, 175, scale) &&
            InScaledOrPhysicalRange(rect.Height, 44, 56, scale) &&
            centered &&
            distanceFromBottom >= 0 && distanceFromBottom <= 140 * scale;

        if (isOldLockOsd)
            _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
    }

    private static bool InScaledOrPhysicalRange(double value, double minimum, double maximum, double scale) =>
        (value >= minimum && value <= maximum) ||
        (value >= minimum * scale && value <= maximum * scale);

    public void Dispose()
    {
        _disposed = true;
        if (_systemEventsRegistered)
        {
            try
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
            catch (InvalidOperationException)
            {
            }
            catch (ExternalException)
            {
            }
            _systemEventsRegistered = false;
        }
        _timer?.Dispose();
        _timer = null;
        if (_logWatcher is not null)
        {
            _logWatcher.EnableRaisingEvents = false;
            _logWatcher.Dispose();
            _logWatcher = null;
        }
        if (_winEventHook != IntPtr.Zero)
        {
            _ = NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
        _winEventCallback = null;
    }
}
