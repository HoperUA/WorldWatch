using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WorldWatch.Models;
using WorldWatch.Services;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace WorldWatch;

/// <summary>One borderless, transparent, always-on-top overlay bound to a single time zone.</summary>
public partial class ClockWindow : Window
{
    private TimeZoneInfo _tz;

    public ClockSettings Settings { get; }

    /// <summary>Raised after the user drags the window, so the owner can persist the new position.</summary>
    public event Action? PositionChanged;

    /// <summary>Raised when the user picks "Settings…" from this window's context menu.</summary>
    public event Action? SettingsRequested;

    public ClockWindow(ClockSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        _tz = ResolveTimeZone(settings.TimeZoneId);

        Left = settings.PosX;
        Top = settings.PosY;

        ApplySettings();
    }

    private IntPtr _hwnd;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetToolWindowStyle(_hwnd);
        KeepOnTop();
    }

    /// <summary>
    /// Re-asserts HWND_TOPMOST at the Win32 level. WPF's Topmost property alone can lose
    /// the z-order race (taskbar auto-hide flicker, explorer.exe restarting, a fullscreen
    /// app briefly grabbing the topmost band), so the app timer calls this every tick.
    /// </summary>
    public void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero)
            NativeMethods.MakeTopmost(_hwnd);
    }

    /// <summary>Re-reads appearance + time zone from <see cref="Settings"/> (after edits).</summary>
    public void ApplySettings()
    {
        _tz = ResolveTimeZone(Settings.TimeZoneId);

        TimeText.FontSize = Settings.FontSize;
        TimeText.FontFamily = new FontFamily(Settings.FontFamily);
        TimeText.FontWeight = Settings.Bold ? FontWeights.Bold : FontWeights.Normal;
        TimeText.Foreground = new SolidColorBrush(ParseColor(Settings.ColorHex));
        BackgroundBorder.Background = BuildBackgroundBrush();
        Opacity = Settings.Opacity;

        Left = Settings.PosX;
        Top = Settings.PosY;

        UpdateTime(DateTime.UtcNow);
    }

    /// <summary>Formats <paramref name="utcNow"/> for this window's zone and shows it.</summary>
    public void UpdateTime(DateTime utcNow)
    {
        var time = FormatTime(utcNow);
        TimeText.Text = Settings.ShowLabel && !string.IsNullOrWhiteSpace(Settings.Label)
            ? $"{Settings.Label} {time}"
            : time;
    }

    public string FormatTime(DateTime utcNow)
    {
        string format = (Settings.Use24Hour, Settings.ShowSeconds) switch
        {
            (true, true) => "HH:mm:ss",
            (true, false) => "HH:mm",
            (false, true) => "h:mm:ss tt",
            (false, false) => "h:mm tt",
        };

        return TimeZoneInfo.ConvertTimeFromUtc(utcNow, _tz).ToString(format);
    }

    public string FormatDate(DateTime utcNow) =>
        FormatDate(TimeZoneInfo.ConvertTimeFromUtc(utcNow, _tz), Settings.DateFormat);

    /// <summary>Empty format means no date; an invalid one is echoed back so the user sees what to fix.</summary>
    public static string FormatDate(DateTime local, string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return "";

        try
        {
            return local.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    /// <summary>The user's label, or the current UTC offset (e.g. "UTC-4") when none is set.</summary>
    public string ShortLabel(DateTime utcNow)
    {
        if (!string.IsNullOrWhiteSpace(Settings.Label))
            return Settings.Label;

        var offset = _tz.GetUtcOffset(utcNow);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return abs.Minutes == 0 ? $"UTC{sign}{abs.Hours}" : $"UTC{sign}{abs.Hours}:{abs.Minutes:00}";
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Settings.LockPosition)
            return;

        DragMove();

        // DragMove() blocks until the mouse is released; persist the resting spot.
        Settings.PosX = Left;
        Settings.PosY = Top;
        PositionChanged?.Invoke();
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>
    /// Builds the panel brush behind the digits. Opacity rides on the alpha channel
    /// (not Border.Opacity) so the text and its shadow stay fully crisp.
    /// </summary>
    private SolidColorBrush BuildBackgroundBrush()
    {
        if (!Settings.ShowBackground)
            return new SolidColorBrush(Colors.Transparent);

        var rgb = ParseColor(Settings.BackgroundColorHex);
        var alpha = (byte)Math.Round(Math.Clamp(Settings.BackgroundOpacity, 0.0, 1.0) * 255);
        return new SolidColorBrush(Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B));
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Colors.White;
        }
    }
}
