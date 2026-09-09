#if WINDOWS
using SkiaSharp;

namespace Winotch.Platform.Windows;

/// <summary>Windows screen capture using System.Drawing (GDI CopyFromScreen).</summary>
public sealed class WindowsScreenCapture : IScreenCapture
{
    public ScreenCaptureResult? Capture(int x, int y, int width, int height)
    {
        try
        {
            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);

            graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));

            // Convert to SKBitmap
            var skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

            var bitmapData = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, width, height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                unsafe
                {
                    var ptr = (IntPtr)bitmapData.Scan0;
                    skBitmap.InstallPixels(info, ptr, bitmapData.Stride);

                    // Create a copy since InstallPixels doesn't own the memory
                    var copy = skBitmap.Copy();
                    skBitmap = copy;
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return new ScreenCaptureResult(skBitmap, x, y);
        }
        catch
        {
            return null;
        }
    }
}
#endif
