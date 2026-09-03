using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace FluentFlyoutWPF.CapsFlyout;

internal static class WindowsServiceRegistration
{
    internal const string ServiceName = "CapsLockFlyoutService";
    private const string DisplayName = "CapsLockFlyout 自动启动服务";
    private const string Description = "在用户登录或解锁后尽早启动 CapsLockFlyout 浮窗与托盘程序。";

    private const uint ScManagerConnect = 0x0001;
    private const uint ScManagerCreateService = 0x0002;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint Delete = 0x00010000;
    private const uint ServiceWin32OwnProcess = 0x00000010;
    private const uint ServiceAutoStart = 0x00000002;
    private const uint ServiceErrorNormal = 0x00000001;
    private const uint ServiceNoChange = 0xFFFFFFFF;
    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceStopped = 0x00000001;
    private const int ServiceConfigDescription = 1;
    private const int ServiceConfigDelayedAutoStartInfo = 3;

    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceNotActive = 1062;
    private const int ErrorServiceMarkedForDelete = 1072;

    internal static bool IsAutomaticForCurrentExecutable
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
                if (key is null || Convert.ToInt32(key.GetValue("Start", -1)) != ServiceAutoStart)
                    return false;

                var imagePath = Convert.ToString(key.GetValue("ImagePath")) ?? string.Empty;
                var executable = Environment.ProcessPath ?? string.Empty;
                return executable.Length > 0 &&
                       imagePath.Contains(executable, StringComparison.OrdinalIgnoreCase) &&
                       imagePath.Contains("--service", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    internal static bool Install(out string? error)
    {
        error = null;
        try
        {
            EnsureAdministrator();
            var executable = Environment.ProcessPath ??
                             throw new InvalidOperationException("无法确定程序路径。");
            var binaryPath = $"\"{executable}\" --service";

            var manager = OpenSCManager(null, null, ScManagerConnect | ScManagerCreateService);
            if (manager == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                var access = ServiceQueryStatus | ServiceStart | ServiceStop |
                             ServiceChangeConfig | Delete;
                var service = CreateService(
                    manager,
                    ServiceName,
                    DisplayName,
                    access,
                    ServiceWin32OwnProcess,
                    ServiceAutoStart,
                    ServiceErrorNormal,
                    binaryPath,
                    null,
                    IntPtr.Zero,
                    null,
                    null,
                    null);

                if (service == IntPtr.Zero)
                {
                    var createError = Marshal.GetLastWin32Error();
                    if (createError != 1073) // ERROR_SERVICE_EXISTS
                        throw new Win32Exception(createError);

                    service = OpenService(manager, ServiceName, access);
                    if (service == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error());

                    if (!ChangeServiceConfig(
                            service,
                            ServiceNoChange,
                            ServiceAutoStart,
                            ServiceNoChange,
                            binaryPath,
                            null,
                            IntPtr.Zero,
                            null,
                            null,
                            null,
                            DisplayName))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }

                try
                {
                    var description = new ServiceDescription { Value = Description };
                    _ = ChangeServiceDescription(service, ServiceConfigDescription, ref description);

                    // Normal automatic start is intentional: delayed automatic
                    // start would postpone the per-user tray process after login.
                    var delayed = new ServiceDelayedAutoStartInfo { Delayed = false };
                    _ = ChangeDelayedAutoStart(service, ServiceConfigDelayedAutoStartInfo, ref delayed);

                    if (!StartService(service, 0, IntPtr.Zero))
                    {
                        var startError = Marshal.GetLastWin32Error();
                        if (startError != ErrorServiceAlreadyRunning)
                            throw new Win32Exception(startError);
                    }
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(manager);
            }

            AppStorage.AppendServiceLog("自动启动服务已安装并启动。");
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            AppStorage.AppendServiceLog($"安装自动启动服务失败：{exception}");
            return false;
        }
    }

    internal static bool Uninstall(out string? error)
    {
        error = null;
        try
        {
            EnsureAdministrator();
            var manager = OpenSCManager(null, null, ScManagerConnect);
            if (manager == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                var service = OpenService(
                    manager,
                    ServiceName,
                    ServiceQueryStatus | ServiceStop | Delete);
                if (service == IntPtr.Zero)
                {
                    var openError = Marshal.GetLastWin32Error();
                    if (openError == ErrorServiceDoesNotExist)
                        return true;
                    throw new Win32Exception(openError);
                }

                try
                {
                    if (!ControlService(service, ServiceControlStop, out _))
                    {
                        var stopError = Marshal.GetLastWin32Error();
                        if (stopError != ErrorServiceNotActive)
                            throw new Win32Exception(stopError);
                    }

                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (DateTime.UtcNow < deadline &&
                           QueryServiceStatus(service, out var status) &&
                           status.CurrentState != ServiceStopped)
                    {
                        Thread.Sleep(100);
                    }

                    if (!DeleteService(service))
                    {
                        var deleteError = Marshal.GetLastWin32Error();
                        if (deleteError != ErrorServiceMarkedForDelete)
                            throw new Win32Exception(deleteError);
                    }
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(manager);
            }

            AppStorage.AppendServiceLog("自动启动服务已停止并移除。");
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            AppStorage.AppendServiceLog($"移除自动启动服务失败：{exception}");
            return false;
        }
    }

    private static void EnsureAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("此操作需要管理员权限。");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceDescription
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceDelayedAutoStartInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        internal bool Delayed;
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

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateService(
        IntPtr manager,
        string serviceName,
        string displayName,
        uint desiredAccess,
        uint serviceType,
        uint startType,
        uint errorControl,
        string binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig(
        IntPtr service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceDescription(
        IntPtr service,
        int infoLevel,
        ref ServiceDescription info);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeDelayedAutoStart(
        IntPtr service,
        int infoLevel,
        ref ServiceDelayedAutoStartInfo info);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(IntPtr service, int argumentCount, IntPtr arguments);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(IntPtr service, uint control, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteService(IntPtr service);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
