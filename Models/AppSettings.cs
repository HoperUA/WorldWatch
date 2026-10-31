namespace WorldWatch.Models;

/// <summary>Root persisted document: the list of clocks plus global options.</summary>
public class AppSettings
{
    public List<ClockSettings> Clocks { get; set; } = new();

    public bool StartWithWindows { get; set; } = false;

    /// <summary>When true the visible clocks are shown as one compact block on the taskbar instead of as overlays.</summary>
    public bool TaskbarMode { get; set; } = false;

    /// <summary>Builds the first-run defaults: New York + Ukraine, stacked bottom-right.</summary>
    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            Clocks =
            {
                new ClockSettings
                {
                    TimeZoneId = "Eastern Standard Time", // New York
                    PosX = 1500,
                    PosY = 980,
                },
                new ClockSettings
                {
                    TimeZoneId = "FLE Standard Time", // Kyiv, Ukraine
                    PosX = 1500,
                    PosY = 920,
                },
            },
        };
    }
}
