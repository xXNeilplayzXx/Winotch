using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Winotch;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Winotch.SingleInstance";
    private Mutex? _singleInstanceMutex;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!TryAcquireSingleInstance(SingleInstanceMutexName, out _singleInstanceMutex))
            {
                desktop.Shutdown(0);
                return;
            }

            var settings = new SettingsService();
            var startup = new StartupService();

            // Repair stale startup paths and enforce the stored setting at boot.
            var executable = StartupService.CurrentExecutablePath();
            var state = startup.GetState(executable);
            if (state.CanAccess && settings.Current.General.StartWithWindows && !state.IsEnabled)
            {
                startup.SetEnabled(true, executable);
            }

            _manager = new OverlayWindowManager(settings, startup);
            _manager.Start();

            // First run: greet the user with the setup wizard.
            if (settings.WasCreated)
            {
                _manager.OpenSetupWizard();
            }

            desktop.ShutdownRequested += (_, _) =>
            {
                _manager?.Dispose();
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private OverlayWindowManager? _manager;

    public static bool TryAcquireSingleInstance(string mutexName, out Mutex? mutex)
    {
        mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (createdNew) return true;
        mutex.Dispose();
        mutex = null;
        return false;
    }
}
