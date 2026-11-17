namespace KosHome;

/// <summary>Frame-rate independent animation maths for the tablet. No ImGui in here, so the host tests can prove it.</summary>
internal static class Motion
{
    /// <summary>A frame's delta, clamped: a hitch (loading screen, alt-tab) must not fling a spring across the window.</summary>
    public static float Dt(float raw) => float.IsFinite(raw) ? Math.Clamp(raw, 0f, 1f / 20f) : 0f;

    /// <summary>Exponential approach: closes the same share of the gap per second at any frame rate.</summary>
    public static float Approach(float current, float target, float dt, float rate) =>
        target + (current - target) * MathF.Exp(-rate * dt);

    /// <summary>Window alpha during the open fade. Never 0: ImGui hides a window whose style alpha is 0 at Begin(),
    /// Dalamud then skips Draw(), and the fade (advanced in Draw) would stay at 0 for good.</summary>
    public static float OpenAlpha(float fade) => MathF.Max(0.01f, EaseOutCubic(fade));

    public static float EaseOutCubic(float t)
    {
        var u = 1 - Math.Clamp(t, 0f, 1f);
        return 1 - u * u * u;
    }

    /// <summary>Ease out with a small overshoot (peaks near 1.1) for things that pop into place.</summary>
    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1;
        var u = Math.Clamp(t, 0f, 1f) - 1;
        return 1 + c3 * u * u * u + c1 * u * u;
    }

    /// <summary>0..1 progress of item <paramref name="index"/> in an entrance where each item starts
    /// <paramref name="delay"/> seconds after the one before it.</summary>
    public static float Stagger(float elapsed, int index, float delay, float duration) =>
        Math.Clamp((elapsed - index * delay) / duration, 0f, 1f);

    /// <summary>Eorzea time: one Eorzean hour is 175 real seconds, so Eorzea runs 3600/175 = 144/7 times as fast,
    /// counted from the Unix epoch.</summary>
    public static (int Hour, int Minute) EorzeaTime(DateTimeOffset now)
    {
        var seconds = now.ToUnixTimeMilliseconds() * 144 / 7 / 1000;
        return ((int)(seconds / 3600 % 24), (int)(seconds / 60 % 60));
    }
}

/// <summary>A damped spring for one value (icon scale, the island's width). Stepped in slices of at most 1/120 s, so a
/// stiff spring stays stable at 30 fps and comes out the same at 30 and 240 fps.</summary>
internal struct Spring(float value)
{
    public float Value = value;
    public float Velocity;

    public void Step(float target, float dt, float stiffness = 380f, float damping = 22f)
    {
        while (dt > 0)
        {
            var h = MathF.Min(dt, 1f / 120f);
            Velocity += (stiffness * (target - Value) - damping * Velocity) * h;
            Value += Velocity * h;
            dt -= h;
        }
    }

    public void Snap(float target)
    {
        Value = target;
        Velocity = 0;
    }
}
