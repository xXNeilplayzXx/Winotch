#if WINDOWS
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SkiaSharp;

namespace Winotch.Platform.Windows;

/// <summary>Windows clipboard service using System.Windows.Forms.Clipboard.</summary>
public sealed class WindowsClipboardService : IClipboardService
{
    public bool ContainsText() => Clipboard.ContainsText();
    public string? GetText() => Clipboard.GetText();
    public void SetText(string text) => Clipboard.SetText(text);

    public bool ContainsFiles() => Clipboard.ContainsFileDropList();

    public string[]? GetFiles()
    {
        if (!Clipboard.ContainsFileDropList()) return null;
        var list = Clipboard.GetFileDropList();
        var result = new string[list.Count];
        for (var i = 0; i < list.Count; i++) result[i] = list[i];
        return result;
    }

    public void SetFiles(string[] files)
    {
        var collection = new System.Collections.Specialized.StringCollection();
        foreach (var file in files) collection.Add(file);
        Clipboard.SetFileDropList(collection);
    }

    public bool ContainsImage() => Clipboard.ContainsImage();

    public string? GetImageAsPngBase64(out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!Clipboard.ContainsImage()) return null;

        using var image = Clipboard.GetImage();
        if (image is null) return null;

        using var stream = new MemoryStream();
        image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        bytes = stream.ToArray();
        return Convert.ToBase64String(bytes);
    }

    public void SetImage(byte[] pngData)
    {
        using var stream = new MemoryStream(pngData);
        using var image = System.Drawing.Image.FromStream(stream);
        Clipboard.SetImage(image);
    }
}

#endif
