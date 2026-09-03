using System.Threading;
using System.Windows;
using Application = System.Windows.Application;

namespace Winotch;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Winotch.SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!TryAcquireSingleInstance(SingleInstanceMutexName, out _singleInstanceMutex))
        {
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        var settings = new SettingsService();
        var startup = new StartupService();

        // Repair stale Run-key paths and enforce the stored setting at boot.
        var executable = StartupService.CurrentExecutablePath();
        var state = startup.GetState(executable);
        if (state.CanAccess && settings.Current.General.StartWithWindows && !state.IsEnabled)
        {
            startup.SetEnabled(true, executable);
        }

        var window = new OverlayWindow(settings, startup);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    public static bool TryAcquireSingleInstance(string mutexName, out Mutex? mutex)
    {
        mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (createdNew) return true;
        mutex.Dispose();
        mutex = null;
        return false;
    }
}
