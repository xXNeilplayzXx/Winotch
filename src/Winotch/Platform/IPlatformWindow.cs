using SkiaSharp;

namespace Winotch.Platform;

/// <summary>Abstracts the overlay window with click-through support.
/// Windows: WPF Window + WM_NCHITTEST returning HTTRANSPARENT
/// Linux: X11 override-redirect with XShape; Wayland wlr-layer-shell with input_region</summary>
public interface IPlatformWindow : IDisposable
{
    /// <summary>Raised when the window needs to be redrawn.</summary>
    event Action? PaintRequested;

    /// <summary>Raised on mouse move.</summary>
    event Action<float, float>? MouseMoved;

    /// <summary>Raised on mouse button press.</summary>
    event Action<float, float, int>? MousePressed;

    /// <summary>Raised on mouse button release.</summary>
    event Action<float, float, int>? MouseReleased;

    /// <summary>Current window width in device-independent pixels.</summary>
    float Width { get; }

    /// <summary>Current window height in device-independent pixels.</summary>
    float Height { get; }

    /// <summary>Current DPI scale factor.</summary>
    float Scale { get; }

    /// <summary>Sets the window bounds in screen coordinates.</summary>
    void SetBounds(int x, int y, int width, int height);

    /// <summary>Sets the click-through region. Areas outside this region pass clicks through.</summary>
    void SetInteractiveRegion(SKRect? region);

    /// <summary>Shows the window.</summary>
    void Show();

    /// <summary>Hides the window.</summary>
    void Hide();

    /// <summary>Closes the window.</summary>
    void Close();

    /// <summary>Invalidates the window to trigger a repaint.</summary>
    void Invalidate();

    /// <summary>Gets the canvas for drawing. Call this in response to PaintRequested.</summary>
    SKCanvas? BeginPaint();

    /// <summary>Ends the paint operation and presents the frame.</summary>
    void EndPaint();
}
