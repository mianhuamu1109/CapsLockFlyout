using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.IO;

namespace FluentFlyoutWPF.CapsFlyout;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CapsLockFlyout";

    internal static bool IsEnabled =>
        WindowsServiceRegistration.IsAutomaticForCurrentExecutable;

    internal static bool TrySetEnabled(bool enabled, out string? error)
    {
        error = null;
        try
        {
            RemoveLegacyRunRegistration();

            var executable = Environment.ProcessPath ??
                             Path.Combine(AppContext.BaseDirectory, "CapsLockFlyout.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = enabled ? "--install-service" : "--uninstall-service",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };
            using var process = Process.Start(startInfo) ??
                                throw new InvalidOperationException("无法启动服务设置程序。");
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Windows 服务设置失败，请查看安装目录 Data 中的服务日志。");
            return enabled ? IsEnabled : !IsEnabled;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            error = "已取消管理员授权。";
            return false;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    internal static void RemoveLegacyRunRegistration()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Old startup registration is harmless if policy prevents removal;
            // the per-session mutex still guarantees a single UI process.
        }
    }
}
