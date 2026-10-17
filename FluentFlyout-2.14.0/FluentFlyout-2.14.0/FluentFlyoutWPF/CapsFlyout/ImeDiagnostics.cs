using System.IO;
using System.Text.Json;

namespace FluentFlyoutWPF.CapsFlyout;

internal static class ImeDiagnostics
{
    internal static bool Enabled { get; set; }
    internal static void Presented(OsdMessage message, double width)
    {
        if (!Enabled) return;
        try
        {
            AppStorage.EnsureDataDirectory();
            File.AppendAllText(Path.Combine(AppStorage.DataDirectory, "ime-diagnostics.jsonl"),
                JsonSerializer.Serialize(new
                {
                    Time = DateTimeOffset.UtcNow, message.Title, message.Detail,
                    message.InputMethod, Width = width
                }) + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
