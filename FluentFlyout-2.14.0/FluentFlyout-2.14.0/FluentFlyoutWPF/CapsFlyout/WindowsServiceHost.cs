using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace FluentFlyoutWPF.CapsFlyout;

internal static class WindowsServiceHost
{
    private const uint ServiceControlStop = 1;
    private const uint ServiceControlShutdown = 5;
    private const uint ServiceControlSessionChange = 14;

    private const uint ServiceStopped = 1;
    private const uint ServiceStartPending = 2;
    private const uint ServiceStopPending = 3;
    private const uint ServiceRunning = 4;

    private const uint ServiceAcceptStop = 0x00000001;
    private const uint ServiceAcceptShutdown = 0x00000004;
    private const uint ServiceAcceptSessionChange = 0x00000080;
    private const uint ServiceWin32OwnProcess = 0x00000010;

    private const uint WtsConsoleConnect = 1;
    private const uint WtsRemoteConnect = 3;
    private const uint WtsSessionLogon = 5;
    private const uint WtsSessionUnlock = 8;
    private const uint InvalidSessionId = 0xFFFFFFFF;

    private const uint MaximumAllowed = 0x02000000;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint StillActive = 259;

    private static readonly ServiceMainFunction ServiceMainDelegate = OnServiceMain;
    private static readonly ServiceControlHandlerEx ControlHandlerDelegate = OnServiceControl;
    private static readonly ManualResetEvent StopEvent = new(false);
    private static readonly object ProcessGate = new();
    private static readonly Dictionary<uint, IntPtr> SessionProcesses = new();

    private static IntPtr _statusHandle;
    private static uint _checkpoint;

    internal static int Run()
    {
        var table = new[]
        {
            new ServiceTableEntry
            {
                ServiceName = WindowsServiceRegistration.ServiceName,
                ServiceMain = ServiceMainDelegate
            },
            new ServiceTableEntry()
        };

        if (StartServiceCtrlDispatcher(table))
            return 0;

        var error = Marshal.GetLastWin32Error();
        AppStorage.AppendServiceLog(
            $"无法连接 Windows 服务控制管理器：{new Win32Exception(error).Message} ({error})");
        return error;
    }

    private static void OnServiceMain(uint argumentCount, IntPtr arguments)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx(
            WindowsServiceRegistration.ServiceName,
            ControlHandlerDelegate,
            IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero)
        {
            AppStorage.AppendServiceLog(
                $"注册服务控制处理器失败：{new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            return;
        }

        ReportStatus(ServiceStartPending, waitHint: 5000);
        AppStorage.AppendServiceLog("自动启动服务正在运行。");
        ReportStatus(ServiceRunning);

        var activeSession = WTSGetActiveConsoleSessionId();
        if (activeSession != InvalidSessionId)
            QueueLaunch(activeSession);

        StopEvent.WaitOne();
        ReportStatus(ServiceStopPending, waitHint: 3000);

        lock (ProcessGate)
        {
            foreach (var processHandle in SessionProcesses.Values)
                CloseHandle(processHandle);
            SessionProcesses.Clear();
        }

        AppStorage.AppendServiceLog("自动启动服务已停止。");
        ReportStatus(ServiceStopped);
    }

    private static uint OnServiceControl(
        uint control,
        uint eventType,
        IntPtr eventData,
        IntPtr context)
    {
        switch (control)
        {
            case ServiceControlStop:
            case ServiceControlShutdown:
                ReportStatus(ServiceStopPending, waitHint: 3000);
                StopEvent.Set();
                break;

            case ServiceControlSessionChange:
                if (eventData != IntPtr.Zero &&
                    eventType is WtsConsoleConnect or WtsRemoteConnect or WtsSessionLogon or WtsSessionUnlock)
                {
                    var notification = Marshal.PtrToStructure<WtsSessionNotification>(eventData);
                    QueueLaunch(notification.SessionId);
                }
                break;
        }

        return 0;
    }

