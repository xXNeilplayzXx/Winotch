using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Winotch.Engine;

/// <summary>Real backdrop blur for glass islands. The overlay window is a
/// full-screen transparent surface, so true backdrop-filter-style blur is not
/// available — instead we capture the composited screen region behind the
/// island (physical pixels), blur it with Skia, and draw it clipped to the
/// island silhouette. The capture is cached and re-taken at most twice a
/// second (or whenever the island moves by more than the snap grid), so the
/// cost is bounded. When the capture fails (locked session, exotic setups)
/// the caller simply paints the translucent body without a backdrop.
///
/// Note: the capture includes our own island from the previous frame; because
/// the island body is translucent and the content on top is opaque, this
/// converges to a soft glow rather than feedback — the same artifact real
/// glass panels show.</summary>
public static class BackdropBlur
{
    /// <summary>Capture coords snap to this grid so expansion animation does
    /// not re-capture every frame.</summary>
    private const int SnapGrid = 8;

    /// <summary>The capture is taken at half resolution: a backdrop is already
    /// blurred, so half-res is visually indistinguishable while using about
    /// 1/4 the memory per buffer and costing ~4x less capture + blur work.</summary>
    private const double CaptureScale = 0.5;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, (SKBitmap Bitmap, DateTime At)> Cache = new();

    /// <summary>One cached backdrop per island region; with several monitors
    /// in All mode the capture is keyed so islands don't evict each other.</summary>
    private const int MaxEntries = 4;

    /// <summary>Draw a blurred copy of the screen behind <paramref name="dipRect"/>
    /// into the canvas. <paramref name="originXPx"/>/<paramref name="originYPx"/>
    /// are the overlay window's physical top-left; <paramref name="dpiScale"/>
    /// converts the DIP rect to physical pixels.</summary>
    public static void Draw(SKCanvas canvas, SKRect dipRect,
        int originXPx, int originYPx, double dpiScale, int blurRadius, byte alpha)
    {
        if (blurRadius <= 0 || alpha <= 0 || dipRect.Width <= 0 || dipRect.Height <= 0 || dpiScale <= 0)
        {
            return;
        }

        var x = originXPx + (int)Math.Round(dipRect.Left * dpiScale / SnapGrid) * SnapGrid;
        var y = originYPx + (int)Math.Round(dipRect.Top * dpiScale / SnapGrid) * SnapGrid;
        var w = Math.Max(SnapGrid, (int)Math.Round(dipRect.Width * dpiScale / SnapGrid) * SnapGrid + SnapGrid);
        var h = Math.Max(SnapGrid, (int)Math.Round(dipRect.Height * dpiScale / SnapGrid) * SnapGrid + SnapGrid);
        var cw = Math.Max(2, (int)Math.Round(w * CaptureScale));
        var ch = Math.Max(2, (int)Math.Round(h * CaptureScale));
        var cBlur = Math.Max(1, (int)Math.Round(blurRadius * CaptureScale));
        var key = $"{x},{y},{cw},{ch},{cBlur}";

        var now = DateTime.UtcNow;
        lock (Gate)
        {
            if (!Cache.TryGetValue(key, out var entry) || now - entry.At > RefreshInterval)
            {
                var captured = Capture(x, y, cw, ch, cBlur);
                if (captured is null) return;
                if (Cache.TryGetValue(key, out var old))
                {
                    old.Bitmap.Dispose();
                }
                else if (Cache.Count >= MaxEntries)
                {
                    // Evict the oldest entry (first in insertion order).
                    var oldest = Cache.OrderBy(kv => kv.Value.At).First();
                    oldest.Value.Bitmap.Dispose();
                    Cache.Remove(oldest.Key);
                }
                Cache[key] = (captured, now);
                entry = Cache[key];
            }

            using var paint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, alpha),
                IsAntialias = true,
                // The half-res capture is stretched to the island rect; linear
                // filtering keeps the stretch smooth.
                FilterQuality = SKFilterQuality.Low,
            };
            canvas.DrawBitmap(entry.Bitmap,
                new SKRect(dipRect.Left, dipRect.Top, dipRect.Right, dipRect.Bottom), paint);
        }
    }

    private static SKBitmap? Capture(int x, int y, int w, int h, int blur)
    {
        try
        {
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap(info);

            // The GDI capture bitmap is pixel-copied here and released before
            // the blur allocations below, keeping peak capture memory to three
            // buffers instead of four.
            using (var source = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(source))
                {
                    graphics.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
                }

                var rect = new Rectangle(0, 0, w, h);
                var data = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    Marshal.Copy(data.Scan0, bitmap.GetPixels(), w * h * 4);
                }
                finally
                {
                    source.UnlockBits(data);
                }
            }

            using var surface = SKSurface.Create(info);
            var surfaceCanvas = surface.Canvas;
            using var filter = SKImageFilter.CreateBlur(blur, blur, SKShaderTileMode.Clamp);
            using var blurPaint = new SKPaint { ImageFilter = filter, IsAntialias = true };
            surfaceCanvas.DrawBitmap(bitmap, 0, 0, blurPaint);
            surfaceCanvas.Flush();

            using var image = surface.Snapshot();
            return SKBitmap.FromImage(image);
        }
        catch
        {
            // Locked session, secure desktop, remote session quirks — the
            // caller falls back to a plain translucent body.
            return null;
        }
    }
}