namespace FluentFlyoutWPF.CapsFlyout;

internal enum OsdStateVisual
{
    None,
    Lock,
    Generic
}

internal sealed record OsdMessage(
    string Glyph,
    string Title,
    string Detail,
    double? Progress = null,
    TimeSpan? Duration = null,
    bool? IsEnabled = null,
    OsdStateVisual StateVisual = OsdStateVisual.None,
    double? FixedWidth = null,
    string? FixedDetailReference = null,
    bool CenterDetail = false,
    bool InputMethod = false)
{
    // Keep delayed IME notifications tied to their original foreground session.
    internal nint InputMethodForeground { get; init; }
    internal long InputMethodEpoch { get; init; }
    internal int InputMethodSession { get; init; }

    public static OsdMessage WithProgress(string glyph, string title, string detail, double progress) =>
        new(glyph, title, detail, Math.Clamp(progress, 0, 1));

    public static OsdMessage WithState(
        string glyph,
        string title,
        string detail,
        bool isEnabled,
        OsdStateVisual visual) =>
        new(glyph, title, detail, IsEnabled: isEnabled, StateVisual: visual);
}
