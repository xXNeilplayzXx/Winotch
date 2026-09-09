namespace Winotch.Platform;

/// <summary>Snapshot of captured screen region for backdrop blur.</summary>
public sealed record ScreenCaptureResult(
    SkiaSharp.SKBitmap Bitmap,
    int ScreenX,
    int ScreenY);

/// <summary>Abstracts screen capture for backdrop blur.
/// Windows: System.Drawing CopyFromScreen (GDI)
/// Linux: XGetImage/XShmGetImage (X11); xdg-desktop-portal screencast via PipeWire (Wayland)</summary>
public interface IScreenCapture
{
    ScreenCaptureResult? Capture(int x, int y, int width, int height);
}
