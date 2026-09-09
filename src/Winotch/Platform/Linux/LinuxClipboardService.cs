using System.Diagnostics;
using SKBitmap = SkiaSharp.SKBitmap;

namespace Winotch.Platform.Linux;

/// <summary>Clipboard implementation for Linux using wl-clipboard (Wayland) or xclip/xsel (X11).</summary>
public sealed class LinuxClipboardService : IClipboardService
{
    private readonly bool _isWayland;

    public LinuxClipboardService()
    {
        _isWayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    }

    public bool ContainsText()
    {
        try
        {
            var text = GetText();
            return !string.IsNullOrEmpty(text);
        }
        catch
        {
            return false;
        }
    }

    public string? GetText()
    {
        try
        {
            if (_isWayland)
            {
                return RunCommand("wl-paste", "--no-newline");
            }
            else
            {
                // Try xclip first, fall back to xsel
                try { return RunCommand("xclip", "-selection", "clipboard", "-o"); }
                catch { return RunCommand("xsel", "--clipboard", "--output"); }
            }
        }
        catch
        {
            return null;
        }
    }

    public void SetText(string text)
    {
        if (_isWayland)
        {
            RunCommandWithInput("wl-copy", text);
        }
        else
        {
            try { RunCommandWithInput("xclip", "-selection", "clipboard", text); }
            catch { RunCommandWithInput("xsel", "--clipboard", "--input", text); }
        }
    }

    public bool ContainsFiles()
    {
        // Linux clipboard file detection is complex; return false for now
        return false;
    }

    public string[]? GetFiles()
    {
        // Not easily supported via command-line tools
        return null;
    }

    public void SetFiles(string[] files)
    {
        // Not easily supported via command-line tools
        // Could use URI lists with wl-copy -t text/uri-list
    }

    public bool ContainsImage()
    {
        // Detecting images in clipboard requires toolkit integration
        return false;
    }

    public string? GetImageAsPngBase64(out byte[] bytes)
    {
        // Not easily supported via command-line tools
        bytes = Array.Empty<byte>();
        return null;
    }

    public void SetImage(byte[] pngData)
    {
        // Would need to save to temp file and use wl-copy -t image/png or similar
        // Not implemented for now
    }

    private static string? RunCommand(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : null;
    }

    private static void RunCommandWithInput(string fileName, string input, params string[] extraArgs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in extraArgs)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null) return;

        process.StandardInput.Write(input);
        process.StandardInput.Close();
        process.WaitForExit();
    }
}
