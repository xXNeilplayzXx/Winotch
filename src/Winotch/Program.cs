using Avalonia;

namespace Winotch;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Start logging before anything else: a failure here used to leave no
        // trace at all, which is what "it doesn't launch" looks like.
        AppLog.Install();

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLog.Fatal("startup", ex);
            Environment.Exit(1);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
