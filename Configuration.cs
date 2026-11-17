using Dalamud.Configuration;

namespace KosHome;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const float MinOpacity = 0.2f, DefaultOpacity = 0.85f;

    public int Version { get; set; } = 1;

    /// <summary>Apps the user switched off. Everything else starts (user decision 2026-11-17: all apps on).</summary>
    public HashSet<string> DisabledApps { get; set; } = [];

    /// <summary>Frosted glass behind the KosHome window (KosLib); on by default, the user asked for it.</summary>
    public bool Frosted { get; set; } = true;

    /// <summary>How opaque the tablet's background is, 0.2 to 1 (KosHome Settings).</summary>
    public float Opacity { get; set; } = DefaultOpacity;

    /// <summary>The slow coloured glow drifting behind the home screen.</summary>
    public bool Wallpaper { get; set; } = true;

    /// <summary>Springs, fades and slides. Dalamud's Reduce motion turns them off as well.</summary>
    public bool Animations { get; set; } = true;

    /// <summary>Puts a hand-edited or damaged value back in range; true when something changed.</summary>
    public bool Sanitize()
    {
        var o = float.IsFinite(Opacity) ? Math.Clamp(Opacity, MinOpacity, 1f) : DefaultOpacity;
        var changed = o != Opacity || DisabledApps is null;
        Opacity = o;
        DisabledApps ??= [];
        return changed;
    }

    public void ResetLook()
    {
        Frosted = true;
        Opacity = DefaultOpacity;
        Wallpaper = true;
        Animations = true;
    }
}