    private static void QueueLaunch(uint sessionId)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                LaunchForSession(sessionId);
            }
            catch (Exception exception)
            {
                AppStorage.AppendServiceLog(
                    $"启动用户会话 {sessionId} 的浮窗程序失败：{exception}");
            }
        });
    }

    private static void LaunchForSession(uint sessionId)
    {
        lock (ProcessGate)
        {
            RemoveExitedProcesses();
            if (SessionProcesses.ContainsKey(sessionId))
                return;
        }

        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            var error = Marshal.GetLastWin32Error();
            // A connection event may arrive before the user token is ready.
            AppStorage.AppendServiceLog(
                $"会话 {sessionId} 的用户令牌尚不可用：{new Win32Exception(error).Message} ({error})");
            return;
        }

        IntPtr primaryToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;
        try
        {
            if (!DuplicateTokenEx(
                    userToken,
                    MaximumAllowed,
                    IntPtr.Zero,
                    SecurityImpersonation,
                    TokenPrimary,
                    out primaryToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!CreateEnvironmentBlock(out environment, primaryToken, false))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var executable = Environment.ProcessPath ??
                             throw new InvalidOperationException("无法确定程序路径。");
            var commandLine = new StringBuilder($"\"{executable}\" --startup");
            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Desktop = @"winsta0\default"
            };

            if (!CreateProcessAsUser(
                    primaryToken,
                    executable,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateUnicodeEnvironment,
                    environment,
                    AppContext.BaseDirectory,
                    ref startup,
                    out var process))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            CloseHandle(process.Thread);
            lock (ProcessGate)
            {
                RemoveExitedProcesses();
                if (SessionProcesses.TryGetValue(sessionId, out var oldHandle))
                    CloseHandle(oldHandle);
                SessionProcesses[sessionId] = process.Process;
            }

            AppStorage.AppendServiceLog(
                $"已在用户会话 {sessionId} 启动浮窗与托盘进程，PID={process.ProcessId}。");
        }
        finally
        {
            if (environment != IntPtr.Zero)
                DestroyEnvironmentBlock(environment);
            if (primaryToken != IntPtr.Zero)
                CloseHandle(primaryToken);
            CloseHandle(userToken);
        }
    }

    private static void RemoveExitedProcesses()
    {
        foreach (var pair in SessionProcesses.ToArray())
        {
            if (GetExitCodeProcess(pair.Value, out var exitCode) && exitCode == StillActive)
                continue;

            CloseHandle(pair.Value);
            SessionProcesses.Remove(pair.Key);
        }
    }

    private static void ReportStatus(uint currentState, uint win32ExitCode = 0, uint waitHint = 0)
    {
        if (_statusHandle == IntPtr.Zero)
            return;

        var pending = currentState is ServiceStartPending or ServiceStopPending;
        var status = new ServiceStatus
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = currentState,
            ControlsAccepted = currentState == ServiceRunning
                ? ServiceAcceptStop | ServiceAcceptShutdown | ServiceAcceptSessionChange
                : 0,
            Win32ExitCode = win32ExitCode,
            CheckPoint = pending ? ++_checkpoint : 0,
            WaitHint = waitHint
        };
        _ = SetServiceStatus(_statusHandle, ref status);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceMainFunction(uint argumentCount, IntPtr arguments);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ServiceControlHandlerEx(
        uint control,
        uint eventType,
        IntPtr eventData,
        IntPtr context);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? ServiceName;
        internal ServiceMainFunction? ServiceMain;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        internal uint ServiceType;
        internal uint CurrentState;
        internal uint ControlsAccepted;
        internal uint Win32ExitCode;
        internal uint ServiceSpecificExitCode;
        internal uint CheckPoint;
        internal uint WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionNotification
    {
        internal uint Size;
        internal uint SessionId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        internal int Size;
        internal string? Reserved;
        internal string? Desktop;
        internal string? Title;
        internal uint X;
        internal uint Y;
        internal uint XSize;
        internal uint YSize;
        internal uint XCountChars;
        internal uint YCountChars;
        internal uint FillAttribute;
        internal uint Flags;
        internal ushort ShowWindow;
        internal ushort Reserved2;
        internal IntPtr ReservedPointer;
        internal IntPtr StandardInput;
        internal IntPtr StandardOutput;
        internal IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        internal IntPtr Process;
        internal IntPtr Thread;
        internal uint ProcessId;
        internal uint ThreadId;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] serviceTable);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(
        string serviceName,
        ServiceControlHandlerEx handler,
        IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(IntPtr statusHandle, ref ServiceStatus status);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(
        out IntPtr environment,
        IntPtr token,
        [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(
        IntPtr token,
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
