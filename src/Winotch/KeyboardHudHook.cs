using System.Runtime.InteropServices;

namespace Winotch;

/// <summary>A low-level keyboard hook (WH_KEYBOARD_LL) that lets Winotch claim
/// the volume keys, which is what makes replacing Windows' volume OSD possible:
/// the shell only shows its OSD when the key reaches it, so swallowing the key
/// and applying the change ourselves keeps the native OSD from ever appearing.
///
/// The callback must stay fast — it runs on this app's UI thread and Windows
/// silently drops a hook that dawdles, taking the volume keys with it — so it
/// only decides and swallows; the host applies the change on the dispatcher
/// once the hook has returned.</summary>
public sealed class KeyboardHudHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    // The hook keeps calling this delegate for the lifetime of the app, so it
    // must be held in a field: a collected delegate is a hard crash.
    private readonly LowLevelKeyboardProc _callback;
    private IntPtr _handle;

    public KeyboardHudHook()
    {
        _callback = Callback;
    }

    /// <summary>Called for each key event that reaches the hook. Return true to
    /// swallow the key so Windows never sees it.</summary>
    public Func<int, bool>? Claim { get; set; }

    /// <summary>Called for every key event the hook does <em>not</em> swallow —
    /// how the media keys are mirrored onto the pill without ever interfering
    /// with them. Runs inside the hook too, so it must only classify and post;
    /// never decide anything slow here.</summary>
    public Action<int, bool>? Observed { get; set; }

    public bool Installed => _handle != IntPtr.Zero;

    /// <summary>Install the hook. False when Windows refuses (a locked-down or
    /// sandboxed session); the HUD then simply never appears and the volume
    /// keys keep working the way Windows intends.</summary>
    public bool Install()
    {
        if (_handle != IntPtr.Zero) return true;
        try
        {
            _handle = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
        }
        catch
        {
            _handle = IntPtr.Zero;
        }
        return _handle != IntPtr.Zero;
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                var message = wParam.ToInt32();
                // Swallow the release as well as the press: a half-captured key
                // can leave the shell believing a modifier is still down.
                if (message is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp)
                {
                    var key = Marshal.PtrToStructure<KbdLlHookStruct>(lParam).VirtualKey;
                    if (Claim?.Invoke(key) == true) return 1;
                    // Not ours: report it and let it through untouched. The key
                    // is still delivered exactly as it always was.
                    Observed?.Invoke(key, message is WmKeyDown or WmSysKeyDown);
                }
            }
        }
        catch
        {
            // Never let a hook fault take the keyboard down with it.
        }
        return CallNextHookEx(_handle, code, wParam, lParam);
    }

    public void Dispose()
    {
        var handle = _handle;
        _handle = IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        try
        {
            UnhookWindowsHookEx(handle);
        }
        catch
        {
            // Already torn down with the process.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public int VirtualKey;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr handle, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
