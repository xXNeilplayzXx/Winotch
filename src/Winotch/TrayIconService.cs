#if WINDOWS
using Forms = System.Windows.Forms;

namespace Winotch;

/// <summary>System tray: Open Settings, Pause/Resume the notch, Hide/Show,
/// Start with Windows, Restart, Exit. Windows implementation uses
/// System.Windows.Forms.NotifyIcon.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly INotchHost _host;
    private readonly OverlayWindow _window;
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _startupItem;
    private readonly Forms.ToolStripMenuItem _visibilityItem;

    public TrayIconService(INotchHost host, OverlayWindow window, SettingsService settings, StartupService startup)
    {
        _host = host;
        _window = window;
        _settings = settings;
        _startup = startup;

        _pauseItem = new Forms.ToolStripMenuItem();
        _startupItem = new Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        _visibilityItem = new Forms.ToolStripMenuItem();
        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = CreateContextMenu(),
            Icon = LoadTrayIcon(),
            Text = "Winotch",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatch(_host.OpenSettingsWindow);

        // TODO: TaskbarCreated handling requires Avalonia Win32 interop
    }

    public void Dispose()
    {
        var icon = _notifyIcon.Icon;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        icon?.Dispose();
    }

    private Forms.ContextMenuStrip CreateContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        var settingsItem = new Forms.ToolStripMenuItem("Open Settings");
        var setupItem = new Forms.ToolStripMenuItem("Rerun setup…");
        var updateItem = new Forms.ToolStripMenuItem("Check for updates…");
        var restartItem = new Forms.ToolStripMenuItem("Restart Winotch");
        var exitItem = new Forms.ToolStripMenuItem("Exit");
        settingsItem.Click += (_, _) => Dispatch(_host.OpenSettingsWindow);
        setupItem.Click += (_, _) => Dispatch(_host.OpenSetupWizard);
        updateItem.Click += (_, _) => Dispatch(() => { _ = _host.CheckForUpdatesAsync(); });
        _pauseItem.Click += (_, _) => Dispatch(() => _host.SetPaused(!_host.IsPaused));
        _visibilityItem.Click += (_, _) => Dispatch(() =>
        {
            if (_host.IsHidden) _host.ShowNotch();
            else _host.HideNotch();
        });
        _startupItem.Click += (_, _) => Dispatch(ToggleStartup);
        restartItem.Click += (_, _) => Dispatch(_host.RestartApplication);
        exitItem.Click += (_, _) => Dispatch(_host.QuitApplication);
        menu.Opening += (_, _) => RefreshMenuState();
        menu.Items.Add(settingsItem);
        menu.Items.Add(setupItem);
        menu.Items.Add(updateItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_visibilityItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(restartItem);
        menu.Items.Add(exitItem);
        RefreshMenuState();
        return menu;
    }

    private void ToggleStartup()
    {
        var executable = StartupService.CurrentExecutablePath();
        var current = _startup.GetState(executable);
        var updated = _startup.SetEnabled(!current.IsEnabled, executable);
        if (updated.CanAccess)
        {
            _settings.Update(s => s with
            {
                General = s.General with { StartWithWindows = updated.IsEnabled },
            });
        }
        RefreshMenuState();
    }

    private void RefreshMenuState()
    {
        _pauseItem.Text = _host.IsPaused ? "Resume notch" : "Pause notch";
        _visibilityItem.Text = _host.IsHidden ? "Show notch" : "Hide notch";
        var startup = _startup.GetState(StartupService.CurrentExecutablePath());
        _startupItem.Checked = startup.IsEnabled;
        _startupItem.Enabled = startup.CanAccess;
    }

    private void Dispatch(Action action)
    {
        if (_window.Dispatcher.CheckAccess()) action();
        else _window.Dispatcher.Invoke(action);
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(
                new Uri("pack://application:,,,/Winotch;component/Resources/WinotchTray.ico"));
            if (resource?.Stream is null) return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
            using var stream = resource.Stream;
            using var icon = new System.Drawing.Icon(stream);
            return (System.Drawing.Icon)icon.Clone();
        }
        catch
        {
            return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        }
    }

}
#endif

#if !WINDOWS
using System.Runtime.InteropServices;

namespace Winotch;

