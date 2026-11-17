using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using KosLib;

namespace KosHome;

/// <summary>KosHome's own settings, a page of the tablet: how it looks and moves, and the quick launcher's shortcuts.
/// The apps' settings stay in each app.</summary>
internal sealed class SettingsPage(KosHomePlugin plugin)
{
    public void Draw()
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

        Kos.Section("Quick launcher", "03");
        ImGui.TextDisabled("The collapsed bar (the arrows, top left) shows these apps.");
        var quick = cfg.QuickApps!; // Sanitize filled it at load
        foreach (var app in plugin.Apps)
        {
            var on = quick.Contains(app.Info.Id);
            if (ImGui.Checkbox($"##quick{app.Info.Id}", ref on))
            {
                if (on) quick.Add(app.Info.Id); else quick.Remove(app.Info.Id);
                Save();
            }

            ImGui.SameLine();
            var size = ImGui.GetFrameHeight();
            if (MainWindow.Icon(app) is { } tex) ImGui.Image(tex.Handle, new Vector2(size));
            else ImGui.Dummy(new Vector2(size));
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(app.Info.Name);
        }

        ImGui.Spacing();
        if (Kos.IconButton(FontAwesomeIcon.Undo, "Reset look"))
        {
            cfg.ResetLook();
            Save();
        }
    }

    private void Save() => KosHomePlugin.PluginInterface.SavePluginConfig(plugin.Config);
}
