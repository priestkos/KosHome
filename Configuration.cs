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

    /// <summary>Folded down to the quick launcher: a small bar of shortcuts instead of the tablet.</summary>
    public bool Compact { get; set; }

    /// <summary>App ids on the quick launcher. Null until first sanitised, then the defaults; never initialised here,
    /// because the JSON reader adds a saved list onto an initialised one and a removed shortcut would come back.</summary>
    public HashSet<string>? QuickApps { get; set; }

    public static readonly string[] DefaultQuickApps = ["Calc14", "Quill14", "Aetherpath", "WorldClock14"];

    /// <summary>Puts a hand-edited or damaged value back in range; true when something changed.</summary>
    public bool Sanitize()
    {
        var o = float.IsFinite(Opacity) ? Math.Clamp(Opacity, MinOpacity, 1f) : DefaultOpacity;
        var changed = o != Opacity || DisabledApps is null || QuickApps is null;
        Opacity = o;
        DisabledApps ??= [];
        QuickApps ??= [.. DefaultQuickApps];
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
