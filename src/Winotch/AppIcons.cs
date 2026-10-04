using System.Collections.Concurrent;
using System.IO;
using SkiaSharp;

namespace Winotch;

/// <summary>App icons for the launcher: the widget's tiles and the launcher's
/// ⚙ settings list both show the real icon of each shortcut's target.
///
/// The icon is pulled from the executable with the shell's associated-icon
/// API, converted to PNG once, and cached, because the widget redraws every
/// frame. Anything we cannot read — a bare command name like "code.exe", a
/// missing or locked target — yields null and the caller falls back to the
/// letter tile, so a bad entry never breaks the card.</summary>
public static class AppIcons
{
    /// <summary>Icon size in device-independent pixels. The tiles draw the
    /// icon at 28px (24px at Compact), so a 32px source never upscales.</summary>
    private const int IconSize = 32;

    private static readonly ConcurrentDictionary<string, byte[]?> PngCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SKBitmap?> BitmapCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The shortcut's icon rendered to PNG bytes, or null when it has
    /// none. Cached per path so repeated draws cost nothing.</summary>
    public static byte[]? GetPng(LauncherApp app) => GetPng(app.Path);

    public static byte[]? GetPng(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return PngCache.GetOrAdd(path, ExtractPng);
    }

    /// <summary>The icon as a Skia bitmap for the widget to draw, or null.</summary>
    public static SKBitmap? GetBitmap(LauncherApp app)
    {
        if (string.IsNullOrWhiteSpace(app.Path)) return null;
        return BitmapCache.GetOrAdd(app.Path, key =>
        {
            var png = GetPng(key);
            if (png is null) return null;
            try
            {
                var bitmap = SKBitmap.Decode(png);
                if (bitmap is null) return null;
                // Decoding normalises to one known format; the tile just blits it.
                return bitmap;
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>A WPF image source for the settings list, or null.</summary>
    public static System.Windows.Media.ImageSource? GetImageSource(LauncherApp app)
    {
        var png = GetPng(app);
        if (png is null) return null;
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            using var stream = new MemoryStream(png);
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.DecodePixelWidth = IconSize;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Warm the cache off the UI thread so the first paint of the
    /// launcher card does not stall on disk.</summary>
    public static void Prewarm(IEnumerable<LauncherApp> apps)
    {
        var list = apps.Take(12).ToList();
        if (list.Count == 0) return;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            foreach (var app in list) GetPng(app);
        });
    }

    private static byte[]? ExtractPng(string path)
    {
        try
        {
            var resolved = Resolve(path);
            if (resolved is null) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(resolved);
            if (icon is null) return null;
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }
        catch
        {
            // Not an exe, unreadable, or the shell refused — fall back.
            return null;
        }
    }

    /// <summary>A launcher entry may hold an absolute path (scanned shortcuts
    /// always do) or a bare command such as "chrome.exe" (the quick-add list
    /// and hand-typed rows), which lives on PATH or in the system directories.</summary>
    private static string? Resolve(string path)
    {
        if (File.Exists(path)) return path;
        if (path.IndexOf(System.IO.Path.DirectorySeparatorChar) >= 0) return null;

        var system = Environment.SystemDirectory;
        var candidates = new[]
        {
            System.IO.Path.Combine(system, path),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