/// <summary>Linux tray icon using AppIndicator3 (GNOME) with fallback to
/// StatusNotifierItem (KDE). Creates a system tray icon with context menu.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly INotchHost _host;
    private readonly OverlayWindow _window;
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private IntPtr _indicator;
    private IntPtr _menu;
    private IntPtr _pauseItem;
    private IntPtr _visibilityItem;
    private IntPtr _startupItem;

    public TrayIconService(INotchHost host, OverlayWindow window, SettingsService settings, StartupService startup)
    {
        _host = host;
        _window = window;
        _settings = settings;
        _startup = startup;

        try
        {
            InitAppIndicator();
        }
        catch
        {
            // AppIndicator3 not available, tray icon will not work
        }
    }

    private void InitAppIndicator()
    {
        // Create AppIndicator
        _indicator = app_indicator_new("winotch", "application-x-executable", AppIndicatorCategory.ApplicationStatus);
        if (_indicator == IntPtr.Zero) return;

        app_indicator_set_status(_indicator, AppIndicatorStatus.Active);
        app_indicator_set_title(_indicator, "Winotch");

        // Create menu
        _menu = gtk_menu_new();

        // Open Settings
        var settingsItem = gtk_menu_item_new_with_label("Open Settings");
        g_signal_connect(settingsItem, "activate", new ActivateHandler(OnOpenSettings), IntPtr.Zero);
        gtk_menu_shell_append(_menu, settingsItem);

        // Rerun setup
        var setupItem = gtk_menu_item_new_with_label("Rerun setup…");
        g_signal_connect(setupItem, "activate", new ActivateHandler(OnRerunSetup), IntPtr.Zero);
        gtk_menu_shell_append(_menu, setupItem);

        // Check for updates
        var updateItem = gtk_menu_item_new_with_label("Check for updates…");
        g_signal_connect(updateItem, "activate", new ActivateHandler(OnCheckUpdates), IntPtr.Zero);
        gtk_menu_shell_append(_menu, updateItem);

        // Pause/Resume
        _pauseItem = gtk_menu_item_new_with_label("Pause notch");
        g_signal_connect(_pauseItem, "activate", new ActivateHandler(OnTogglePause), IntPtr.Zero);
        gtk_menu_shell_append(_menu, _pauseItem);

        // Show/Hide
        _visibilityItem = gtk_menu_item_new_with_label("Hide notch");
        g_signal_connect(_visibilityItem, "activate", new ActivateHandler(OnToggleVisibility), IntPtr.Zero);
        gtk_menu_shell_append(_menu, _visibilityItem);

        // Start with Windows
        _startupItem = gtk_check_menu_item_new_with_label("Start with Windows");
        g_signal_connect(_startupItem, "toggled", new ToggledHandler(OnToggleStartup), IntPtr.Zero);
        gtk_menu_shell_append(_menu, _startupItem);

        // Separator
        var separator = gtk_separator_menu_item_new();
        gtk_menu_shell_append(_menu, separator);

        // Restart
        var restartItem = gtk_menu_item_new_with_label("Restart Winotch");
        g_signal_connect(restartItem, "activate", new ActivateHandler(OnRestart), IntPtr.Zero);
        gtk_menu_shell_append(_menu, restartItem);

        // Exit
        var exitItem = gtk_menu_item_new_with_label("Exit");
        g_signal_connect(exitItem, "activate", new ActivateHandler(OnExit), IntPtr.Zero);
        gtk_menu_shell_append(_menu, exitItem);

        gtk_widget_show_all(_menu);
        app_indicator_set_menu(_indicator, _menu);

        RefreshMenuState();
    }

    public void Dispose()
    {
        if (_menu != IntPtr.Zero)
        {
            gtk_widget_destroy(_menu);
            _menu = IntPtr.Zero;
        }
        if (_indicator != IntPtr.Zero)
        {
            g_object_unref(_indicator);
            _indicator = IntPtr.Zero;
        }
    }

    private void RefreshMenuState()
    {
        if (_pauseItem != IntPtr.Zero)
        {
            var text = _host.IsPaused ? "Resume notch" : "Pause notch";
            gtk_menu_item_set_label(_pauseItem, text);
        }
        if (_visibilityItem != IntPtr.Zero)
        {
            var text = _host.IsHidden ? "Show notch" : "Hide notch";
            gtk_menu_item_set_label(_visibilityItem, text);
        }
        if (_startupItem != IntPtr.Zero)
        {
            var startup = _startup.GetState(StartupService.CurrentExecutablePath());
            gtk_check_menu_item_set_active(_startupItem, startup.IsEnabled);
            gtk_widget_set_sensitive(_startupItem, startup.CanAccess);
        }
    }

    private void OnOpenSettings(IntPtr item, IntPtr userData) => _host.OpenSettingsWindow();
    private void OnRerunSetup(IntPtr item, IntPtr userData) => _host.OpenSetupWizard();
    private void OnCheckUpdates(IntPtr item, IntPtr userData) => _ = _host.CheckForUpdatesAsync();

    private void OnTogglePause(IntPtr item, IntPtr userData)
    {
        _host.SetPaused(!_host.IsPaused);
        RefreshMenuState();
    }

    private void OnToggleVisibility(IntPtr item, IntPtr userData)
    {
        if (_host.IsHidden) _host.ShowNotch();
        else _host.HideNotch();
        RefreshMenuState();
    }

    private void OnToggleStartup(IntPtr item, IntPtr userData)
    {
        var executable = StartupService.CurrentExecutablePath();
        var current = _startup.GetState(executable);
        var updated = _startup.SetEnabled(!current.IsEnabled, executable);
        if (updated.CanAccess)
        {
            _settings.Update(s => s with
            {
                General = s.General with { StartWithWindows = updated.IsEnabled },
            });
        }
        RefreshMenuState();
    }

    private void OnRestart(IntPtr item, IntPtr userData) => _host.RestartApplication();
    private void OnExit(IntPtr item, IntPtr userData) => _host.QuitApplication();

    #region AppIndicator3 P/Invoke

    private const string LibAppIndicator = "libappindicator3.so.1";
    private const string LibGtk = "libgtk-3.so.0";
    private const string LibGObject = "libgobject-2.0.so.0";

    private enum AppIndicatorCategory { ApplicationStatus = 0 }
    private enum AppIndicatorStatus { Passive = 0, Active = 1, Attention = 2 }

    [DllImport(LibAppIndicator)]
    private static extern IntPtr app_indicator_new(string id, string icon_name, AppIndicatorCategory category);

    [DllImport(LibAppIndicator)]
    private static extern void app_indicator_set_status(IntPtr indicator, AppIndicatorStatus status);

    [DllImport(LibAppIndicator)]
    private static extern void app_indicator_set_title(IntPtr indicator, string title);

    [DllImport(LibAppIndicator)]
    private static extern void app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    [DllImport(LibGtk)]
    private static extern IntPtr gtk_menu_new();

    [DllImport(LibGtk)]
    private static extern IntPtr gtk_menu_item_new_with_label(string label);

    [DllImport(LibGtk)]
    private static extern IntPtr gtk_check_menu_item_new_with_label(string label);

    [DllImport(LibGtk)]
    private static extern IntPtr gtk_separator_menu_item_new();

    [DllImport(LibGtk)]
    private static extern void gtk_menu_shell_append(IntPtr menuShell, IntPtr child);

    [DllImport(LibGtk)]
    private static extern void gtk_widget_show_all(IntPtr widget);

    [DllImport(LibGtk)]
    private static extern void gtk_widget_destroy(IntPtr widget);

    [DllImport(LibGtk)]
    private static extern void gtk_widget_set_sensitive(IntPtr widget, bool sensitive);

    [DllImport(LibGtk)]
    private static extern void gtk_menu_item_set_label(IntPtr menuItem, string label);

    [DllImport(LibGtk)]
    private static extern void gtk_check_menu_item_set_active(IntPtr checkMenuItem, bool isActive);

    [DllImport(LibGObject)]
    private static extern void g_object_unref(IntPtr obj);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ActivateHandler(IntPtr menuItem, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ToggledHandler(IntPtr menuItem, IntPtr userData);

    [DllImport(LibGObject)]
    private static extern ulong g_signal_connect(IntPtr instance, string signal, Delegate handler, IntPtr data);

    #endregion
}
#endif