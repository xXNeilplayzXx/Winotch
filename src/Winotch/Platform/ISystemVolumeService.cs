namespace Winotch.Platform;

/// <summary>Abstracts system volume control.
/// Windows: CoreAudio COM API (IAudioEndpointVolume)
/// Linux: PulseAudio (pactl), PipeWire (pactl), or D-Bus</summary>
public interface ISystemVolumeService
{
    float Level01 { get; }
    bool IsMuted { get; }
    bool IsAvailable { get; }
    void SetLevel(float level01);
    void SetMuted(bool muted);
}
