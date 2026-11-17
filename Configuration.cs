using Dalamud.Configuration;

namespace KosHome;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Apps the user switched off. Everything else starts (user decision 2026-11-17: all apps on).</summary>
    public HashSet<string> DisabledApps { get; set; } = [];

    /// <summary>Frosted glass behind the KosHome window (KosLib); on by default, the user asked for it.</summary>
    public bool Frosted { get; set; } = true;
}
