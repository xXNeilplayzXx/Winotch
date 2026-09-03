using Winotch.Engine;

namespace Winotch.Tests;

public class EngineMathTests
{
    [Fact]
    public void Vec2_Arithmetic_Works()
    {
        var a = new Vec2(2, 3);
        var b = new Vec2(4, -1);
        Assert.Equal(new Vec2(6, 2), a + b);
        Assert.Equal(new Vec2(-2, 4), a - b);
        Assert.Equal(new Vec2(4, 6), a * 2);
        Assert.Equal(new Vec2(1, 1.5), a / 2);
    }

    [Fact]
    public void Vec2_Lerp_InterpolatesAndExtrapolates()
    {
        Assert.Equal(new Vec2(3, 4), Vec2.Lerp(new Vec2(1, 2), new Vec2(5, 6), 0.5));
        // Unclamped by design (callers clamp t themselves); t > 1 extrapolates.
        Assert.Equal(new Vec2(9, 10), Vec2.Lerp(new Vec2(1, 2), new Vec2(5, 6), 2.0));
    }

    [Fact]
    public void Col_Lerp_Intermediate_Color()
    {
        var from = new Col(0, 0, 0);
        var to = new Col(255, 255, 255);
        var mid = Col.Lerp(from, to, 0.5);
        Assert.Equal(new Col(128, 128, 128), mid);
        Assert.Equal(new Col(128, 128, 128, 128), mid.WithAlpha(128));
        Assert.Equal(new Col(255, 255, 255, 128), new Col(255, 255, 255).WithAlpha(128));
    }

    [Fact]
    public void Easings_MonotonicAndBounded()
    {
        double prev = 0;
        for (var i = 0; i <= 100; i++)
        {
            var t = Easings.EaseOutCubic(i / 100.0);
            Assert.InRange(t, 0, 1);
            Assert.True(t >= prev - 1e-9);
            prev = t;
        }
        Assert.Equal(0, Easings.EaseOutCubic(0), 3);
        Assert.Equal(1, Easings.EaseOutCubic(1), 3);
    }

    [Fact]
    public void Spring_Settles_On_Target()
    {
        var spring = new SecondOrderVec2(new Vec2(100, 100));
        var target = new Vec2(300, 500);
        for (var i = 0; i < 600; i++)
        {
            spring.Step(1.0 / 60.0, target);
        }
        var position = spring.Step(1.0 / 60.0, target);
        Assert.True(Math.Abs(position.X - target.X) < 0.5, $"X off: {position.X}");
        Assert.True(Math.Abs(position.Y - target.Y) < 0.5, $"Y off: {position.Y}");
    }

    [Fact]
    public void Spring_HasNo_Overshoot_WhenHeavilyDamped()
    {
        // Very high damping/frequency should approach monotonically.
        var spring = new SecondOrderVec2(new Vec2(0, 0), 6.0, 1.2, 0);
        var target = new Vec2(50, 50);
        var last = 0.0;
        for (var i = 0; i < 300; i++)
        {
            var p = spring.Step(1.0 / 60.0, target);
            Assert.True(p.X >= last - 1e-6, "Spring moved backwards (overshoot).");
            last = p.X;
        }
    }

    [Fact]
    public void Animator_ReportsProgressAndEnd()
    {
        var animator = new Animator(1.0, Easings.EaseOutCubic);
        var updates = new List<double>();
        animator.Updated += updates.Add;
        var finished = false;
        animator.Finished += () => finished = true;
        animator.Start();
        for (var i = 0; i < 100; i++) animator.Tick(0.02);
        Assert.True(finished);
        Assert.Equal(1.0, animator.Progress, 3);
        Assert.InRange(updates[^1], 0, 1);
    }
}
