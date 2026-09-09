using SkiaSharp;

namespace Winotch;

/// <summary>Downscales clipboard bitmaps to a small PNG for in-memory history.
/// Following the app's privacy stance, only this small thumbnail is kept —
/// never the original bitmap, and nothing ever touches disk.
/// Uses SkiaSharp for cross-platform support.</summary>
public static class ClipboardPng
{
    // Rendered at ~24px in the panel, so 160 keeps quality while cutting
    // each stored thumbnail's memory ~4x vs 320.
    private const int MaxEdge = 160;

    public static string? ToPngBase64(SKBitmap source, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (source is null) return null;

        var scale = Math.Min(1.0, Math.Min(MaxEdge / (double)source.Width, MaxEdge / (double)source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var resized = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(resized))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High })
        {
            canvas.DrawBitmap(source, new SKRect(0, 0, width, height), paint);
        }

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        bytes = data.ToArray();
        return Convert.ToBase64String(bytes);
    }
}
