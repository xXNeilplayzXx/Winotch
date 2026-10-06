namespace Winotch.Engine;

/// <summary>The fade-in / hold / fade-out clock every transient pill face runs
/// on (the level slider, the transport chip, glance chips), plus the now-playing
/// chip's pause grace. One implementation, so "how long is it still there" and
/// "how faded is it" can never disagree between two faces.</summary>
public struct FadeTimeline
{
    private readonly double _fadeIn;
    private readonly double _hold;
    private readonly double _fadeOut;
    private double _age;

    public FadeTimeline(double fadeIn, double hold, double fadeOut)
    {
        _fadeIn = Math.Max(0, fadeIn);
        _hold = Math.Max(0, hold);
        _fadeOut = Math.Max(0, fadeOut);
    }

    /// <summary>Seconds since the timeline started (or restarted).</summary>
    public double Age => _age;

    /// <summary>The seconds a fresh timeline stays fully opaque.</summary>
    public double Hold => _hold;

    /// <summary>True once the hold and the fade-out are over.</summary>
    public bool Expired => _age > _hold + _fadeOut;

    /// <summary>0 = hidden, 1 = fully visible.</summary>
    public double Opacity
    {
        get
        {
            if (_fadeIn > 0 && _age < _fadeIn) return Easings.EaseOutCubic(_age / _fadeIn);
            if (_age <= _hold) return 1;
            if (_fadeOut <= 0) return 0;
            return Math.Max(0, 1 - Easings.EaseOutCubic((_age - _hold) / _fadeOut));
        }
    }

    public void Tick(double deltaTime) => _age += deltaTime;

    /// <summary>Start over — a repeat keystroke re-shows the face instead of
    /// flickering it off and back on.</summary>
    public void Restart() => _age = 0;
}

/// <summary>The kinds of takeover a collapsed pill can show. A face replaces the
/// usual row (LED, clock, media / call / focus chips) for as long as it lives.</summary>
public enum PillFaceKind { Drag, Level, Transport, Glance }

/// <summary>Something other than the normal row that owns the collapsed pill:
/// drag mode's Done / Reset chips, the volume / brightness slider, the transport
/// chip, a glance chip. A face carries its own lifetime and the span it needs,
/// so the pill's size, its contents, its repaint needs and the vertical
/// (rotated) drawing path are all answered from the stack instead of each being
/// a separate branch per feature.</summary>
public abstract class PillFace
{
    /// <summary>Stamped by the stack; newest wins among the transient faces.</summary>
    public long Sequence { get; internal set; }

    public abstract PillFaceKind Kind { get; }

    /// <summary>The span the pill needs along its long axis: the width of a
    /// horizontal pill, the length of a side capsule.</summary>
    public abstract double Span { get; }

    /// <summary>True once the face has faded out and the pill should fall back
    /// to the next live face — or to its normal row.</summary>
    public abstract bool Expired { get; }

    /// <summary>Sticky faces hold the pill until they are removed explicitly
    /// (drag mode); transient faces yield to whatever fired last, so the pill
    /// always answers the most recent thing the user did.</summary>
    public virtual bool Sticky => false;

    public abstract void Tick(double deltaTime);
}

/// <summary>Drag mode (started from Settings): Done / Reset inside the pill,
/// held until the mode is exited.</summary>
public sealed class DragFace : PillFace
{
    public DragFace(double span) => Span = span;

    public override PillFaceKind Kind => PillFaceKind.Drag;
    public override double Span { get; }
    public override bool Sticky => true;
    public override bool Expired => false;
    public override void Tick(double deltaTime) { }
}

/// <summary>The volume / brightness slider. The face owns the <see cref="Hud"/>
/// so a repeat reading can fold into the live one and keep easing the bar.</summary>
public sealed class LevelFace : PillFace
{
    public LevelFace(Hud hud, double span)
    {
        Hud = hud;
        Span = span;
    }

    public Hud Hud { get; }

    public override PillFaceKind Kind => PillFaceKind.Level;
    public override double Span { get; }
    public override bool Expired => Hud.Expired;
    public override void Tick(double deltaTime) => Hud.Tick(deltaTime);
}

/// <summary>What a media keypress puts on the pill. The label names the key that
/// was pressed rather than claiming a playback state: the key is handled by
/// whatever app owns the session, and Winotch only learns the new state on the
/// next SMTC poll — so naming the action can never be wrong.</summary>
public readonly record struct TransportSpec(MediaKeyAction Action, string Glyph, string Label);

