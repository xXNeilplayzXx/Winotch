namespace Winotch.Platform;

/// <summary>Abstracts clipboard operations.
/// Windows: System.Windows.Forms.Clipboard (Win32 STA)
/// Linux: X11 selections (xclip/xsel), Wayland (wl-copy/wl-paste), GTK clipboard</summary>
public interface IClipboardService
{
    bool ContainsText();
    string? GetText();
    void SetText(string text);

    bool ContainsFiles();
    string[]? GetFiles();
    void SetFiles(string[] files);

    bool ContainsImage();
    string? GetImageAsPngBase64(out byte[] bytes);
    void SetImage(byte[] pngData);
}
