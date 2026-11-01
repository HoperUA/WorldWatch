using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using static WorldWatch.Services.NativeMethods;
using Condition = System.Windows.Automation.Condition;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace WorldWatch;

/// <summary>Compact multi-zone block docked on one monitor's taskbar just left of its notification area.</summary>
public partial class TaskbarClockWindow : Window
{
    private const double LineFontSize = 12;
    private const int GapPx = 4;

    // Notification button (32px) plus its 12px edge margin at 100%, used if UI Automation finds nothing.
    private const int SecondaryTrayFallbackAt96Dpi = 44;
    private const long SecondaryTrayRefreshMs = 5000;

    public string DeviceName { get; }

    private IntPtr _hwnd;
    private int? _secondaryTrayWidth;
    private long _secondaryTrayMeasuredAt;
    private IntPtr _taskbar;
    private IntPtr _tray;
    private IntPtr _zOrderHook;
    private readonly WinEventProc _onZOrderChanged;
    private readonly List<(TextBlock Label, TextBlock Date, TextBlock Time)> _lines = new();
    private SolidColorBrush _foreground = ThemeForeground();

    public TaskbarClockWindow(string deviceName)
    {
        DeviceName = deviceName;
        InitializeComponent();

        // Off-screen until the first Dock(), so the initial Show() does not flash at the OS default spot.
        Left = -10000;
        Top = -10000;

        // The only-if-below check also stops the loop: our own SetWindowPos raises another reorder event.
        _onZOrderChanged = (_, _, _, _, _, _, _) =>
        {
            if (IsVisible && IsBelowTaskbar())
                MakeTopmost(_hwnd);
        };

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Closed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            if (_zOrderHook != IntPtr.Zero)
                UnhookWinEvent(_zOrderHook);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Click-through and never activated: the block is display-only, all control lives in the tray menu.
        _hwnd = new WindowInteropHelper(this).Handle;
        SetToolWindowStyle(_hwnd, WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);

        // Explorer re-raises the taskbar asynchronously, several times after each click; reacting to
        // the z-order change itself closes the gap a timer would leave.
        _zOrderHook = SetWinEventHook(EVENT_OBJECT_REORDER, EVENT_OBJECT_REORDER, IntPtr.Zero,
            _onZOrderChanged, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private bool IsBelowTaskbar()
    {
        for (var h = Services.NativeMethods.GetWindow(_hwnd, GW_HWNDPREV); h != IntPtr.Zero;
             h = Services.NativeMethods.GetWindow(h, GW_HWNDPREV))
        {
            if (h == _taskbar)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Windows hides a monitor's taskbar while the foreground window covers that whole monitor.
    /// A maximized window stops at the taskbar, so it does not count.
    /// </summary>
    public bool IsFullscreenAppOnMonitor()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || IsDesktop(foreground) || !IsOnMonitor(foreground, DeviceName)
            || !GetWindowRect(foreground, out var r))
            return false;

        var bounds = System.Windows.Forms.Screen.FromHandle(foreground).Bounds;
        return r.Left <= bounds.Left && r.Top <= bounds.Top && r.Right >= bounds.Right && r.Bottom >= bounds.Bottom;
    }

    private static bool IsDesktop(IntPtr hwnd)
    {
        var className = new StringBuilder(32);
        GetClassName(hwnd, className, className.Capacity);
        return className.ToString() is "Progman" or "WorkerW";
    }

    public void UpdateTime(DateTime utcNow, IReadOnlyList<ClockWindow> clocks)
    {
        EnsureLineCount(clocks.Count);
        for (int i = 0; i < clocks.Count; i++)
        {
            var (label, date, time) = _lines[i];
            label.Text = clocks[i].ShortLabel(utcNow);
            date.Text = clocks[i].FormatDate(utcNow);
            date.Visibility = date.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            time.Text = clocks[i].FormatTime(utcNow);
        }
    }

    /// <summary>
    /// Re-anchors the block to the left of the tray area and re-asserts topmost. Works in raw
    /// Win32 pixels end to end so no DIP conversion is needed across monitors with different DPI.
    /// </summary>
    /// <returns>False when this monitor currently has no taskbar (e.g. "show taskbar on all displays" is off).</returns>
    public bool Dock()
    {
        if (!TryGetAnchor(out var bar, out int anchorX))
            return false;

        if (!IsVisible)
            Show();

        // SizeToContent resizes the HWND only on a layout pass; without this the width is one text change behind.
        UpdateLayout();

        if (!GetWindowRect(_hwnd, out var self))
            return false;

        int width = self.Right - self.Left;
        int height = self.Bottom - self.Top;
        int x = anchorX - width - GapPx;
        int y = bar.Top + (bar.Bottom - bar.Top - height) / 2;

        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        return true;
    }

    public static bool HasTaskbarOn(string deviceName) => FindTaskbar(deviceName) != IntPtr.Zero;

    /// <summary>Uses cached shell handles and re-resolves them once if explorer.exe was restarted.</summary>
    private bool TryGetAnchor(out RECT bar, out int anchorX)
    {
        if (TryReadAnchor(out bar, out anchorX))
            return true;

        _taskbar = FindTaskbar(DeviceName);
        _tray = _taskbar == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(_taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        _secondaryTrayWidth = null;
        return TryReadAnchor(out bar, out anchorX);
    }

    private bool TryReadAnchor(out RECT bar, out int anchorX)
    {
        anchorX = 0;
        // Explorer reassigns its taskbar windows to other monitors when the primary display changes.
        if (!GetWindowRect(_taskbar, out bar) || !IsOnMonitor(_taskbar, DeviceName))
            return false;

        if (_tray != IntPtr.Zero)
        {
            if (!GetWindowRect(_tray, out var notify))
                return false;
            anchorX = notify.Left;
            return true;
        }

        anchorX = bar.Right - SecondaryTrayWidth(bar);
        return true;
    }

    /// <summary>Re-measured every few seconds: a UI Automation walk of explorer is too slow for every tick.</summary>
    private int SecondaryTrayWidth(RECT bar)
    {
        long now = Environment.TickCount64;
        if (_secondaryTrayWidth is not { } width || now - _secondaryTrayMeasuredAt > SecondaryTrayRefreshMs)
        {
            width = MeasureSecondaryTray(bar)
                    ?? (int)Math.Round(SecondaryTrayFallbackAt96Dpi * GetDpiForWindow(_taskbar) / 96.0);
            _secondaryTrayWidth = width;
            _secondaryTrayMeasuredAt = now;
        }
        return width;
    }

    /// <summary>
    /// Secondary taskbars have no tray HWND; their right-side area (notification button, and the clock
    /// when Windows shows it there) exists only as XAML elements whose class names start with "SystemTray.".
    /// </summary>
    private int? MeasureSecondaryTray(RECT bar)
    {
        try
        {
            double left = double.MaxValue;
            var elements = AutomationElement.FromHandle(_taskbar).FindAll(TreeScope.Descendants, Condition.TrueCondition);
            foreach (AutomationElement element in elements)
            {
                if (element.Current.ClassName.StartsWith("SystemTray.", StringComparison.Ordinal))
                    left = Math.Min(left, element.Current.BoundingRectangle.Left);
            }
            return left == double.MaxValue ? null : bar.Right - (int)left;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
            return null;
        }
    }

    private static IntPtr FindTaskbar(string deviceName)
    {
        var primary = FindWindow("Shell_TrayWnd", null);
        if (IsOnMonitor(primary, deviceName))
            return primary;

        for (var h = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null); h != IntPtr.Zero;
             h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null))
        {
            if (IsOnMonitor(h, deviceName))
                return h;
        }
        return IntPtr.Zero;
    }

    private static bool IsOnMonitor(IntPtr hwnd, string deviceName) =>
        hwnd != IntPtr.Zero && System.Windows.Forms.Screen.FromHandle(hwnd).DeviceName == deviceName;

    private void EnsureLineCount(int count)
    {
        if (_lines.Count == count)
            return;

        LinesGrid.Children.Clear();
        LinesGrid.RowDefinitions.Clear();
        _lines.Clear();

        for (int i = 0; i < count; i++)
        {
            LinesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var date = NewText(HorizontalAlignment.Left, new Thickness(0, 0, 6, 0));
            var label = NewText(HorizontalAlignment.Left, new Thickness(0, 0, 6, 0));
            var time = NewText(HorizontalAlignment.Right, new Thickness(0));
            Grid.SetRow(date, i);
            Grid.SetRow(label, i);
            Grid.SetRow(time, i);
            Grid.SetColumn(label, 1);
            Grid.SetColumn(time, 2);
            LinesGrid.Children.Add(label);
            LinesGrid.Children.Add(date);
            LinesGrid.Children.Add(time);
            _lines.Add((label, date, time));
        }
    }

    private TextBlock NewText(HorizontalAlignment alignment, Thickness margin) => new()
    {
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = LineFontSize,
        Foreground = _foreground,
        HorizontalAlignment = alignment,
        Margin = margin,
    };

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            _foreground = ThemeForeground();
            foreach (var (label, date, time) in _lines)
            {
                label.Foreground = _foreground;
                date.Foreground = _foreground;
                time.Foreground = _foreground;
            }
        });
    }

    /// <summary>The taskbar follows the system (not app) theme, which only the registry exposes.</summary>
    private static SolidColorBrush ThemeForeground()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        bool light = key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        return new SolidColorBrush(light ? Colors.Black : Colors.White);
    }
}
