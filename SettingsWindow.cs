using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using KosLib;

namespace KosHome;

/// <summary>KosHome's own settings: how the tablet looks and moves. The apps' settings stay in each app.</summary>
public sealed class SettingsWindow : Window
{
    private readonly KosHomePlugin plugin;
    private KosTheme.Scope? windowTheme;

    internal SettingsWindow(KosHomePlugin plugin) : base("KosHome Settings###KosHomeSettings", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
    }

    public override void PreDraw()
    {
        windowTheme?.Dispose();
        windowTheme = KosTheme.PushWindow(KosAccent.KosMos, material: true);
    }

    public override void PostDraw()
    {
        windowTheme?.Dispose();
        windowTheme = null;
    }

    public override void Draw()
    {
        var cfg = plugin.Config;
        var s = ImGuiHelpers.GlobalScale;

        Kos.Section("Window", "01");
        var percent = cfg.Opacity * 100;
        ImGui.SetNextItemWidth(240 * s);
        if (ImGui.SliderFloat("Opacity", ref percent, Configuration.MinOpacity * 100, 100, "%.0f%%", ImGuiSliderFlags.AlwaysClamp))
            cfg.Opacity = percent / 100; // shown live; written once the slider is let go
        if (ImGui.IsItemDeactivatedAfterEdit()) Save();
        Kos.HelpMarker("How see-through the KosHome window is. Frosted glass keeps a light tint behind it even at the lowest setting.");

        var frosted = cfg.Frosted;
        if (MaterialKos.Switch("Frosted glass", ref frosted)) { cfg.Frosted = frosted; Save(); }
        var wallpaper = cfg.Wallpaper;
        if (MaterialKos.Switch("Glow wallpaper", ref wallpaper)) { cfg.Wallpaper = wallpaper; Save(); }

        Kos.Section("Motion", "02");
        var animations = cfg.Animations;
        if (MaterialKos.Switch("Animations", ref animations)) { cfg.Animations = animations; Save(); }
        if (KosHomePlugin.PluginInterface.UiBuilder.ShouldUseReducedMotion)
            ImGui.TextDisabled("Dalamud's Reduce motion is on, so KosHome keeps still.");

        ImGui.Spacing();
        if (Kos.IconButton(FontAwesomeIcon.Undo, "Reset look"))
        {
            cfg.ResetLook();
            Save();
        }
    }

    private void Save() => KosHomePlugin.PluginInterface.SavePluginConfig(plugin.Config);
}
