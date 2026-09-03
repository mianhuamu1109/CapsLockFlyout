using System.IO;

namespace FluentFlyoutWPF.CapsFlyout;

internal static class AppStorage
{
    internal static string DataDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data");

    internal static string ErrorLogPath =>
        Path.Combine(DataDirectory, "CapsLockFlyout.error.log");

    internal static string ServiceLogPath =>
        Path.Combine(DataDirectory, "CapsLockFlyout.service.log");

    internal static void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
    }

    internal static void AppendServiceLog(string message)
    {
        try
        {
            EnsureDataDirectory();
            File.AppendAllText(
                ServiceLogPath,
                $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // A service must remain startable even if the installation folder
            // was made read-only after installation.
        }
    }
}
