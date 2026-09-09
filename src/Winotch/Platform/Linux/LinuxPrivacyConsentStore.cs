namespace Winotch;

#if !WINDOWS
/// <summary>Linux privacy consent store. Detecting microphone/camera use on
/// Linux requires PipeWire/PulseAudio introspection or D-Bus. Returns false
/// until a proper implementation is added.</summary>
public sealed class LinuxPrivacyConsentStore : IPrivacyConsentStore
{
    public bool IsMicrophoneActive() => false;
    public bool IsCameraActive() => false;
}
#endif
