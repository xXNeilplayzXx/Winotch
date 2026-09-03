namespace Winotch.Engine;

/// <summary>Lightweight time-based tween used for fades and micro-animations.</summary>
public sealed class Animator
{
    private readonly double _duration;
    private readonly Func<double, double>? _easing;
    private double _elapsed;

    public Animator(double durationSeconds, Func<double, double>? easing = null)
    {
        _duration = Math.Max(0.001, durationSeconds);
        _easing = easing;
    }

    public event Action<double>? Updated;
    public event Action? Finished;

    public bool IsRunning { get; private set; }
    public double Progress { get; private set; }

    public void Start()
    {
        _elapsed = 0;
        Progress = 0;
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
    }

    public void Tick(double deltaTime)
    {
        if (!IsRunning) return;
        _elapsed += deltaTime;
        Progress = Math.Min(1, _elapsed / _duration);
        var raw = _easing is null ? Progress : _easing(Progress);
        Updated?.Invoke(raw);
        if (Progress >= 1)
        {
            IsRunning = false;
            Finished?.Invoke();
        }
    }
}
