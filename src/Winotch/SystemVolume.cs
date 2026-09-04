using System.Runtime.InteropServices;

namespace Winotch;

/// <summary>System-wide volume and mute for the default render endpoint, via
/// the CoreAudio COM API. SMTC (the media session API Winotch watches) has no
/// volume surface, so the Now playing card's slider and mute button control
/// the system volume — the same device the keyboard volume keys drive.
/// Falls back to "unavailable" (the slider is ghosted and inert) when the
/// endpoint cannot be acquired.</summary>
public static class SystemVolume
{
    private static readonly IAudioEndpointVolume? Endpoint = Acquire();

    public static bool Available => Endpoint is not null;

    public static float Level01
    {
        get
        {
            if (Endpoint is null) return 0;
            try
            {
                Endpoint.GetMasterVolumeLevelScalar(out var level);
                return Math.Clamp(level, 0f, 1f);
            }
            catch { return 0; }
        }
    }

    public static bool Muted
    {
        get
        {
            if (Endpoint is null) return false;
            try { Endpoint.GetMute(out var mute); return mute != 0; }
            catch { return false; }
        }
    }

    public static void SetLevel(float level01)
    {
        if (Endpoint is null) return;
        try
        {
            var ctx = Guid.Empty;
            Endpoint.SetMasterVolumeLevelScalar(Math.Clamp(level01, 0f, 1f), ref ctx);
        }
        catch { }
    }

    public static void SetMuted(bool muted)
    {
        if (Endpoint is null) return;
        try
        {
            var ctx = Guid.Empty;
            Endpoint.SetMute(muted ? 1 : 0, ref ctx);
        }
        catch { }
    }

    private static IAudioEndpointVolume? Acquire()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 0 /* eConsole */, out var device);
            if (device is null) return null;
            var iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A"); // IAudioEndpointVolume
            device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out var volumePtr);
            if (volumePtr == IntPtr.Zero) return null;
            return (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(volumePtr);
        }
        catch
        {
            return null;
        }
    }

    // vtable order must match mmdeviceapi.h / endpointvolume.h exactly.

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr iface);
        int OpenPropertyStore(int access, out IntPtr properties);
        int GetId(out IntPtr id);
        int GetState(out int state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out int count);
        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(int channel, float levelDb, ref Guid eventContext);
        int SetChannelVolumeLevelScalar(int channel, float level, ref Guid eventContext);
        int GetChannelVolumeLevel(int channel, out float levelDb);
        int GetChannelVolumeLevelScalar(int channel, out float level);
        int SetMute(int mute, ref Guid eventContext);
        int GetMute(out int mute);
        int SetVolumeStep(ref Guid eventContext);
        int GetVolumeStep(ref Guid eventContext);
        int GetVolumeStepInfo(out int step, out int stepCount);
        int QueryHardwareSupport(out int mask);
        int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }
}
