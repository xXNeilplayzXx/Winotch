using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Winotch;

/// <summary>System tray: Open Settings, Pause/Resume the notch, Hide/Show,
/// Start with Windows, Restart, Exit. Re-shows the icon when Explorer
/// restarts (TaskbarCreated). Bound to the app host (the manager) and a host
/// window whose HwndSource receives the TaskbarCreated message.</summary>
public sealed class TrayIconService : IDisposable
{
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private readonly INotchHost _host;
    private readonly OverlayWindow _window;
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _startupItem;
    private readonly Forms.ToolStripMenuItem _visibilityItem;
    private HwndSource? _source;

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

        _source = window.Source;
        _source?.AddHook(WndProc);
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
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
        updateItem.Click += (_, _) => Dispatch(() => _ = _host.CheckForUpdatesAsync());
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

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == TaskbarCreatedMessage)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Visible = true;
        }
        return IntPtr.Zero;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
}