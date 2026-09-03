using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Winotch;

/// <summary>Downscales clipboard bitmaps to a small PNG for in-memory history.
/// Following the app's privacy stance, only this small thumbnail is kept —
/// never the original bitmap, and nothing ever touches disk.</summary>
public static class ClipboardPng
{
    private const int MaxEdge = 320;

    public static string? ToPngBase64(System.Drawing.Image source, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (source is null) return null;

        var scale = Math.Min(1.0, Math.Min(MaxEdge / (double)source.Width, MaxEdge / (double)source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, width, height);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        bytes = stream.ToArray();
        return Convert.ToBase64String(bytes);
    }
}
