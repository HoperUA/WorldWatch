namespace WorldWatch.Models;

/// <summary>
/// State of a single overlay clock window. One instance == one on-screen clock.
/// Persisted to settings.json; everything the user can tweak lives here.
/// </summary>
public class ClockSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Windows time zone id, e.g. "Eastern Standard Time" / "FLE Standard Time".</summary>
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    public double FontSize { get; set; } = 28;

    /// <summary>ARGB hex like "#FFFFFFFF".</summary>
    public string ColorHex { get; set; } = "#FFFFFFFF";

    /// <summary>Window opacity 0.1 .. 1.0.</summary>
    public double Opacity { get; set; } = 0.9;

    /// <summary>When true a colored panel is drawn behind the digits for contrast.</summary>
    public bool ShowBackground { get; set; } = false;

    /// <summary>Background fill color as RGB hex like "#000000" (alpha comes from <see cref="BackgroundOpacity"/>).</summary>
    public string BackgroundColorHex { get; set; } = "#000000";

    /// <summary>Background fill opacity 0.0 .. 1.0, applied via the brush alpha channel so the text stays crisp.</summary>
    public double BackgroundOpacity { get; set; } = 0.5;

    public double PosX { get; set; } = 100;
    public double PosY { get; set; } = 100;

    public bool Visible { get; set; } = true;
    public bool Use24Hour { get; set; } = true;
    public bool ShowSeconds { get; set; } = true;
    public bool Bold { get; set; } = true;

    /// <summary>Optional short text shown before the time, e.g. "NY".</summary>
    public string Label { get; set; } = "";
    public bool ShowLabel { get; set; } = false;

    public string FontFamily { get; set; } = "Consolas";

    /// <summary>When true the window cannot be dragged (prevents accidental moves).</summary>
    public bool LockPosition { get; set; } = false;
}
