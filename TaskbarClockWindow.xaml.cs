using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using static WorldWatch.Services.NativeMethods;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace WorldWatch;

/// <summary>Compact multi-zone block docked on the taskbar just left of the notification area.</summary>
public partial class TaskbarClockWindow : Window
{
    private const double LineFontSize = 12;
    private const int GapPx = 4;

    private IntPtr _hwnd;
    private IntPtr _taskbar;
    private IntPtr _tray;
    private IntPtr _zOrderHook;
    private readonly WinEventProc _onZOrderChanged;
    private readonly List<(TextBlock Label, TextBlock Date, TextBlock Time)> _lines = new();
    private SolidColorBrush _foreground = ThemeForeground();

    public TaskbarClockWindow()
    {
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

    /// <summary>The taskbar is hidden while a fullscreen app, game or presentation is in front.</summary>
    public static bool IsFullscreenAppActive() =>
        SHQueryUserNotificationState(out int state) == 0
        && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;

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
    /// Re-anchors the block to the left of TrayNotifyWnd and re-asserts topmost. Works in raw
    /// Win32 pixels end to end so no DIP conversion is needed across monitors with different DPI.
    /// </summary>
    public void Dock()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        // SizeToContent resizes the HWND only on a layout pass; without this the width is one text change behind.
        UpdateLayout();

        if (!TryGetTaskbarRects(out var bar, out var notify) || !GetWindowRect(_hwnd, out var self))
            return;

        int width = self.Right - self.Left;
        int height = self.Bottom - self.Top;
        int x = notify.Left - width - GapPx;
        int y = bar.Top + (bar.Bottom - bar.Top - height) / 2;

        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Uses cached shell handles and re-resolves them once if explorer.exe was restarted.</summary>
    private bool TryGetTaskbarRects(out RECT bar, out RECT notify)
    {
        if (GetWindowRect(_taskbar, out bar) && GetWindowRect(_tray, out notify))
            return true;

        _taskbar = FindWindow("Shell_TrayWnd", null);
        _tray = _taskbar == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(_taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        notify = default;
        return GetWindowRect(_taskbar, out bar) && GetWindowRect(_tray, out notify);
    }

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
