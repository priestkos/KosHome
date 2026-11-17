using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using KosLib;

namespace KosHome;

/// <summary>The tablet: a home screen of app tiles, and an Apps page to switch apps on and off.</summary>
public sealed class MainWindow : Window
{
    private readonly KosHomePlugin plugin;
    private KosTheme.Scope? windowTheme;
    private bool appsPage;

    internal MainWindow(KosHomePlugin plugin) : base("KosHome###KosHome")
    {
        this.plugin = plugin;
        Size = new Vector2(500, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(240, 260), MaximumSize = new Vector2(1400, 1400) };
    }

    internal void OpenAppsPage()
    {
        appsPage = true;
        IsOpen = true;
    }

    /// <summary>MaterialKos in KosMos colours, pushed before ImGui begins the window. Frosted, KosTheme.Frost() blurs
    /// behind the window, so Dalamud's own blur pass is switched off.</summary>
    public override void PreDraw()
    {
        windowTheme?.Dispose();
        AllowBackgroundBlur = !plugin.Config.Frosted;
        windowTheme = KosTheme.PushWindow(KosAccent.KosMos, material: true, frosted: plugin.Config.Frosted);
    }

    /// <summary>Dalamud calls this whenever it called PreDraw, so the whole-window push is always popped.</summary>
    public override void PostDraw()
    {
        windowTheme?.Dispose();
        windowTheme = null;
    }

    public override void Draw()
    {
        KosTheme.Frost(); // nothing unless PreDraw pushed the frosted look
        DrawStatusBar();
        if (appsPage) DrawAppsPage(); else DrawHome();
    }

