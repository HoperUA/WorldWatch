using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WorldWatch.Models;
using WorldWatch.Services;
using WinFormsApp = System.Windows.Forms;

namespace WorldWatch;

public partial class App : System.Windows.Application
{
    // Fixed GUID so the mutex name can never collide with an unrelated app.
    private const string SingleInstanceMutexName = "WorldWatch-8f2b6e0a-3a4e-4c8a-9a4b-6a1f7a0c9e2d";

    private AppSettings _settings = null!;
    private readonly List<ClockWindow> _clocks = new();
    private DispatcherTimer _timer = null!;
    private WinFormsApp.NotifyIcon _tray = null!;
    private readonly List<TaskbarClockWindow> _taskbarClocks = new();
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance already owns the mutex; nothing left to initialize.
            Shutdown();
            return;
        }

        _settings = SettingsStore.Load();
        RebuildTaskbarClocks();

        foreach (var clockSettings in _settings.Clocks)
            CreateClockWindow(clockSettings);
        ApplyDisplayMode();

        // One shared timer drives every overlay and the taskbar block; 250ms keeps the
        // seconds digit honest without the cost of a per-window timer.
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SetupTray();
    }

    private void Tick()
    {
        var utcNow = DateTime.UtcNow;
        if (_settings.TaskbarMode)
        {
            TickTaskbar(utcNow);
            return;
        }

        foreach (var clock in _clocks)
        {
            if (clock.Settings.Visible)
            {
                clock.UpdateTime(utcNow);
                clock.KeepOnTop();
            }
        }
    }

    private void TickTaskbar(DateTime utcNow)
    {
        var visible = _clocks.Where(c => c.Settings.Visible).ToList();
        bool hideAll = visible.Count == 0 || TaskbarClockWindow.IsFullscreenAppActive();

        foreach (var block in _taskbarClocks)
        {
            if (hideAll || !block.HasTaskbar())
            {
                if (block.IsVisible)
                    block.Hide();
                continue;
            }

            block.UpdateTime(utcNow, visible);
            if (!block.IsVisible)
                block.Show();
            block.Dock();
        }
    }

    /// <summary>One block per selected monitor that is currently connected; the primary monitor when none is.</summary>
    private void RebuildTaskbarClocks()
    {
        var targets = WinFormsApp.Screen.AllScreens
            .Select(s => s.DeviceName)
            .Where(_settings.TaskbarMonitors.Contains)
            .ToList();
        if (targets.Count == 0 && WinFormsApp.Screen.PrimaryScreen is { } primary)
            targets.Add(primary.DeviceName);

        foreach (var block in _taskbarClocks.Where(b => !targets.Contains(b.DeviceName)).ToList())
        {
            block.Close();
            _taskbarClocks.Remove(block);
        }

        foreach (var deviceName in targets.Where(d => _taskbarClocks.All(b => b.DeviceName != d)))
            _taskbarClocks.Add(new TaskbarClockWindow(deviceName));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RebuildTaskbarClocks();
            Tick();
            RebuildMenu();
        });
    }

    private void SetTaskbarMonitor(string deviceName, bool selected)
    {
        // A primary-monitor fallback block is not in the list yet, so materialize what is shown before editing.
        foreach (var shown in _taskbarClocks.Select(b => b.DeviceName))
        {
            if (!_settings.TaskbarMonitors.Contains(shown))
                _settings.TaskbarMonitors.Add(shown);
        }

        _settings.TaskbarMonitors.Remove(deviceName);
        if (selected)
            _settings.TaskbarMonitors.Add(deviceName);

        RebuildTaskbarClocks();
        Tick();
        SettingsStore.Save(_settings);
        RebuildMenu();
    }

    /// <summary>Shows either the visible overlays or the taskbar block, never both.</summary>
    private void ApplyDisplayMode()
    {
        foreach (var clock in _clocks)
        {
            if (!_settings.TaskbarMode && clock.Settings.Visible)
                clock.Show();
            else
                clock.Hide();
        }

        if (!_settings.TaskbarMode)
        {
            foreach (var block in _taskbarClocks)
                block.Hide();
        }

        Tick();
    }

    private ClockWindow CreateClockWindow(ClockSettings settings)
    {
        var window = new ClockWindow(settings);
        window.PositionChanged += () => SettingsStore.Save(_settings);
        window.SettingsRequested += () => OpenSettings(window);
        _clocks.Add(window);
        return window;
    }

    private void OpenSettings(ClockWindow window)
    {
        var editor = new SettingsWindow(
            window.Settings,
            onChanged: () =>
            {
                window.ApplySettings();
                SettingsStore.Save(_settings);
            },
            onDelete: () => RemoveClock(window));

        editor.ShowDialog();
    }

    private void RemoveClock(ClockWindow window)
    {
        _settings.Clocks.Remove(window.Settings);
        _clocks.Remove(window);
        window.Close();
        ApplyDisplayMode();
        SettingsStore.Save(_settings);
        RebuildMenu();
    }

    private void AddClock()
    {
        var settings = new ClockSettings { PosX = 200, PosY = 200 };
        _settings.Clocks.Add(settings);
        var window = CreateClockWindow(settings);
        ApplyDisplayMode();
        SettingsStore.Save(_settings);
        RebuildMenu();
        OpenSettings(window); // let the user pick the zone right away
    }

    private void SetVisible(ClockWindow window, bool visible)
    {
        window.Settings.Visible = visible;
        ApplyDisplayMode();
        SettingsStore.Save(_settings);
    }

    private void SetupTray()
    {
        _tray = new WinFormsApp.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "WorldWatch",
            Visible = true,
        };

        // Double-click toggles all clocks on/off at once.
        _tray.DoubleClick += (_, _) =>
        {
            bool anyVisible = _clocks.Any(c => c.Settings.Visible);
            foreach (var clock in _clocks)
                clock.Settings.Visible = !anyVisible;
            ApplyDisplayMode();
            SettingsStore.Save(_settings);
            RebuildMenu();
        };

        RebuildMenu();
    }

    private void RebuildMenu()
    {
        var menu = new WinFormsApp.ContextMenuStrip();

        foreach (var clock in _clocks)
        {
            var label = TimeZoneLabel(clock.Settings.TimeZoneId);
            var clockMenu = new WinFormsApp.ToolStripMenuItem(label);

            var showItem = new WinFormsApp.ToolStripMenuItem("Показувати")
            {
                Checked = clock.Settings.Visible,
                CheckOnClick = true,
            };
            var captured = clock;
            showItem.Click += (_, _) =>
            {
                SetVisible(captured, showItem.Checked);
            };

            var settingsItem = new WinFormsApp.ToolStripMenuItem("Налаштування…");
            settingsItem.Click += (_, _) => OpenSettings(captured);

            clockMenu.DropDownItems.Add(showItem);
            clockMenu.DropDownItems.Add(settingsItem);
            menu.Items.Add(clockMenu);
        }

        menu.Items.Add(new WinFormsApp.ToolStripSeparator());

        var taskbarItem = new WinFormsApp.ToolStripMenuItem("На панелі задач")
        {
            Checked = _settings.TaskbarMode,
            CheckOnClick = true,
        };
        taskbarItem.Click += (_, _) =>
        {
            _settings.TaskbarMode = taskbarItem.Checked;
            ApplyDisplayMode();
            SettingsStore.Save(_settings);
        };
        menu.Items.Add(taskbarItem);
        menu.Items.Add(BuildTaskbarMonitorsMenu());

        var addItem = new WinFormsApp.ToolStripMenuItem("Додати годинник…");
        addItem.Click += (_, _) => AddClock();
        menu.Items.Add(addItem);

        var autostartItem = new WinFormsApp.ToolStripMenuItem("Запускати з Windows")
        {
            Checked = AutostartManager.IsEnabled(),
            CheckOnClick = true,
        };
        autostartItem.Click += (_, _) =>
        {
            AutostartManager.SetEnabled(autostartItem.Checked);
            _settings.StartWithWindows = autostartItem.Checked;
            SettingsStore.Save(_settings);
        };
        menu.Items.Add(autostartItem);

        menu.Items.Add(new WinFormsApp.ToolStripSeparator());

        var exitItem = new WinFormsApp.ToolStripMenuItem("Вийти");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        _tray.ContextMenuStrip = menu;
    }

    private WinFormsApp.ToolStripMenuItem BuildTaskbarMonitorsMenu()
    {
        var monitorsMenu = new WinFormsApp.ToolStripMenuItem("Монітори панелі задач");
        var shown = _taskbarClocks.Select(b => b.DeviceName).ToHashSet();

        foreach (var screen in WinFormsApp.Screen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y))
        {
            var name = screen.DeviceName.TrimStart('\\', '.');
            var text = $"{name}  {screen.Bounds.Width}×{screen.Bounds.Height}" + (screen.Primary ? "  (основний)" : "");
            var item = new WinFormsApp.ToolStripMenuItem(text)
            {
                Checked = shown.Contains(screen.DeviceName),
                CheckOnClick = true,
            };
            var deviceName = screen.DeviceName;
            item.Click += (_, _) => SetTaskbarMonitor(deviceName, item.Checked);
            monitorsMenu.DropDownItems.Add(item);
        }

        return monitorsMenu;
    }

    /// <summary>Friendly label for the tray submenu, e.g. "(UTC-05:00) Eastern Time".</summary>
    private static string TimeZoneLabel(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id).DisplayName;
        }
        catch
        {
            return id;
        }
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = Icon.ExtractAssociatedIcon(exe);
                if (icon is not null)
                    return icon;
            }
        }
        catch
        {
            // Fall through to the stock icon below.
        }

        return SystemIcons.Application;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _tray?.Dispose();
        if (_settings is not null)
            SettingsStore.Save(_settings);
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
