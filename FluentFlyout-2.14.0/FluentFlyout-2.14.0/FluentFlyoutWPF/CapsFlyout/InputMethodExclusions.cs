using System.IO;
using System.Text.Json;

namespace FluentFlyoutWPF.CapsFlyout;

// Immutable snapshots: UI saves once; polling never reads the configuration file.
internal sealed class InputMethodExclusions
{
    private static readonly Lazy<InputMethodExclusions> Shared = new(() =>
        new(Path.Combine(AppStorage.DataDirectory, "ime-exclusions.json")));
    internal static InputMethodExclusions Current => Shared.Value;
    private readonly string _file;
    private string[] _paths = [];
    internal event Action? Changed;
    internal string? LoadError { get; }
    internal IReadOnlyList<string> Paths => Array.AsReadOnly(Volatile.Read(ref _paths));
    internal bool HasEntries => Volatile.Read(ref _paths).Length != 0;

    internal InputMethodExclusions(string file)
    {
        _file = file;
        try
        {
            if (File.Exists(file))
                _paths = Normalize(JsonSerializer.Deserialize<string[]>(File.ReadAllText(file))
                    ?? throw new InvalidDataException("配置内容为空。"));
        }
        catch (Exception e) when (IsStorageError(e))
        {
            LoadError = e.Message;
        }
    }

    internal bool Contains(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return false;
        try
        {
            var normalized = NormalizePath(executable);
            return Array.Exists(Volatile.Read(ref _paths),
                path => StringComparer.OrdinalIgnoreCase.Equals(path, normalized));
        }
        catch (Exception e) when (IsStorageError(e)) { return false; }
    }

    // UI-thread operation. Publish only after the on-disk replacement succeeds.
    internal bool TrySave(IEnumerable<string> paths, out string? error)
    {
        error = null;
        string? temporary = null;
        try
        {
            if (LoadError is not null)
                throw new InvalidDataException("原配置无法读取，为防止覆盖已暂停保存。请检查 " + _file);
            var next = Normalize(paths);
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            temporary = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(next,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _file, overwrite: true);
            Volatile.Write(ref _paths, next);
        }
        catch (Exception e) when (IsStorageError(e))
        {
            error = e.Message;
            return false;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        Changed?.Invoke();
        return true;
    }

    private static string[] Normalize(IEnumerable<string> paths) => paths
        .Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择程序的完整 EXE 路径。");
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + fullPath[8..];
        return fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? fullPath[4..] : fullPath;
    }

    private static bool IsStorageError(Exception e) =>
        e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException
            or NotSupportedException or System.Security.SecurityException;
}
