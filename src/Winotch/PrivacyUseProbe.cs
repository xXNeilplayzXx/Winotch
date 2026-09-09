#if WINDOWS
using Microsoft.Win32;
#endif

namespace Winotch;

/// <summary>Live microphone / camera use. An app is "in use" while
/// LastUsedTimeStart > 0 and LastUsedTimeStop == 0 (the app is currently
/// capturing).</summary>
public interface IPrivacyConsentStore
{
    bool IsMicrophoneActive();
    bool IsCameraActive();
}

#if WINDOWS

/// <summary>Reads from Windows' privacy consent store — the same source the
/// legacy app polled. Walks each per-app subkey recursively.</summary>
public sealed class RegistryPrivacyConsentStore : IPrivacyConsentStore
{
    private const string ConsentRoot = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    public bool IsMicrophoneActive() => CapabilityActive(@"microphone");

    public bool IsCameraActive() => CapabilityActive(@"webcam");

    private static bool CapabilityActive(string capability)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{ConsentRoot}\{capability}");
            if (key is null) return false;
            return KeyActive(key) || AnySubKeyActive(key);
        }
        catch
        {
            return false;
        }
    }

    private static bool KeyActive(RegistryKey key)
    {
        try
        {
            var start = ReadQWord(key, "LastUsedTimeStart");
            var stop = ReadQWord(key, "LastUsedTimeStop");
            return start > 0 && stop == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool AnySubKeyActive(RegistryKey key)
    {
        try
        {
            foreach (var subName in key.GetSubKeyNames())
            {
                using var child = key.OpenSubKey(subName);
                if (child is null) continue;
                if (KeyActive(child) || AnySubKeyActive(child)) return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private static long ReadQWord(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name) switch
            {
                long qword => qword,
                int dword => dword,
                _ => 0,
            };
        }
        catch
        {
            return 0;
        }
    }
}
#endif

/// <summary>In-memory fake for tests.</summary>
public sealed class FakePrivacyConsentStore : IPrivacyConsentStore
{
    public bool Microphone { get; set; }
    public bool Camera { get; set; }

    public bool IsMicrophoneActive() => Microphone;
    public bool IsCameraActive() => Camera;
}
