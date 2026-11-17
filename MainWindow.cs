using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using KosLib;

namespace KosHome;

/// <summary>
/// The tablet: a status bar with the island, a clock card and a grid of app icons on the home page, the Apps page with a
/// switch per app, and a dock. Everything moves through <see cref="Motion"/> and <see cref="Spring"/>, so with
/// animations off (or Dalamud's Reduce motion on) every value simply snaps to where it is going.
/// </summary>
public sealed class MainWindow : Window
{
    private enum Page { Home, Apps }

    private readonly KosHomePlugin plugin;
    private readonly Spring[] tileScale;
    private readonly float[] ripple;   // seconds since the tile was tapped; negative = no ripple
    private readonly float[] openDot;  // 0..1 the "window open" dot under each icon
    private readonly Spring[] dockScale = [new(1), new(1), new(1)];
    private KosTheme.Scope? windowTheme;
    private ImRaii.StyleDisposable? windowStyle;

    private Page page = Page.Home;
    private float pageProgress = 1, pageDirection = 1;
    private float sinceOpen = 10, clock, fade = 1;

    private string? toast;
    private AppSlot? toastApp;
    private float toastLeft;
    private Spring island = new(0);

    internal MainWindow(KosHomePlugin plugin)
        : base("KosHome###KosHomeTablet", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        var n = plugin.Apps.Count;
        tileScale = Enumerable.Repeat(new Spring(1), n).ToArray();
        ripple = Enumerable.Repeat(-1f, n).ToArray();
        openDot = new float[n];
        Size = new Vector2(440, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(300, 420), MaximumSize = new Vector2(1400, 1600) };
    }

    private bool Animate => plugin.Config.Animations && !KosHomePlugin.PluginInterface.UiBuilder.ShouldUseReducedMotion;

    private static float S => ImGuiHelpers.GlobalScale;

    public override void OnOpen()
    {
        sinceOpen = 0;
        fade = Animate ? 0 : 1;
        island.Snap(0);
    }

    /// <summary>KosLib's MaterialKos in KosMos colours, the user's opacity on the background, a rounder phone-like
    /// corner, and the open fade. Frosted, KosTheme.Frost() blurs behind the window, so Dalamud's blur is switched off.</summary>
    public override void PreDraw()
    {
        windowStyle?.Dispose();
        windowTheme?.Dispose();
        var cfg = plugin.Config;
        AllowBackgroundBlur = !cfg.Frosted;
        windowTheme = KosTheme.PushWindow(KosAccent.KosMos, material: true, frosted: cfg.Frosted);
        ImGui.SetNextWindowBgAlpha(cfg.Opacity);
        windowStyle = new ImRaii.StyleDisposable();
        windowStyle.Push(ImGuiStyleVar.WindowRounding, 18 * S);
        windowStyle.Push(ImGuiStyleVar.WindowBorderSize, 1 * S);
        windowStyle.Push(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * Motion.EaseOutCubic(fade));
    }

    /// <summary>Dalamud calls this whenever it called PreDraw, so everything PreDraw pushed is always popped.</summary>
    public override void PostDraw()
    {
        windowStyle?.Dispose();
        windowStyle = null;
        windowTheme?.Dispose();
        windowTheme = null;
    }

