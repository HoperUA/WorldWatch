using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WorldWatch.Models;
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

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080; // keeps the window out of Alt+Tab and the taskbar
    private const int WS_EX_APPWINDOW = 0x00040000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private IntPtr _hwnd;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Mark as a tool window so it never appears in the Alt+Tab switcher.
        _hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        ex = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex);

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
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
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
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, _tz);

        string format = (Settings.Use24Hour, Settings.ShowSeconds) switch
        {
            (true, true) => "HH:mm:ss",
            (true, false) => "HH:mm",
            (false, true) => "h:mm:ss tt",
            (false, false) => "h:mm tt",
        };

        var time = local.ToString(format);
        TimeText.Text = Settings.ShowLabel && !string.IsNullOrWhiteSpace(Settings.Label)
            ? $"{Settings.Label} {time}"
            : time;
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