    private void DrawStatusBar()
    {
        if (appsPage)
        {
            if (Kos.IconButton(FontAwesomeIcon.ArrowLeft, "Home"))
                appsPage = false;
            ImGui.SameLine();
        }

        var running = plugin.Apps.Count(a => a.Instance is not null);
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled($"{running} of {plugin.Apps.Count} apps running");
        var clock = DateTime.Now.ToString("HH:mm");
        ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(clock).X);
        ImGui.TextUnformatted(clock);
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawHome()
    {
        var s = ImGuiHelpers.GlobalScale;
        var icon = 64 * s;
        var cell = new Vector2(icon + 44 * s, icon + ImGui.GetTextLineHeight() + 22 * s);
        var tiles = plugin.Apps.Count + 1; // + the Apps tile
        var avail = ImGui.GetContentRegionAvail();
        var cols = Math.Clamp((int)(avail.X / cell.X), 1, tiles);
        var origin = ImGui.GetCursorScreenPos() + new Vector2(MathF.Max(0, (avail.X - cols * cell.X) / 2), 0);

        AppSlot? open = null, toggle = null;
        for (var i = 0; i < tiles; i++)
        {
            ImGui.SetCursorScreenPos(origin + new Vector2(i % cols * cell.X, i / cols * cell.Y));
            if (i == plugin.Apps.Count)
            {
                if (Tile("##apps", "Apps", null, icon, cell, dim: false, attention: false))
                    appsPage = true;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Switch apps on and off; the look of this window.");
                continue;
            }

            var app = plugin.Apps[i];
            var on = !plugin.Config.DisabledApps.Contains(app.Info.Id);
            var tex = KosHomePlugin.TextureProvider.GetFromManifestResource(typeof(MainWindow).Assembly, $"apps.{app.Info.Id}.png");
            if (Tile($"##{app.Info.Id}", app.Info.Name, tex.TryGetWrap(out var wrap, out _) ? wrap : null, icon, cell,
                     dim: app.Instance is null, attention: app.Status is not null))
                open = app;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(app.Info.Name + "\n" + app.Info.Summary + "\n" + app.Info.Commands
                                 + (app.Status is { } st ? "\n\n" + st : on ? "" : "\n\nOff: click to switch it on and open it."));
            using (var menu = ImRaii.ContextPopupItem($"##menu{app.Info.Id}"))
            {
                if (menu)
                {
                    if (ImGui.MenuItem("Open")) open = app;
                    if (app.Info.HasSettings && ImGui.MenuItem("Settings", "", false, app.Instance is not null)) app.Instance?.OpenSettings();
                    if (ImGui.MenuItem(on ? "Switch off" : "Switch on")) toggle = app;
                }
            }
        }

        var rows = (tiles + cols - 1) / cols;
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, origin.Y));
        ImGui.Dummy(new Vector2(avail.X, rows * cell.Y)); // claim the grid so the window scrolls

        // Acted on after the loop, never while drawing the list.
        if (toggle is not null) plugin.SetEnabled(toggle, plugin.Config.DisabledApps.Contains(toggle.Info.Id));
        if (open is not null) Launch(open);
    }

    /// <summary>Tap = open. An app that is off is switched on first; one that failed or was blocked is retried.</summary>
    private void Launch(AppSlot app)
    {
        if (plugin.Config.DisabledApps.Contains(app.Info.Id)) plugin.SetEnabled(app, true);
        else if (app.Instance is null) plugin.Start(app);
        app.Instance?.OpenMain();
    }

    /// <summary>An app tile: icon (or a drawn tile with a gear), label below, an attention dot. The invisible button is
    /// the last item, so tooltips and the context menu attach to it.</summary>
    private static bool Tile(string id, string label, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? tex,
                             float icon, Vector2 cell, bool dim, bool attention)
    {
        var s = ImGuiHelpers.GlobalScale;
        var min = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, cell);
        var layer = ImGui.IsItemActive() ? MaterialKos.Press : ImGui.IsItemHovered() ? MaterialKos.Hover : 0;
        var draw = ImGui.GetWindowDrawList();
        var text = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        if (layer > 0) draw.AddRectFilled(min, min + cell, ImGui.GetColorU32(text with { W = layer }), 14 * s);

        var grow = layer > 0 ? 2 * s : 0; // hovered tiles lift a little
        var a = new Vector2(min.X + (cell.X - icon) / 2 - grow, min.Y + 8 * s - grow);
        var b = a + new Vector2(icon + 2 * grow);
        var tint = ImGui.GetColorU32(new Vector4(1, 1, 1, dim ? 0.35f : 1));
        if (tex is not null)
            draw.AddImage(tex.Handle, a, b, Vector2.Zero, Vector2.One, tint);
        else
        {
            // The family icons are 512-unit squares with rx 104, so 0.2 of the side matches their corners.
            draw.AddRectFilled(a, b, ImGui.GetColorU32(ImGuiCol.Button), (b.X - a.X) * 0.2f);
            if (id == "##apps")
            {
                var glyph = FontAwesomeIcon.Th.ToIconString();
                var size = icon * 0.45f;
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    var g = ImGui.CalcTextSize(glyph) * (size / ImGui.GetFontSize());
                    draw.AddText(UiBuilder.IconFont, size, (a + b) / 2 - g / 2, ImGui.GetColorU32(ImGuiCol.Text), glyph);
                }
            }
        }

        if (attention)
        {
            var c = new Vector2(b.X - 4 * s, a.Y + 4 * s);
            draw.AddCircleFilled(c, 7 * s, ImGui.GetColorU32(ImGuiCol.WindowBg) | 0xFF000000u, 16);
            draw.AddCircleFilled(c, 5 * s, ImGui.GetColorU32(ImGuiColors.DalamudOrange), 16);
        }

        var shown = Fit(label, cell.X - 6 * s);
        var w = ImGui.CalcTextSize(shown).X;
        draw.AddText(new Vector2(min.X + (cell.X - w) / 2, b.Y + 6 * s + grow),
                     ImGui.GetColorU32(dim ? ImGuiCol.TextDisabled : ImGuiCol.Text), shown);
        return clicked;
    }

    /// <summary>The label, cut with an ellipsis when it is wider than the tile (the tooltip has the full name).</summary>
    private static string Fit(string label, float width)
    {
        if (ImGui.CalcTextSize(label).X <= width) return label;
        for (var n = label.Length - 1; n > 1; n--)
            if (ImGui.CalcTextSize(label[..n] + "...").X <= width) return label[..n] + "...";
        return label[..1];
    }

    private void DrawAppsPage()
    {
        var s = ImGuiHelpers.GlobalScale;
        Kos.Section("Apps", "01");
        foreach (var app in plugin.Apps)
        {
            using var id = ImRaii.PushId(app.Info.Id);
            var on = !plugin.Config.DisabledApps.Contains(app.Info.Id);
            var tex = KosHomePlugin.TextureProvider.GetFromManifestResource(typeof(MainWindow).Assembly, $"apps.{app.Info.Id}.png");
            var row = ImGui.GetCursorScreenPos();
            var size = 36 * s;
            if (tex.TryGetWrap(out var wrap, out _))
                ImGui.GetWindowDrawList().AddImage(wrap.Handle, row, row + new Vector2(size), Vector2.Zero, Vector2.One,
                                                   ImGui.GetColorU32(new Vector4(1, 1, 1, on ? 1 : 0.35f)));
            ImGui.Dummy(new Vector2(size));
            ImGui.SameLine();
            using (ImRaii.Group())
            {
                ImGui.TextUnformatted(app.Info.Name);
                ImGui.SameLine();
                ImGui.TextDisabled(app.Info.Commands);
                using (ImRaii.TextWrapPos(ImGui.GetContentRegionMax().X - 90 * s))
                    ImGui.TextDisabled(app.Info.Summary);
            }

            var groupEnd = ImGui.GetItemRectMax().Y;
            var switchW = ImGui.GetFrameHeight() * 1.625f;
            ImGui.SameLine(ImGui.GetContentRegionMax().X - switchW);
            if (MaterialKos.Switch("##on", ref on))
                plugin.SetEnabled(app, on);
            ImGui.SetCursorScreenPos(new Vector2(row.X, MathF.Max(groupEnd, row.Y + size) + ImGui.GetStyle().ItemSpacing.Y));

            if (app.Status is { } status)
            {
                Kos.Callout(status, ImGuiColors.DalamudOrange);
                if (on && app.Instance is null && Kos.IconButton(FontAwesomeIcon.Redo, "Retry"))
                    plugin.Start(app);
            }

            if (app.Instance is not null)
            {
                if (Kos.IconButton(FontAwesomeIcon.WindowMaximize, "Open")) app.Instance.OpenMain();
                if (app.Info.HasSettings)
                {
                    ImGui.SameLine();
                    if (Kos.IconButton(FontAwesomeIcon.Cog, "Settings")) app.Instance?.OpenSettings();
                }
            }

            ImGui.Spacing();
        }

        Kos.Section("Look", "02");
        var frosted = plugin.Config.Frosted;
        if (MaterialKos.Switch("Frosted glass", ref frosted))
        {
            plugin.Config.Frosted = frosted;
            KosHomePlugin.PluginInterface.SavePluginConfig(plugin.Config);
        }
    }
}
