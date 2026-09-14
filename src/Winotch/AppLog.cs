using System.IO;
using System.Text;

namespace Winotch;

/// <summary>File logging for start-up and crash diagnostics.
///
/// The notch is a windowless overlay: if something fails before the island is
/// drawn there is nothing on screen to say so, which is exactly how a start-up
/// crash ends up being reported as "it doesn't launch" with no error anywhere.
/// Every failure is appended to %LOCALAPPDATA%\Winotch\logs\winotch.log and
/// fatal ones are also surfaced in a message box, so a failure is reportable.</summary>
internal static class AppLog
{
    private const long MaxBytes = 512 * 1024;

    private static readonly object Gate = new();

    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Winotch", "logs");

    public static string LogPath { get; } = Path.Combine(DirectoryPath, "winotch.log");

    private static string PreviousLogPath => Path.Combine(DirectoryPath, "winotch.previous.log");

    /// <summary>Hook the process-wide crash handlers and drop a breadcrumb that
    /// the app got as far as starting.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write($"FATAL (unhandled): {Describe(e.ExceptionObject as Exception)}");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write($"Task failure (unobserved): {Describe(e.Exception)}");
            e.SetObserved();
        };

        Write($"--- launch {DateTime.Now:yyyy-MM-dd HH:mm:ss}, " +
              $"v{typeof(AppLog).Assembly.GetName().Version}, {Environment.OSVersion} ---");
    }

    /// <summary>Append a line to the log file. Never throws: logging must not be
    /// the thing that takes the app down.</summary>
    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(DirectoryPath);
                RotateIfLarge();
                File.AppendAllText(LogPath, message + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Nothing sensible to do if the log itself is unwritable.
        }
    }

    /// <summary>Record a recovered failure (a frame that could not be painted,
    /// a service call that failed) without interrupting the app.</summary>
    public static void Report(string context, Exception ex) =>
        Write($"{context} failed: {Describe(ex)}");

    /// <summary>Record a failure that stops the app from starting, and tell the
    /// user where the details went instead of vanishing silently.</summary>
    public static void Fatal(string context, Exception ex)
    {
        var detail = Describe(ex);
        Write($"FATAL ({context}): {detail}");
        Show($"Winotch could not start.\n\n{context}: {ex.GetType().Name}: {ex.Message}\n\n" +
             $"Details were written to:\n{LogPath}");
    }

    /// <summary>Tell the user something they need to know when no window of ours
    /// is on screen yet (Windows only; elsewhere the log is the channel).</summary>
    public static void Show(string message)
    {
#if WINDOWS
        try
        {
            System.Windows.Forms.MessageBox.Show(message, "Winotch",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
        }
        catch
        {
            // A message box is best effort.
        }
#endif
    }

    private static void RotateIfLarge()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxBytes) return;

        try
        {
            if (File.Exists(PreviousLogPath)) File.Delete(PreviousLogPath);
            File.Move(LogPath, PreviousLogPath);
        }
        catch
        {
            // If the roll fails the file just keeps growing.
        }
    }

    private static string Describe(Exception? ex)
    {
        if (ex is null) return "(no exception details)";

        var builder = new StringBuilder();
        var current = ex;
        for (var depth = 0; current is not null && depth < 6; depth++)
        {
            if (depth > 0) builder.Append(" --> ");
            builder.Append(current.GetType().FullName).Append(": ").Append(current.Message);
            current = current.InnerException;
        }
        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
            builder.AppendLine();
            builder.Append(ex.StackTrace);
        }
        return builder.ToString();
    }
}
