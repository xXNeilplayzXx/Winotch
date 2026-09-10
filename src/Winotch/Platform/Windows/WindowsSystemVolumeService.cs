#if WINDOWS
namespace Winotch.Platform.Windows;

/// <summary>Windows system volume service using CoreAudio COM API.</summary>
public sealed class WindowsSystemVolumeService : ISystemVolumeService
{
    public float Level01
    {
        get => SystemVolume.Level01;
    }

    public bool IsMuted
    {
        get => SystemVolume.Muted;
    }

    public bool IsAvailable
    {
        get => SystemVolume.Available;
    }

    public void SetLevel(float level01)
    {
        SystemVolume.SetLevel(level01);
    }

    public void SetMuted(bool muted)
    {
        SystemVolume.SetMuted(muted);
    }
}

#endif
