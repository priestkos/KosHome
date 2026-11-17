using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace KosHome.Apps.WorldClock14;

/// <summary>
/// The main window. Nothing in this class touches the game: no hooks, no native pointers, no game memory.
/// The worst it can do is draw badly, which is the point of keeping the conversion in TimeConversion.cs.
/// </summary>
public sealed class WorldClockWindow : Window, IDisposable
{
    private const float FlashSeconds = 2.5f;

    private readonly Configuration config;
    private readonly Action<string> requestCopy;
    private readonly Action openSettings;

    private string input = string.Empty;
    private bool liveNow = true;
    private string note = string.Empty;
    private string echo = string.Empty;
    private string flash = string.Empty;
    private double flashUntil;

    public WorldClockWindow(Configuration config, Action<string> requestCopy, Action openSettings)
        : base("WorldClock14###WorldClock14Main")
    {
        this.config = config;
        this.requestCopy = requestCopy;
        this.openSettings = openSettings;

        Size = new Vector2(420, 380);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 240),
            MaximumSize = new Vector2(900, 1000),
        };
    }

    /// <summary>Used by /tz when the argument could not be read, so the user sees why instead of a silent no-op.</summary>
    public void ShowError(string message, string source)
    {
        input = source;
        liveNow = false;
        note = message;
        echo = string.Empty;
        IsOpen = true;
    }

    public override void Draw()
    {
        var baseZone = ZoneResolver.ResolveBaseOrLocal(config.BaseZone);

        DrawModeRow();
        if (!liveNow) DrawInputRow();
        DrawBaseZoneRow();

        ImGui.Spacing();
        var rows = BuildRows(baseZone);
        DrawResults(rows);

        ImGui.Spacing();
        DrawFooter(rows);
    }

    private void DrawModeRow()
    {
        if (ImGui.RadioButton("Live now##modeNow", liveNow)) liveNow = true;
        ImGui.SameLine();
        if (ImGui.RadioButton("Custom time##modeCustom", !liveNow)) liveNow = false;

        var label = "Settings";
        var width = ImGui.CalcTextSize(label).X + 18 * ImGuiHelpers.GlobalScale;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - width);
        if (ImGui.Button($"{label}##openSettings", new Vector2(width, 0))) openSettings();
    }

    private void DrawInputRow()
    {
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##tzinput", "20:00  ·  8pm  ·  Wed 21:00  ·  2026-10-07 20:00", ref input, 64);
    }

    private void DrawBaseZoneRow()
    {
        ImGui.TextUnformatted("Times are read in");
        UiHelpers.BaseZoneCombo(config, "##tzbase");
    }

    private List<ConversionRow> BuildRows(TimeZoneInfo baseZone)
    {
        var now = DateTimeOffset.UtcNow;

        if (liveNow)
        {
            note = string.Empty;
            echo = TimeFmt.Full(TimeZoneInfo.ConvertTime(now, baseZone), config.Format.Use24Hour);
            return Converter.Convert(now, baseZone, config.Zones);
        }

        if (input.Trim().Length == 0)
        {
            note = "Type a time - 20:00, 8pm, Wed 21:00 or 2026-10-07 20:00.";
            echo = string.Empty;
            return new List<ConversionRow>();
        }

        if (!Converter.TryBuildRows(input, now, config.BaseZone, config.Zones,
                out var rows, out var parsed, out var ambiguous, out var error))
        {
            note = error;
            echo = string.Empty;
            return new List<ConversionRow>();
        }

        note = ambiguous ? "Careful: the clocks change around that time in the base zone." : string.Empty;
        echo = TimeFmt.Echo(parsed.WallClock, config.Format.Use24Hour);
        return rows;
    }

    private void DrawResults(List<ConversionRow> rows)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX;
        if (!ImGui.BeginTable("##tzrows", 3, flags)) return;

        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthFixed, 96 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##copy", ImGuiTableColumnFlags.WidthFixed, 62 * ImGuiHelpers.GlobalScale);

        for (var i = 0; i < rows.Count; i++) DrawResultRow(rows[i], i);

        ImGui.EndTable();
    }

    private void DrawResultRow(ConversionRow row, int index)
    {
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(row.Label);

        ImGui.TableSetColumnIndex(1);
        if (!row.Ok)
        {
            ImGui.TextWrapped(row.Error);
        }
        else
        {
            ImGui.TextUnformatted(row.Time(config.Format));

            if (row.DeltaText.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(row.DeltaText);
            }
        }

        ImGui.TableSetColumnIndex(2);
        if (!row.Ok) return;

        if (ImGui.Button($"Copy##tzcopy{index}", new Vector2(-1, 0)))
        {
            var text = ChatFormatter.RowText(row, config.Format);
            RequestCopy(text, text);
        }
    }

    private void DrawFooter(List<ConversionRow> rows)
    {
        var copyable = rows.Exists(r => r.Ok);
        var text = copyable ? ChatFormatter.Build(rows, config.Format) : string.Empty;

        ImGui.BeginDisabled(!copyable);
        if (ImGui.Button("Copy for chat", new Vector2(-1, 0)) && copyable) RequestCopy(text, text);
        ImGui.EndDisabled();

        if (flash.Length > 0 && ImGui.GetTime() < flashUntil)
        {
            ImGui.TextWrapped(flash);
        }
        else if (note.Length > 0)
        {
            ImGui.TextWrapped(note);
        }

        if (echo.Length > 0) ImGui.TextDisabled(echo);
    }

    private void RequestCopy(string text, string flashText)
    {
        if (text.Length == 0) return;

        requestCopy(text);

        var flat = flashText.Replace('\n', ' ');
        flash = "Copied: " + (flat.Length > 68 ? flat[..65] + "..." : flat);
        flashUntil = ImGui.GetTime() + FlashSeconds;
    }

    /// <summary>
    /// KosHome's home-screen widget: every zone live, a row click copies that zone's line, and the header has copy all,
    /// the full converter (custom times) and settings. Same rows, formatting and copy path as the window. Drawn on the
    /// draw list like the clock card above it, inside <see cref="WidgetFrame.Width"/>: the window's content region is
    /// wider than the card, so nothing here measures against it.
    /// </summary>
    public void DrawWidget(WidgetFrame frame, Action openConverter)
    {
        var rows = Converter.Convert(DateTimeOffset.UtcNow, ZoneResolver.ResolveBaseOrLocal(config.BaseZone), config.Zones);
        var s = ImGuiHelpers.GlobalScale;
        var draw = ImGui.GetWindowDrawList();
        var colors = ImGui.GetStyle().Colors;
        var ink = colors[(int)ImGuiCol.Text];
        var accent = colors[(int)ImGuiCol.CheckMark];
        uint Ink(float a) => ImGui.GetColorU32(ink with { W = ink.W * a }); // GetColorU32 applies the card's fade
        var width = frame.Width;
        var origin = ImGui.GetCursorScreenPos();
        var line = ImGui.GetTextLineHeight();
        var now = ImGui.GetTime();

        // Header: the title (or what was just copied) on the left, three round icon buttons on the right.
        var button = ImGui.GetFrameHeight();
        var gap = 4 * s;
        var buttonsX = origin.X + width - 3 * button - 2 * gap;
        var flashing = flash.Length > 0 && now < flashUntil;
        ImGui.PushClipRect(origin, new Vector2(buttonsX - gap, origin.Y + button), true);
        draw.AddText(new Vector2(origin.X, origin.Y + (button - line) / 2), flashing ? ImGui.GetColorU32(accent) : Ink(0.6f),
                     flashing ? flash : "WORLD CLOCK");
        ImGui.PopClipRect();

        var copyable = rows.Exists(r => r.Ok);
        ImGui.SetCursorScreenPos(new Vector2(buttonsX, origin.Y));
        if (GhostButton("##wcAll", FontAwesomeIcon.Copy, "Copy every zone for chat", copyable))
            RequestCopy(ChatFormatter.Build(rows, config.Format), "every zone");
        ImGui.SameLine(0, gap);
        if (GhostButton("##wcConvert", FontAwesomeIcon.ExchangeAlt, "Convert another time (/tz)")) openConverter();
        ImGui.SameLine(0, gap);
        if (GhostButton("##wcSettings", FontAwesomeIcon.Cog, "Zones and format (/tzconfig)")) openSettings();

        var y = origin.Y + button + 4 * s;
        if (rows.Count == 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(origin.X, y));
            ImGui.TextDisabled("No zones switched on: add some in settings."); // wraps at the card edge (host's wrap pos)
            y = ImGui.GetCursorScreenPos().Y;
        }

        // One row per zone: label (and a day marker when it is another day) on the left, the time large on the right.
        var rowH = MathF.Round(MathF.Max(line * 1.8f, 28 * s));
        var big = line * 1.4f;
        var inset = 6 * s;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var min = new Vector2(origin.X, y);
            var max = min + new Vector2(width, rowH);
            var mid = min.Y + rowH / 2;
            ImGui.SetCursorScreenPos(min);
            var clicked = ImGui.InvisibleButton($"##wcRow{i}", new Vector2(width, rowH));
            var hovered = ImGui.IsItemHovered();
            var held = ImGui.IsItemActive();
            if (hovered) ImGui.SetTooltip(row.Ok ? $"{row.DayAbbr} {row.DateText}  -  click to copy for chat" : row.Error);

            if (i > 0) draw.AddLine(new Vector2(min.X + inset, min.Y), new Vector2(max.X - inset, min.Y), Ink(0.07f), s);
            var hl = new Vector2(inset, 0);
            if (row.Ok && (hovered || held)) draw.AddRectFilled(min - hl, max + hl, Ink(held ? 0.10f : 0.05f), 10 * s);
            var sinceCopy = (float)(now - copiedAt);
            if (i == copiedRow && sinceCopy is >= 0 and < 0.6f)
                draw.AddRectFilled(min - hl, max + hl, ImGui.GetColorU32(accent with { W = accent.W * 0.22f * (1 - sinceCopy / 0.6f) }), 10 * s);

            var labelW = ImGui.CalcTextSize(row.Label).X;
            draw.AddText(new Vector2(min.X, mid - line / 2), Ink(row.Ok ? 0.9f : 0.5f), row.Label);

            if (!row.Ok)
            {
                // The reason, right-aligned and cut at the label so a long zone id cannot run over it.
                var errW = ImGui.CalcTextSize(row.Error).X;
                ImGui.PushClipRect(new Vector2(min.X + labelW + 12 * s, min.Y), max, true);
                draw.AddText(new Vector2(MathF.Max(min.X + labelW + 12 * s, max.X - errW), mid - line / 2), Ink(0.45f), row.Error);
                ImGui.PopClipRect();
            }
            else
            {
                if (row.DeltaText.Length > 0)
                    draw.AddText(new Vector2(min.X + labelW + 8 * s, mid - line / 2), ImGui.GetColorU32(accent), row.DeltaText);

                // 12-hour times draw "3:15" large and "PM" at text size beside it.
                var time = row.Time(config.Format);
                var space = time.IndexOf(' ');
                var main = space > 0 ? time[..space] : time;
                var suffix = space > 0 ? time[(space + 1)..] : string.Empty;
                var right = max.X;
                if (suffix.Length > 0)
                {
                    var sw = ImGui.CalcTextSize(suffix).X;
                    draw.AddText(new Vector2(right - sw, mid - line / 2), Ink(0.6f), suffix);
                    right -= sw + 4 * s;
                }

                DrawLarge(draw, frame.DisplayFont, main, big, new Vector2(right, mid), Ink(1f));
            }

            if (clicked && row.Ok)
            {
                RequestCopy(ChatFormatter.RowText(row, config.Format), row.Label);
                copiedRow = i;
                copiedAt = now;
            }

            y += rowH;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));
        ImGui.Dummy(new Vector2(width, 0));
    }

    private int copiedRow = -1;
    private double copiedAt = double.NegativeInfinity;

    /// <summary>Text at <paramref name="size"/> px, right edge and vertical centre on <paramref name="rightMid"/>, in the
    /// display font when it is built (the default font scaled up until then).</summary>
    private static void DrawLarge(ImDrawListPtr draw, IFontHandle? font, string text, float size, Vector2 rightMid, uint color)
    {
        using (font is { Available: true } ? font.Push() : null)
        {
            var f = ImGui.GetFont();
            var t = ImGui.CalcTextSize(text) * (size / ImGui.GetFontSize());
            draw.AddText(f, size, new Vector2(rightMid.X - t.X, rightMid.Y - t.Y / 2), color, text);
        }
    }

    /// <summary>A round, borderless icon button in the tablet's style: the glyph alone, a soft disc on hover.</summary>
    private static bool GhostButton(string id, FontAwesomeIcon icon, string tooltip, bool enabled = true)
    {
        var size = new Vector2(ImGui.GetFrameHeight());
        var min = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size) && enabled;
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        var draw = ImGui.GetWindowDrawList();
        var ink = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var centre = min + size / 2;
        if (enabled && (hovered || held))
            draw.AddCircleFilled(centre, size.X / 2, ImGui.GetColorU32(ink with { W = ink.W * (held ? 0.16f : 0.09f) }), 24);

        var text = icon.ToIconString();
        var glyph = size.X * 0.46f;
        var color = ImGui.GetColorU32(ink with { W = ink.W * (!enabled ? 0.25f : hovered ? 1f : 0.65f) });
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var g = ImGui.CalcTextSize(text) * (glyph / ImGui.GetFontSize());
            draw.AddText(UiBuilder.IconFont, glyph, centre - g / 2, color, text);
        }

        if (hovered) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    public void Dispose() { }
}
