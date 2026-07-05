using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
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

        foreach (var clockSettings in _settings.Clocks)
            CreateClockWindow(clockSettings);

        // One shared timer drives every overlay; 250ms keeps the seconds digit honest
        // without the cost of a per-window timer.
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        SetupTray();
        Tick();
    }

    private void Tick()
    {
        var utcNow = DateTime.UtcNow;
        foreach (var clock in _clocks)
        {
            if (clock.Settings.Visible)
            {
                clock.UpdateTime(utcNow);
                clock.KeepOnTop();
            }
        }
    }

    private ClockWindow CreateClockWindow(ClockSettings settings)
    {
        var window = new ClockWindow(settings);
        window.PositionChanged += () => SettingsStore.Save(_settings);
        window.SettingsRequested += () => OpenSettings(window);
        _clocks.Add(window);

        if (settings.Visible)
            window.Show();

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
        SettingsStore.Save(_settings);
        RebuildMenu();
    }

    private void AddClock()
    {
        var settings = new ClockSettings { PosX = 200, PosY = 200 };
        _settings.Clocks.Add(settings);
        var window = CreateClockWindow(settings);
        SettingsStore.Save(_settings);
        RebuildMenu();
        OpenSettings(window); // let the user pick the zone right away
    }

    private void SetVisible(ClockWindow window, bool visible)
    {
        window.Settings.Visible = visible;
        if (visible)
        {
            window.Show();
            window.UpdateTime(DateTime.UtcNow);
        }
        else
        {
            window.Hide();
        }

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

        // Double-click toggles all overlays on/off at once.
        _tray.DoubleClick += (_, _) =>
        {
            bool anyVisible = _clocks.Any(c => c.Settings.Visible);
            foreach (var clock in _clocks)
                SetVisible(clock, !anyVisible);
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
        _tray?.Dispose();
        if (_settings is not null)
            SettingsStore.Save(_settings);
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