    public override void Draw()
    {
        KosTheme.Frost(); // nothing unless PreDraw pushed the frosted look
        KosLib.MaterialKos.Motion = Animate;
        var dt = Motion.Dt(ImGui.GetIO().DeltaTime);
        var animate = Animate;
        sinceOpen += dt;
        if (animate) clock += dt;
        fade = animate ? MathF.Min(1, fade + dt / 0.22f) : 1;
        pageProgress = animate ? MathF.Min(1, pageProgress + dt / 0.28f) : 1;

        if (plugin.Config.Wallpaper) DrawWallpaper();
        DrawStatusBar(dt, animate);

        var dockHeight = 62 * S;
        var avail = ImGui.GetContentRegionAvail();
        var size = new Vector2(avail.X, MathF.Max(40 * S, avail.Y - dockHeight - ImGui.GetStyle().ItemSpacing.Y));
        var e = Motion.EaseOutCubic(pageProgress);
        var start = ImGui.GetCursorPos();
        ImGui.SetCursorPos(start + new Vector2((1 - e) * 36 * S * pageDirection, 0));
        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * (0.15f + 0.85f * e)))
        using (var child = ImRaii.Child("##page", size, false, ImGuiWindowFlags.NoBackground | (page == Page.Home ? ImGuiWindowFlags.NoScrollbar : 0)))
        {
            if (child)
            {
                if (page == Page.Home)
                {
                    DrawClockCard();
                    DrawWidgets();
                    DrawGrid(dt, animate);
                }
                else
                {
                    DrawAppsPage();
                }
            }
        }

        ImGui.SetCursorPos(new Vector2(start.X, start.Y + size.Y + ImGui.GetStyle().ItemSpacing.Y));
        DrawDock(dt, animate, dockHeight);
    }

    // ------------------------------------------------------------------ wallpaper

    /// <summary>Three soft glows in the theme's colours drifting slowly behind everything. ImGui has no radial gradient,
    /// so each glow is a stack of faint circles. Centres stay within 20% of the middle and the radius under 42% of the
    /// short side, which keeps every glow clear of the rounded corners (the window clips square).</summary>
    private void DrawWallpaper()
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var colors = ImGui.GetStyle().Colors;
        var tints = new[] { colors[(int)ImGuiCol.CheckMark], colors[(int)ImGuiCol.PlotLines], colors[(int)ImGuiCol.ButtonActive] };
        var radius = 0.42f * MathF.Min(size.X, size.Y);
        var strength = 0.016f * plugin.Config.Opacity;
        for (var g = 0; g < tints.Length; g++)
        {
            var phase = g * 2.1f;
            var c = pos + size / 2 + new Vector2(MathF.Sin(clock * 0.11f + phase) * 0.2f * size.X,
                                                 MathF.Cos(clock * 0.09f + phase * 1.3f) * 0.2f * size.Y);
            var col = ImGui.GetColorU32(tints[g] with { W = strength });
            for (var r = 0; r < 16; r++)
                draw.AddCircleFilled(c, radius * (1 - r / 16f), col, 48);
        }
    }

    // ------------------------------------------------------------------ status bar and island

    private void DrawStatusBar(float dt, bool animate)
    {
        var draw = ImGui.GetWindowDrawList();
        var height = 24 * S;
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var mid = min.Y + height / 2;
        var text = ImGui.GetColorU32(ImGuiCol.Text);

        var now = DateTime.Now.ToString("HH:mm");
        draw.AddText(new Vector2(min.X + 6 * S, mid - ImGui.GetTextLineHeight() / 2), text, now);

        // The island: a dark pill that grows to say what just happened, then shrinks back.
        toastLeft -= dt;
        if (toastLeft <= 0) toast = null;
        var restW = 66 * S;
        var iconSize = 16 * S;
        var wantW = toast is null ? restW : MathF.Min(width - 120 * S, ImGui.CalcTextSize(toast).X + iconSize + 30 * S);
        if (animate) island.Step(wantW, dt, 260, 26); else island.Snap(wantW);
        var w = MathF.Max(restW * 0.5f, island.Value); // from 0 on open, so the island pops in
        var h = 20 * S;
        var a = new Vector2(min.X + width / 2 - w / 2, mid - h / 2);
        var b = a + new Vector2(w, h);
        draw.AddRectFilled(a, b, 0xF0000000, h / 2);
        var open = Math.Clamp((w - restW) / MathF.Max(1, wantW - restW), 0, 1);
        if (toast is not null && open > 0.6f)
        {
            var alpha = (open - 0.6f) / 0.4f;
            var x = a.X + 10 * S;
            if (toastApp is not null && Icon(toastApp) is { } tex)
            {
                draw.AddImage(tex.Handle, new Vector2(x, mid - iconSize / 2), new Vector2(x + iconSize, mid + iconSize / 2),
                              Vector2.Zero, Vector2.One, ImGui.GetColorU32(new Vector4(1, 1, 1, alpha)));
                x += iconSize + 6 * S;
            }

            using (ImRaii.PushClipRect(a, b, true))
                draw.AddText(new Vector2(x, mid - ImGui.GetTextLineHeight() / 2), ImGui.GetColorU32(new Vector4(1, 1, 1, alpha)), toast);
        }
        else
        {
            // The camera dot, as on the phone it imitates.
            draw.AddCircleFilled(new Vector2(b.X - 10 * S, mid), 3 * S, 0xFF3A3A3A, 12);
        }

        // Close: the window has no title bar.
        var r = 9 * S;
        var centre = new Vector2(min.X + width - r - 4 * S, mid);
        ImGui.SetCursorScreenPos(centre - new Vector2(r));
        if (ImGui.InvisibleButton("##close", new Vector2(2 * r))) IsOpen = false;
        var hot = ImGui.IsItemHovered();
        if (hot) ImGui.SetTooltip("Close KosHome (Esc, or /koshome)");
        draw.AddCircleFilled(centre, r, ImGui.GetColorU32(hot ? ImGuiColors.DalamudRed with { W = 0.85f } : ImGui.GetStyle().Colors[(int)ImGuiCol.Text] with { W = 0.10f }), 20);
        Glyph(draw, FontAwesomeIcon.Times, centre, 10 * S, hot ? 0xFFFFFFFF : text);

        ImGui.SetCursorScreenPos(new Vector2(min.X, min.Y + height + 6 * S));
    }

    private void Toast(AppSlot app, string message)
    {
        toastApp = app;
        toast = $"{app.Info.Name} {message}";
        toastLeft = 2.2f;
    }

    // ------------------------------------------------------------------ clock card

    private void DrawClockCard()
    {
        var draw = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;
        var height = 96 * S;
        var min = ImGui.GetCursorScreenPos() + new Vector2(0, 2 * S);
        var max = min + new Vector2(width, height);
        var colors = ImGui.GetStyle().Colors;
        var primary = colors[(int)ImGuiCol.CheckMark];
        var ink = colors[(int)ImGuiCol.Text];
        var rounding = 16 * S;

        // Glass card: a faint fill, a lighter top half for sheen, a hairline edge.
        var e = Animate ? Motion.EaseOutCubic(Motion.Stagger(sinceOpen, 0, 0, 0.35f)) : 1;
        var lift = (1 - e) * 10 * S;
        min.Y += lift;
        max.Y += lift;
        GlassFrame(draw, min, max, e);

        var now = DateTime.Now;
        var pad = 16 * S;
        var time = now.ToString("HH:mm");
        var font = plugin.ClockFont;
        Vector2 timeSize;
        if (font.Available)
        {
            using (font.Push())
            {
                timeSize = ImGui.CalcTextSize(time);
                draw.AddText(new Vector2(min.X + pad, min.Y + 12 * S), ImGui.GetColorU32(ink with { W = e }), time);
            }
        }
        else
        {
            timeSize = ImGui.CalcTextSize(time);
            draw.AddText(new Vector2(min.X + pad, min.Y + 14 * S), ImGui.GetColorU32(ink with { W = e }), time);
        }

        var line = ImGui.GetTextLineHeight();
        draw.AddText(new Vector2(min.X + pad, min.Y + 16 * S + timeSize.Y), ImGui.GetColorU32(ink with { W = 0.6f * e }), now.ToString("dddd d MMMM"));

        // Right column: Eorzea time and how many apps are running.
        var (eh, em) = Motion.EorzeaTime(DateTimeOffset.UtcNow);
        var et = $"{eh:00}:{em:00}";
        var etLabel = "Eorzea";
        var right = max.X - pad;
        var y = min.Y + 16 * S;
        var etW = ImGui.CalcTextSize(et).X;
        Glyph(draw, eh is >= 6 and < 18 ? FontAwesomeIcon.Sun : FontAwesomeIcon.Moon, new Vector2(right - etW - 12 * S, y + line / 2), line * 0.8f, ImGui.GetColorU32(primary with { W = e }));
        draw.AddText(new Vector2(right - etW, y), ImGui.GetColorU32(ink with { W = e }), et);
        draw.AddText(new Vector2(right - ImGui.CalcTextSize(etLabel).X, y + line + 2 * S), ImGui.GetColorU32(ink with { W = 0.6f * e }), etLabel);

        var running = plugin.Apps.Count(a => a.Instance is not null);
        var total = plugin.Apps.Count;
        var runText = $"{running} of {total} running";
        var dot = MathF.Round(line * 0.16f);
        var pipsW = total * 3 * dot;
        var yRun = max.Y - pad - line;
        draw.AddText(new Vector2(right - ImGui.CalcTextSize(runText).X, yRun - line - 2 * S), ImGui.GetColorU32(ink with { W = 0.6f * e }), runText);
        Kos.Pips(draw, new Vector2(right - pipsW, yRun + line / 2), running, total, ImGui.GetColorU32(primary with { W = e }), ImGui.GetColorU32(ink with { W = 0.35f * e }), dot);

        ImGui.Dummy(new Vector2(width, height + 2 * S + 12 * S));
    }

    /// <summary>The glass card look: a faint fill, a lighter top band for sheen, a hairline edge.</summary>
    private static void GlassFrame(ImDrawListPtr draw, Vector2 min, Vector2 max, float alpha)
    {
        var ink = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var rounding = 16 * S;
        draw.AddRectFilled(min, max, ImGui.GetColorU32(ink with { W = 0.06f * alpha }), rounding);
        draw.AddRectFilled(min, new Vector2(max.X, min.Y + MathF.Min(48 * S, (max.Y - min.Y) * 0.45f)),
                           ImGui.GetColorU32(ink with { W = 0.03f * alpha }), rounding, ImDrawFlags.RoundCornersTop);
        draw.AddRect(min, max, ImGui.GetColorU32(ink with { W = 0.10f * alpha }), rounding, ImDrawFlags.None, S);
    }

    // ------------------------------------------------------------------ widgets

    /// <summary>Widget apps (WorldClock14) on the home screen, each in a glass card sliding in after the clock card. The
    /// frame goes on a background channel once the body has been laid out, as KosLib's Kos.Card does.</summary>
    private void DrawWidgets()
    {
        for (var i = 0; i < plugin.Apps.Count; i++)
        {
            var app = plugin.Apps[i];
            if (!app.Info.Widget || plugin.Config.DisabledApps.Contains(app.Info.Id)) continue;
            if (app.Instance is not IKosWidget widget)
            {
                if (app.Status is { } status) Kos.Callout($"{app.Info.Name}: {status}", ImGuiColors.DalamudOrange);
                continue;
            }

            var e = Animate ? Motion.EaseOutCubic(Motion.Stagger(sinceOpen, 1, 0.06f, 0.4f)) : 1;
            var pad = new Vector2(14, 10) * S;
            var draw = ImGui.GetWindowDrawList();
            var width = ImGui.GetContentRegionAvail().X;
            var start = ImGui.GetCursorScreenPos() + new Vector2(0, (1 - e) * 12 * S);
            draw.ChannelsSplit(2);
            draw.ChannelsSetCurrent(1);
            ImGui.SetCursorScreenPos(start + pad);
            using (ImRaii.PushId(app.Info.Id))
            using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * e))
            using (ImRaii.Group())
            {
                ImGui.PushItemWidth(width - 2 * pad.X);
                using (ImRaii.TextWrapPos(start.X - ImGui.GetWindowPos().X + width - pad.X))
                {
                    ImGui.Dummy(new Vector2(width - 2 * pad.X, 0)); // the group is the card's full inner width
                    widget.DrawWidget();
                }

                ImGui.PopItemWidth();
            }

            var end = ImGui.GetItemRectMax();
            draw.ChannelsSetCurrent(0);
            GlassFrame(draw, start, new Vector2(start.X + width, end.Y + pad.Y), e);
            draw.ChannelsMerge();
            ImGui.SetCursorScreenPos(new Vector2(start.X, end.Y + pad.Y));
            ImGui.Dummy(new Vector2(width, 12 * S));
        }
    }

    // ------------------------------------------------------------------ app grid

    private void DrawGrid(float dt, bool animate)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var cols = Math.Clamp((int)(width / (96 * S)), 2, 6);
        var cellW = width / cols;
        var icon = Math.Clamp(cellW * 0.6f, 44 * S, 72 * S);
        var cell = new Vector2(cellW, icon + ImGui.GetTextLineHeight() + 26 * S);
        var origin = ImGui.GetCursorScreenPos();

        AppSlot? launch = null, toggle = null;
        var slot = 0;
        for (var i = 0; i < plugin.Apps.Count; i++)
        {
            var app = plugin.Apps[i];
            if (app.Info.Widget) continue; // drawn by DrawWidgets
            var min = origin + new Vector2(slot % cols * cell.X, slot / cols * cell.Y);
            slot++;
            ImGui.SetCursorScreenPos(min);
            var clicked = ImGui.InvisibleButton($"##{app.Info.Id}", cell);
            var hovered = ImGui.IsItemHovered();
            var held = ImGui.IsItemActive();
            var on = !plugin.Config.DisabledApps.Contains(app.Info.Id);
            var mainOpen = app.Instance?.MainOpen == true;

            if (hovered)
                ImGui.SetTooltip(app.Info.Name + "\n" + app.Info.Summary + "\n" + app.Info.Commands + "\n\n"
                                 + (app.Status ?? (!on ? "Off: click to switch it on and open it."
                                     : mainOpen ? "Open: click to close it. Right-click for more."
                                     : "Click to open. Right-click for more.")));
            using (var menu = ImRaii.ContextPopupItem($"##menu{app.Info.Id}"))
            {
                if (menu)
                {
                    if (ImGui.MenuItem(mainOpen ? "Close" : "Open")) launch = app;
                    if (app.Info.HasSettings && ImGui.MenuItem("Settings", "", false, app.Instance is not null)) app.Instance?.OpenSettings();
                    ImGui.Separator();
                    if (ImGui.MenuItem(on ? "Switch off" : "Switch on")) toggle = app;
                }
            }

            if (clicked)
            {
                launch = app;
                ripple[i] = 0;
                tileScale[i].Velocity -= 3f; // a little press-kick even on a quick tap
            }

            var target = held ? 0.92f : hovered ? 1.08f : 1f;
            if (animate) tileScale[i].Step(target, dt); else tileScale[i].Snap(target);
            openDot[i] = animate ? Motion.Approach(openDot[i], mainOpen ? 1 : 0, dt, 14) : mainOpen ? 1 : 0;
            if (ripple[i] >= 0) ripple[i] = animate && ripple[i] + dt < 0.55f ? ripple[i] + dt : -1;

            // Entrance: each icon pops in a beat after the one before it.
            var t = animate ? Motion.Stagger(sinceOpen, slot + 1, 0.035f, 0.42f) : 1;
            DrawTile(app, i, min, cell, icon, Motion.EaseOutBack(t), Motion.EaseOutCubic(t), on, hovered);
        }

        var rows = (slot + cols - 1) / cols;
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, rows * cell.Y)); // claim the grid so the page can scroll when short

        // Acted on after the loop, never while drawing the list.
        if (toggle is not null)
        {
            var turnOn = plugin.Config.DisabledApps.Contains(toggle.Info.Id);
            plugin.SetEnabled(toggle, turnOn);
            Toast(toggle, turnOn ? (toggle.Instance is null ? "couldn't start" : "switched on") : "switched off");
        }

        if (launch is not null) Launch(launch);
    }

    /// <summary>Tap = open, tap again = close. An app that is off is switched on first; one that failed or was blocked
    /// is retried.</summary>
    private void Launch(AppSlot app)
    {
        var wasOff = plugin.Config.DisabledApps.Contains(app.Info.Id);
        if (wasOff) plugin.SetEnabled(app, true);
        else if (app.Instance is null) plugin.Start(app);

        if (app.Instance is not { } instance)
        {
            Toast(app, "couldn't start");
            return;
        }

        instance.ToggleMain();
        Toast(app, wasOff ? "switched on" : instance.MainOpen ? "opened" : "closed");
    }

    private void DrawTile(AppSlot app, int i, Vector2 min, Vector2 cell, float icon, float pop, float alpha, bool on, bool hovered)
    {
        var draw = ImGui.GetWindowDrawList();
        var ink = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var scale = tileScale[i].Value * (0.6f + 0.4f * pop);
        var centre = new Vector2(min.X + cell.X / 2, min.Y + 8 * S + icon / 2 + (1 - alpha) * 14 * S - (hovered ? 2 * S : 0));
        var half = new Vector2(icon * scale / 2);
        var a = centre - half;
        var b = centre + half;
        var rounding = (b.X - a.X) * 0.2f; // the family icons are 512 squares with rx 104

        // Soft drop shadow: a few widening, fading layers.
        for (var k = 1; k <= 4; k++)
        {
            var g = new Vector2(k * 1.5f * S);
            draw.AddRectFilled(a - g + new Vector2(0, 3 * S), b + g + new Vector2(0, 3 * S),
                               ImGui.GetColorU32(new Vector4(0, 0, 0, 0.07f * alpha * (on ? 1 : 0.4f))), rounding + g.X);
        }

        var dim = app.Instance is null ? 0.38f : 1f;
        if (Icon(app) is { } tex)
            draw.AddImage(tex.Handle, a, b, Vector2.Zero, Vector2.One, ImGui.GetColorU32(new Vector4(1, 1, 1, alpha * dim)));
        else
            draw.AddRectFilled(a, b, ImGui.GetColorU32(ImGuiCol.Button, alpha), rounding);

        // Ripple from a tap.
        if (ripple[i] >= 0)
        {
            var t = ripple[i] / 0.55f;
            var e = Motion.EaseOutCubic(t);
            draw.AddCircle(centre, icon * (0.5f + 0.45f * e), ImGui.GetColorU32(ink with { W = 0.45f * (1 - t) }), 48, 2 * S);
        }

        // Something is wrong: an orange dot that breathes.
        if (app.Status is not null)
        {
            var c = new Vector2(b.X - 3 * S, a.Y + 3 * S);
            var pulse = Animate ? 0.8f * MathF.Sin(clock * 4) : 0;
            draw.AddCircleFilled(c, (7 + pulse) * S, ImGui.GetColorU32(ImGuiCol.WindowBg, 1f), 20);
            draw.AddCircleFilled(c, (5 + pulse * 0.6f) * S, ImGui.GetColorU32(ImGuiColors.DalamudOrange with { W = alpha }), 20);
        }

        // Label with a faint shadow so it reads over the glow.
        var label = Fit(app.Info.Name, cell.X - 8 * S);
        var size = ImGui.CalcTextSize(label);
        var at = new Vector2(min.X + (cell.X - size.X) / 2, min.Y + 8 * S + icon + 7 * S + (1 - alpha) * 14 * S);
        draw.AddText(at + new Vector2(0, S), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.45f * alpha)), label);
        draw.AddText(at, ImGui.GetColorU32((on ? ink : ink with { W = 0.5f }) with { W = (on ? ink.W : 0.5f) * alpha }), label);

        // Open: a small pill under the label, like a dock's running light.
        if (openDot[i] > 0.01f)
        {
            var primary = ImGui.GetStyle().Colors[(int)ImGuiCol.CheckMark];
            var w = (4 + 10 * openDot[i]) * S;
            var y = at.Y + size.Y + 5 * S;
            draw.AddRectFilled(new Vector2(centre.X - w / 2, y), new Vector2(centre.X + w / 2, y + 3 * S),
                               ImGui.GetColorU32(primary with { W = openDot[i] * alpha }), 2 * S);
        }
    }

    // ------------------------------------------------------------------ dock

    private void DrawDock(float dt, bool animate, float height)
    {
        var draw = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail().X;
        var colors = ImGui.GetStyle().Colors;
        var ink = colors[(int)ImGuiCol.Text];
        var primary = colors[(int)ImGuiCol.CheckMark];
        var r = 19 * S;
        var gap = 14 * S;
        var items = new (FontAwesomeIcon Icon, string Tip, bool Active)[]
        {
            (FontAwesomeIcon.Home, "Home", page == Page.Home),
            (FontAwesomeIcon.Th, "Apps: switch apps on and off", page == Page.Apps),
            (FontAwesomeIcon.Cog, "KosHome Settings: opacity, glass, animation", plugin.SettingsOpen),
        };
        var dockW = items.Length * 2 * r + (items.Length + 1) * gap;
        var min = ImGui.GetCursorScreenPos() + new Vector2((avail - dockW) / 2, (height - (2 * r + 2 * gap * 0.6f)) / 2);
        var max = min + new Vector2(dockW, 2 * r + 2 * gap * 0.6f);
        var e = animate ? Motion.EaseOutBack(Motion.Stagger(sinceOpen, 0, 0, 0.5f)) : 1;
        var lift = (1 - e) * 20 * S;
        min.Y += lift;
        max.Y += lift;
        draw.AddRectFilled(min, max, ImGui.GetColorU32(ink with { W = 0.07f }), (max.Y - min.Y) / 2);
        draw.AddRect(min, max, ImGui.GetColorU32(ink with { W = 0.12f }), (max.Y - min.Y) / 2, ImDrawFlags.None, S);

        for (var k = 0; k < items.Length; k++)
        {
            var (glyph, tip, active) = items[k];
            var c = new Vector2(min.X + gap + r + k * (2 * r + gap), (min.Y + max.Y) / 2);
            ImGui.SetCursorScreenPos(c - new Vector2(r));
            var clicked = ImGui.InvisibleButton($"##dock{k}", new Vector2(2 * r));
            var hovered = ImGui.IsItemHovered();
            if (hovered) ImGui.SetTooltip(tip);
            var target = ImGui.IsItemActive() ? 0.9f : hovered ? 1.12f : 1f;
            if (animate) dockScale[k].Step(target, dt); else dockScale[k].Snap(target);
            var rr = r * dockScale[k].Value;
            draw.AddCircleFilled(c, rr, ImGui.GetColorU32(active ? primary with { W = 0.85f } : ink with { W = hovered ? 0.16f : 0.09f }), 32);
            Glyph(draw, glyph, c, rr * 0.85f, active ? ImGui.GetColorU32(colors[(int)ImGuiCol.WindowBg] with { W = 1 }) : ImGui.GetColorU32(ink));
            if (!clicked) continue;
            if (k == 0) SetPage(Page.Home);
            else if (k == 1) SetPage(Page.Apps);
            else plugin.ToggleSettings();
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, max.Y));
    }

    private void SetPage(Page next)
    {
        if (next == page) return;
        pageDirection = next > page ? 1 : -1;
        page = next;
        pageProgress = Animate ? 0 : 1;
        if (next == Page.Home) sinceOpen = MathF.Max(sinceOpen, 0.15f); // icons settle quickly instead of a full entrance
    }

    // ------------------------------------------------------------------ apps page

    private void DrawAppsPage()
    {
        var s = S;
        Kos.Section("Apps", $"{plugin.Apps.Count(a => a.Instance is not null)}/{plugin.Apps.Count}");
        for (var i = 0; i < plugin.Apps.Count; i++)
        {
            var app = plugin.Apps[i];
            Kos.Card(app.Info.Id, app.Status is null ? null : ImGuiColors.DalamudOrange, () =>
            {
                var on = !plugin.Config.DisabledApps.Contains(app.Info.Id);
                var size = 40 * s;
                var row = ImGui.GetCursorScreenPos();
                if (Icon(app) is { } tex)
                    ImGui.GetWindowDrawList().AddImage(tex.Handle, row, row + new Vector2(size), Vector2.Zero, Vector2.One,
                                                       ImGui.GetColorU32(new Vector4(1, 1, 1, app.Instance is null ? 0.38f : 1)));
                ImGui.Dummy(new Vector2(size));
                ImGui.SameLine();
                var switchW = ImGui.GetFrameHeight() * 1.625f;
                using (ImRaii.Group())
                {
                    ImGui.TextUnformatted(app.Info.Name);
                    ImGui.SameLine();
                    ImGui.TextDisabled(app.Info.Commands);
                    using (ImRaii.TextWrapPos(ImGui.GetContentRegionMax().X - switchW - 12 * s))
                        ImGui.TextDisabled(app.Info.Summary);
                }

                ImGui.SameLine(ImGui.GetContentRegionMax().X - switchW - 12 * s);
                if (MaterialKos.Switch("##on", ref on))
                {
                    plugin.SetEnabled(app, on);
                    Toast(app, on ? (app.Instance is null ? "couldn't start" : "switched on") : "switched off");
                }

                if (app.Status is { } status)
                {
                    Kos.Callout(status, ImGuiColors.DalamudOrange);
                    if (on && app.Instance is null && Kos.IconButton(FontAwesomeIcon.Redo, "Retry"))
                        plugin.Start(app);
                }

                if (app.Instance is { } instance)
                {
                    var open = instance.MainOpen;
                    if (Kos.IconButton(open ? FontAwesomeIcon.WindowClose : FontAwesomeIcon.WindowMaximize, open ? "Close" : "Open"))
                        instance.ToggleMain();
                    if (app.Info.HasSettings)
                    {
                        ImGui.SameLine();
                        if (Kos.IconButton(FontAwesomeIcon.Cog, "Settings")) instance.OpenSettings();
                    }
                }
            });
            ImGui.Spacing();
        }
    }

    // ------------------------------------------------------------------ helpers

    private static IDalamudTextureWrap? Icon(AppSlot app) =>
        KosHomePlugin.TextureProvider.GetFromManifestResource(typeof(MainWindow).Assembly, $"apps.{app.Info.Id}.png")
                     .TryGetWrap(out var wrap, out _) ? wrap : null;

    /// <summary>A FontAwesome glyph centred on a point at any size (the icon font scaled on the draw list).</summary>
    private static void Glyph(ImDrawListPtr draw, FontAwesomeIcon icon, Vector2 centre, float size, uint color)
    {
        var text = icon.ToIconString();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var g = ImGui.CalcTextSize(text) * (size / ImGui.GetFontSize());
            draw.AddText(UiBuilder.IconFont, size, centre - g / 2, color, text);
        }
    }

    /// <summary>The label, cut with "..." when it is wider than the tile (the tooltip has the full name).</summary>
    private static string Fit(string label, float width)
    {
        if (ImGui.CalcTextSize(label).X <= width) return label;
        for (var n = label.Length - 1; n > 1; n--)
            if (ImGui.CalcTextSize(label[..n] + "...").X <= width) return label[..n] + "...";
        return label[..1];
    }
}
