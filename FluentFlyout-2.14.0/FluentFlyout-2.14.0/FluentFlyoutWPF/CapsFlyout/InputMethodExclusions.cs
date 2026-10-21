using System.IO;
using System.Text.Json;

namespace FluentFlyoutWPF.CapsFlyout;

internal sealed record ApplicationExclusion(string Name, string Family, string AppId);

// One immutable snapshot for both kinds of rules; polling never reads files.
internal sealed class InputMethodExclusions
{
    private sealed record Configuration(int Version, string[] Paths, ApplicationExclusion[] Applications);
    private static readonly Lazy<InputMethodExclusions> Shared = new(() =>
        new(Path.Combine(AppStorage.DataDirectory, "ime-exclusions.json")));
    internal static InputMethodExclusions Current => Shared.Value;
    private readonly string _file;
    private Configuration _state = new(2, [], []);
    private bool _legacyFile;
    internal event Action? Changed;
    internal string? LoadError { get; }
    internal IReadOnlyList<string> Paths => Array.AsReadOnly(Volatile.Read(ref _state).Paths);
    internal IReadOnlyList<ApplicationExclusion> Applications => Array.AsReadOnly(Volatile.Read(ref _state).Applications);
    internal bool HasEntries
    {
        get { var state = Volatile.Read(ref _state); return state.Paths.Length + state.Applications.Length != 0; }
    }
    internal bool HasApplications => Volatile.Read(ref _state).Applications.Length != 0;

    internal InputMethodExclusions(string file)
    {
        _file = file;
        try
        {
            if (!File.Exists(file)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                _state = Normalize(document.RootElement.Deserialize<string[]>() ?? throw new InvalidDataException(), []);
                _legacyFile = true;
            }
            else
            {
                var saved = document.RootElement.Deserialize<Configuration>() ?? throw new InvalidDataException("配置内容为空。");
                if (saved.Version != 2 || saved.Paths is null || saved.Applications is null)
                    throw new InvalidDataException("不支持的配置格式；原文件未改动。");
                _state = Normalize(saved.Paths, saved.Applications);
            }
        }
        catch (Exception e) when (IsStorageError(e)) { LoadError = e.Message; }
    }

    internal bool Contains(string? executable) => Matches(new(executable));

    internal bool Matches(ApplicationIdentity identity)
    {
        var state = Volatile.Read(ref _state);
        if (identity.Family is not null && identity.AppId is not null &&
            state.Applications.Any(rule =>
                StringComparer.OrdinalIgnoreCase.Equals(rule.Family, identity.Family) &&
                StringComparer.OrdinalIgnoreCase.Equals(rule.AppId, identity.AppId))) return true;
        if (string.IsNullOrWhiteSpace(identity.Executable)) return false;
        try
        {
            var normalized = NormalizePath(identity.Executable);
            return Array.Exists(state.Paths, path => StringComparer.OrdinalIgnoreCase.Equals(path, normalized));
        }
        catch (Exception e) when (IsStorageError(e)) { return false; }
    }

    internal bool TrySave(IEnumerable<string> paths, out string? error) => TrySave(paths, Applications, out error);

    // UI-thread operation. Publish only after the on-disk replacement succeeds.
    internal bool TrySave(IEnumerable<string> paths, IEnumerable<ApplicationExclusion> applications, out string? error)
    {
        error = null;
        string? temporary = null;
        try
        {
            if (LoadError is not null)
                throw new InvalidDataException("原配置无法读取，为防止覆盖已暂停保存。请检查 " + _file);
            var next = Normalize(paths, applications);
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            temporary = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(next,
                new JsonSerializerOptions { WriteIndented = true }));
            // Preserve an original 1.1.2-format backup on the first successful edit.
            if (_legacyFile && !File.Exists(_file + ".v1-backup"))
                File.Copy(_file, _file + ".v1-backup", overwrite: false);
            File.Move(temporary, _file, overwrite: true);
            _legacyFile = false;
            Volatile.Write(ref _state, next);
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

    private static Configuration Normalize(IEnumerable<string> paths, IEnumerable<ApplicationExclusion> apps)
    {
        var normalized = paths.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var applications = apps.Select(rule =>
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Name) ||
                !InstalledApplications.TrySplitId(rule.AppId, out var family) ||
                !StringComparer.OrdinalIgnoreCase.Equals(family, rule.Family))
                throw new InvalidDataException("应用身份格式无效。");
            return rule with { Name = rule.Name.Trim() };
        }).DistinctBy(rule => rule.AppId, StringComparer.OrdinalIgnoreCase)
          .OrderBy(rule => rule.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        return new(2, normalized, applications);
    }

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
