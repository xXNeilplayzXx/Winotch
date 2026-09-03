namespace Winotch.Engine;

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
