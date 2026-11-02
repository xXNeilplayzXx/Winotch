namespace Winotch.Engine;

/// <summary>One-dimensional second-order dynamics: the same integrator as
/// <see cref="SecondOrderVec2"/>, for values that are a single number — the
/// volume bar, an opacity, a progress fill.
///
/// A plain exponential lerp ("move a fixed fraction of the way each frame")
/// cannot do what a spring does here: it has no velocity, so a value that is
/// re-targeted half-way through arrives with a visible seam, whereas a spring
/// carries the bar's own speed into the new target and the motion stays
/// continuous. That is the whole reason a held volume key feels like one slide
/// rather than a series of jumps.</summary>
public sealed class SecondOrderScalar
{
    private double _previousTarget;
    private double _position;
    private double _velocity;
    private double _k1;
    private double _k2;
    private double _k3;

    public SecondOrderScalar(double initial,
        double frequency = 2.6, double damping = 1.0, double response = 0.1)
    {
        _position = initial;
        _previousTarget = initial;
        Configure(frequency, damping, response);
    }

    /// <summary><paramref name="damping"/> is a ratio: 1 is critically damped
    /// (no overshoot), below 1 overshoots. Use 1 for readouts that follow a
    /// value, and only go under it when a gesture carried momentum.</summary>
    public void Configure(double frequency, double damping, double response)
    {
        _k1 = damping / (Math.PI * frequency);
        _k2 = 1.0 / Math.Pow(2.0 * Math.PI * frequency, 2);
        _k3 = response * damping / (2.0 * Math.PI * frequency);
    }

    /// <summary>Advance the simulation by <paramref name="deltaTime"/> seconds
    /// toward <paramref name="target"/>; returns the smoothed value.</summary>
    public double Step(double deltaTime, double target)
    {
        if (deltaTime <= 0) return _position;

        var derived = (target - _previousTarget) / deltaTime;
        _previousTarget = target;

        var k2Stable = Math.Max(_k2, Math.Max(
            deltaTime * deltaTime / 2 + deltaTime * _k1 / 2,
            deltaTime * _k1));

        // Velocity first, then position: the value returned has to be the one at
        // the end of the tick just simulated. Integrating position first returns
        // the previous frame's value, so a retarget reads as no change at all for
        // one frame — the lag this spring exists to remove.
        _velocity += ((target + _k3 * derived) - _position - _k1 * _velocity) *
                     (deltaTime / k2Stable);
        _position += _velocity * deltaTime;
        return _position;
    }

    /// <summary>Jump to a value and forget the motion (a session change, not a
    /// level change).</summary>
    public void Snap(double value)
    {
        _position = value;
        _previousTarget = value;
        _velocity = 0;
    }
}

/// <summary>Second-order dynamics ("spring") driving the island geometry toward
/// a moving target. Smooth and responsive with no instability; parameters are
/// frequency, damping and response. The formulation is the classic
/// second-order integrator used by animation toolkits: tune frequency down and
/// damping up for slower, calmer motion; raise frequency for a snappier morph.</summary>
public sealed class SecondOrderVec2
{
    private Vec2 _previousTarget;
    private Vec2 _position;
    private Vec2 _velocity;
    private double _k1;
    private double _k2;
    private double _k3;

    public SecondOrderVec2(Vec2 initial,
        double frequency = 2.6, double damping = 0.62, double response = 0.1)
    {
        _position = initial;
        _previousTarget = initial;
        Configure(frequency, damping, response);
    }

    public void Configure(double frequency, double damping, double response)
    {
        _k1 = damping / (Math.PI * frequency);
        _k2 = 1.0 / Math.Pow(2.0 * Math.PI * frequency, 2);
        _k3 = response * damping / (2.0 * Math.PI * frequency);
    }

    /// <summary>Advance the simulation by <paramref name="deltaTime"/> seconds
    /// toward <paramref name="target"/>; returns the smoothed position.</summary>
    public Vec2 Step(double deltaTime, Vec2 target)
    {
        if (deltaTime <= 0) return _position;

        var derived = (target - _previousTarget) / deltaTime;
        _previousTarget = target;

        var k2Stable = Math.Max(_k2, Math.Max(
            deltaTime * deltaTime / 2 + deltaTime * _k1 / 2,
            deltaTime * _k1));

        _position += _velocity * deltaTime;
        _velocity += ((target + _k3 * derived) - _position - _k1 * _velocity) *
                     (deltaTime / k2Stable);
        return _position;
    }
}