public sealed class TransportFace : PillFace
{
    private const double FadeIn = 0.08;
    private const double Hold = 1.05;
    private const double FadeOut = 0.24;

    private FadeTimeline _timeline = new(FadeIn, Hold, FadeOut);

    public TransportFace(TransportSpec spec, double span)
    {
        Spec = spec;
        Span = span;
    }

    public TransportSpec Spec { get; }

    public override PillFaceKind Kind => PillFaceKind.Transport;
    public override double Span { get; }
    public override bool Expired => _timeline.Expired;
    public double Opacity => _timeline.Opacity;
    public override void Tick(double deltaTime) => _timeline.Tick(deltaTime);
}

/// <summary>A glance chip: the pill-sized version of a toast, for things that
/// want awareness rather than attention (rain on the way, the charger going in,
/// something copied). A <c>Fill01</c> draws a hairline under the text, which is
/// how the charging chip keeps the toast's flourish.</summary>
public readonly record struct GlanceSpec(
    string Glyph,
    string Label,
    string? Detail = null,
    double Seconds = 3.2,
    double? Fill01 = null,
    bool Emoji = true);

public sealed class GlanceFace : PillFace
{
    private const double FadeIn = 0.10;
    private const double FadeOut = 0.26;

    // Not readonly: FadeTimeline is a mutable struct, and a readonly field would
    // tick a defensive copy and never age (the same reason Hud's own timeline
    // field is writable).
    private FadeTimeline _timeline;

    public GlanceFace(GlanceSpec spec, double span)
    {
        Spec = spec;
        Span = span;
        _timeline = new FadeTimeline(FadeIn, Math.Max(0.4, spec.Seconds), FadeOut);
    }

    public GlanceSpec Spec { get; }

    public override PillFaceKind Kind => PillFaceKind.Glance;
    public override double Span { get; }
    public override bool Expired => _timeline.Expired;
    public double Opacity => _timeline.Opacity;
    public bool HasFill => Spec.Fill01 is not null;
    public double Fill => Math.Clamp(Spec.Fill01 ?? 0, 0, 1);
    public override void Tick(double deltaTime) => _timeline.Tick(deltaTime);
}

/// <summary>The live takeovers of the collapsed pill. Exactly one face wins:
/// a sticky face (drag mode) outranks everything, and otherwise the most
/// recently shown face does — so the pill always answers the last thing the
/// user did. Adding a face of a kind that is already live replaces it, which is
/// why holding a volume key slides one slider instead of stacking sliders.</summary>
public sealed class PillFaceStack
{
    private readonly List<PillFace> _faces = new();
    private long _sequence;

    /// <summary>Faces held, expired ones included until the next tick.</summary>
    public int Count => _faces.Count;

    /// <summary>True while any face is held (drives the once-a-frame repaint).</summary>
    public bool Any => _faces.Count > 0;

    /// <summary>The face that owns the pill, or null for the normal row.</summary>
    public PillFace? Live
    {
        get
        {
            PillFace? best = null;
            foreach (var face in _faces)
            {
                if (face.Expired) continue;
                if (best is null || Beats(face, best)) best = face;
            }
            return best;
        }
    }

    /// <summary>True while a face is still on the pill, so the overlay keeps
    /// repainting through its fades.</summary>
    public bool NeedsLiveUpdates => Live is not null;

    public void Add(PillFace face)
    {
        Remove(face.Kind);
        face.Sequence = ++_sequence;
        _faces.Add(face);
    }

    /// <summary>Re-stamp a face as the newest, so refreshing it also lifts it
    /// back above a face that was shown after it.</summary>
    public void Touch(PillFace face) => face.Sequence = ++_sequence;

    public T? Find<T>() where T : PillFace => _faces.OfType<T>().FirstOrDefault();

    public void Remove(PillFaceKind kind) => _faces.RemoveAll(f => f.Kind == kind);

    public void Clear() => _faces.Clear();

    /// <summary>Advance every face and drop the ones that have finished.</summary>
    public void Tick(double deltaTime)
    {
        for (var i = _faces.Count - 1; i >= 0; i--)
        {
            _faces[i].Tick(deltaTime);
            if (_faces[i].Expired) _faces.RemoveAt(i);
        }
    }

    private static bool Beats(PillFace candidate, PillFace current) =>
        candidate.Sticky != current.Sticky
            ? candidate.Sticky
            : candidate.Sequence > current.Sequence;
}
