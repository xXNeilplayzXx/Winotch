using System.Threading;
using System.Windows;
using Winotch.Engine;
using Application = System.Windows.Application;

namespace Winotch;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Winotch.SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        Mutex? acquiredMutex = null;
        try
        {
            if (!TryAcquireSingleInstance(SingleInstanceMutexName, out var mutex))
            {
                Shutdown(0);
                return;
            }
            acquiredMutex = mutex;
            _singleInstanceMutex = mutex;

            base.OnStartup(e);

            // Windows' "Animation effects" switch is the same signal a native app
            // reads, and it is the user's standing answer to "do you want things
            // moving on screen". Honour it now and whenever it is toggled.
            ApplyMotionPreference();
            SystemParameters.StaticPropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                {
                    ApplyMotionPreference();
                }
            };

            var settings = new SettingsService();
            var startup = new StartupService();

            // Repair stale Run-key paths and enforce the stored setting at boot.
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
        }
        catch
        {
            acquiredMutex?.ReleaseMutex();
            acquiredMutex?.Dispose();
            throw;
        }
    }

    private static void ApplyMotionPreference() =>
        IslandMotion.ReducedMotion = !SystemParameters.ClientAreaAnimation;

    private OverlayWindowManager? _manager;

    protected override void OnExit(ExitEventArgs e)
    {
        _manager?.Dispose();
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
