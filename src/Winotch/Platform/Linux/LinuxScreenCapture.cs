#if !WINDOWS
using System.Runtime.InteropServices;
using SkiaSharp;
using Winotch.Platform;

namespace Winotch;

/// <summary>Linux screen capture using XGetImage (X11). For Wayland, would
/// require xdg-desktop-portal screencast via PipeWire.</summary>
public sealed class LinuxScreenCapture : IScreenCapture
{
    public ScreenCaptureResult? Capture(int x, int y, int width, int height)
    {
        IntPtr display = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;

        try
        {
            display = XLib.XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return null;

            var rootWindow = XLib.XRootWindow(display, XLib.XDefaultScreen(display));

            // Capture the screen region
            image = XLib.XGetImage(
                display, rootWindow,
                x, y, (uint)width, (uint)height,
                AllPlanes, ZPixmap);

            if (image == IntPtr.Zero) return null;

            // Parse XImage structure
            var ximage = Marshal.PtrToStructure<XImage>(image);

            // Create SKBitmap and copy pixel data
            var skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var pixels = skBitmap.GetPixels();

            // Copy pixel data from XImage to SKBitmap
            unsafe
            {
                var src = (byte*)ximage.data.ToPointer();
                var dst = (byte*)pixels.ToPointer();
                var bytesPerLine = ximage.bytes_per_line;
                var bytesPerPixel = ximage.bits_per_pixel / 8;

                for (int row = 0; row < height; row++)
                {
                    var srcRow = src + (row * bytesPerLine);
                    var dstRow = dst + (row * width * 4);

                    for (int col = 0; col < width; col++)
                    {
                        // X11 typically uses BGRA format
                        dstRow[col * 4 + 0] = srcRow[col * bytesPerPixel + 0]; // B
                        dstRow[col * 4 + 1] = srcRow[col * bytesPerPixel + 1]; // G
                        dstRow[col * 4 + 2] = srcRow[col * bytesPerPixel + 2]; // R
                        dstRow[col * 4 + 3] = 0xFF; // A
                    }
                }
            }

            return new ScreenCaptureResult(skBitmap, x, y);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (image != IntPtr.Zero && display != IntPtr.Zero)
            {
                XLib.XDestroyImage(image);
            }
            if (display != IntPtr.Zero)
            {
                XLib.XCloseDisplay(display);
            }
        }
    }

    private const int AllPlanes = ~0;
    private const int ZPixmap = 2;
}

#region X11 P/Invoke

[StructLayout(LayoutKind.Sequential)]
internal struct XImage
{
    public int width;
    public int height;
    public int xoffset;
    public int format;
    public IntPtr data;
    public int byte_order;
    public int bitmap_unit;
    public int bitmap_bit_order;
    public int bitmap_pad;
    public int depth;
    public int bytes_per_line;
    public int bits_per_pixel;
    public ulong red_mask;
    public ulong green_mask;
    public ulong blue_mask;
    // ... other fields omitted for brevity
}

internal static partial class XLib
{
    // LibX11 is defined in LinuxOverlayWindow.cs partial class

    // Additional methods for screen capture
    [DllImport("libX11.so.6")]
    public static extern IntPtr XGetImage(
        IntPtr display, IntPtr drawable,
        int x, int y, uint width, uint height,
        long plane_mask, int format);

    [DllImport("libX11.so.6")]
    public static extern int XDestroyImage(IntPtr image);
}

#endregion
#endif
