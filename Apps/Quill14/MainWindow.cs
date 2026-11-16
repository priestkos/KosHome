using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace KosHome.Apps.Quill14;

public sealed class MainWindow : Window
{
    private const int MaxDraftBytes = 8000;
    private static readonly string[] TemplateChannels = ["Draft's channel", "Say", "Party", "Tell (draft's target)", "Reply"];

    private readonly Plugin plugin;
    private readonly ImGui.ImGuiInputTextCallbackDelegate editorCallback; // kept in a field so it outlives the frame
    private bool selectSettings;

    // editor selection, as UTF-8 byte offsets, valid while selText is still the draft's text
    private int selStart, selEnd;
    private string? selText;

    // after a marker button: put the keyboard back in the editor with the caret here (UTF-8 bytes)
    private int? pendingCaret;
    private bool refocusEditor;

    private string oocLine = "";
    private string search = "";
    private Template? selected;
    private string? spellWord;
    private List<string> spellSuggestions = [];
    private (string Text, List<Misspelling> Found) spellCache = ("\u0000", []);

    // template fill-in flow
    private Template? flowTemplate;
    private bool flowSend, openFillPopup;
    private Dictionary<string, string> fills = [];

    public MainWindow(Plugin plugin) : base("Quill###Quill14")
    {
        this.plugin = plugin;
        editorCallback = OnEditorCallback;
        Size = new Vector2(620, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(460, 420), MaximumSize = new Vector2(1600, 1600) };
    }

    private Configuration Config => plugin.Config;

    public void OpenSettings()
    {
        IsOpen = true;
        selectSettings = true;
    }

    /// <summary>Insert or send a template; asks for {?fill-ins} first when it has any.</summary>
    public void BeginTemplate(Template t, bool send)
    {
        flowTemplate = t;
        flowSend = send;
        fills = Templates.FillIns(t.Body).ToDictionary(n => n, _ => "");
        if (fills.Count == 0) { CompleteTemplate(); return; }
        IsOpen = true;
        openFillPopup = true;
    }

    private void CompleteTemplate()
    {
        if (flowTemplate is not { } t) return;
        flowTemplate = null;
        var r = plugin.UseTemplate(t, fills);
        if (r.Missing.Count > 0)
        {
            plugin.LastError = $"\"{t.Name}\" needs {string.Join(", ", r.Missing)} (no target, no tell yet, or an empty fill-in).";
            if (!IsOpen) plugin.Notify(plugin.LastError);
            return;
        }

        var d = plugin.ActiveDraft;
        if (flowSend)
        {
            if (!plugin.Post(r.Text, t.Channel ?? d.Channel, d.TellTarget, false) && !IsOpen) plugin.Notify(plugin.LastError ?? "Not sent.");
            return;
        }

        d.Text = d.Text.Length == 0 ? r.Text : d.Text.TrimEnd() + " " + r.Text;
        IsOpen = true;
        Config.ActiveDraft = Config.Drafts.IndexOf(d);
        plugin.SaveConfig();
    }

    public override void Draw()
    {
        DrawFillPopup();
        using var bar = ImRaii.TabBar("##tabs");
        if (!bar) return;
        using (var t = ImRaii.TabItem("Write")) if (t) DrawWrite();
        using (var t = ImRaii.TabItem("Templates")) if (t) DrawTemplates();
        using (var t = ImRaii.TabItem("History")) if (t) DrawHistory();
        using (var t = ImRaii.TabItem("Settings", selectSettings ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None)) if (t) DrawSettings();
        selectSettings = false;
        using (var t = ImRaii.TabItem("Help")) if (t) DrawHelp();
    }

    // ---------------- Write ----------------

    private void DrawWrite()
    {
        using (var drafts = ImRaii.TabBar("##drafts"))
        {
            if (!drafts) return;
            for (var i = 0; i < Config.Drafts.Count; i++)
            {
                using var ti = ImRaii.TabItem($"{Config.Drafts[i].Name}###draft{i}");
                if (!ti) continue;
                if (Config.ActiveDraft != i) { Config.ActiveDraft = i; plugin.SaveConfig(); }
                DrawDraft(Config.Drafts[i]);
            }

            if (ImGui.TabItemButton("+", ImGuiTabItemFlags.Trailing))
            {
                Config.Drafts.Add(new Draft { Name = $"Scene {Config.Drafts.Count + 1}" });
                plugin.SaveConfig();
            }

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("New draft (one per scene or partner).");
        }
    }

    private void DrawDraft(Draft d)
    {
        var name = d.Name;
        ImGui.SetNextItemWidth(140);
        if (ImGui.InputText("##name", ref name, 32)) d.Name = name;
        if (ImGui.IsItemDeactivatedAfterEdit()) plugin.SaveConfig();
        ImGui.SameLine();
        foreach (var c in Enum.GetValues<Channel>())
        {
            if (ImGui.RadioButton(Channels.Label(c), d.Channel == c)) { d.Channel = c; plugin.SaveConfig(); }
            ImGui.SameLine();
        }

        ImGui.NewLine();
        DrawTarget(d);
        var prefix = plugin.Prefix(d.Channel, d.TellTarget);

        DrawMarkerButtons(d);
        DrawEditor(d, prefix);
        DrawSpelling(d);

        if (prefix is null) return;
        var split = plugin.Split(d.Text, prefix, d.WrapOoc);
        DrawPreview(split, prefix);
        DrawSend(d, split, prefix);
    }

    private void DrawTarget(Draft d)
    {
        if (d.Channel == Channel.Tell)
        {
            var target = d.TellTarget;
            ImGui.SetNextItemWidth(260);
            if (ImGui.InputTextWithHint("##target", "Forename Surname@World", ref target, 64)) d.TellTarget = target;
            if (ImGui.IsItemDeactivatedAfterEdit()) plugin.SaveConfig();
            ImGui.SameLine();
            if (ImGui.Button("From target") && plugin.TargetAsTell() is { } t) { d.TellTarget = t; plugin.SaveConfig(); }
            ImGui.SameLine();
            if (Channels.IsValidTarget(d.TellTarget)) ImGui.TextColored(ImGuiColors.HealerGreen, "ok");
            else ImGui.TextColored(ImGuiColors.DalamudRed, "needs Forename Surname@World");
        }
        else if (d.Channel == Channel.Reply)
        {
            if (plugin.LastTellSender.Length > 0)
                ImGui.TextUnformatted($"Reply → {plugin.LastTellSender} (locked for the whole post when you press Send)");
            else
                ImGui.TextColored(ImGuiColors.DalamudYellow, "No tell received since Quill loaded — nobody to reply to yet.");
        }
    }

    private void DrawMarkerButtons(Draft d)
    {
        var markers = Config.Markers.Where(m => m.IsUsable).ToList();
        for (var i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            if (ImGui.Button($"{m.Open}{m.Name}{m.Close}##mk{i}")) WrapSelection(d, m);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Wrap the selection, or the text you just typed, in {m.Open}…{m.Close}{(i < 9 ? $" (Ctrl+{i + 1} while typing)" : "")}.");
            ImGui.SameLine();
        }

        var wrap = d.WrapOoc;
        var ooc = MarkerPair.OocOf(Config.Markers);
        if (ImGui.Checkbox($"Whole post OOC {ooc.Open}…{ooc.Close}", ref wrap)) { d.WrapOoc = wrap; plugin.SaveConfig(); }
    }

    private void WrapSelection(Draft d, MarkerPair m)
    {
        int a, b;
        if (selText == d.Text)
            (a, b) = (Markup.ByteToChar(d.Text, selStart), Markup.ByteToChar(d.Text, selEnd));
        else
            a = b = d.Text.Length;
        var (text, caret) = Markup.WrapSmart(d.Text, a, b, m, Config.Markers);
        d.Text = text;
        pendingCaret = Markup.CharToByte(text, caret);
        refocusEditor = true;
        selText = null;
        plugin.SaveConfig();
    }

    private void DrawEditor(Draft d, string? prefix)
    {
        var text = d.Text;
        if (refocusEditor)
        {
            ImGui.SetKeyboardFocusHere(); // the button click took the keyboard away; give it back
            refocusEditor = false;
        }

        var ctrlEnter = ImGui.InputTextMultiline("##text", ref text, MaxDraftBytes, new Vector2(-1, 150),
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackAlways, editorCallback);
        if (text != d.Text) d.Text = text;
        if (ImGui.IsItemActive()) selText = d.Text;
        if (ImGui.IsItemDeactivatedAfterEdit()) plugin.SaveConfig();
        if (ctrlEnter && prefix is not null) Send(d);
    }

    // Runs every frame the editor is focused: places the caret after a button wrap, remembers the selection for the
    // marker buttons and handles Ctrl+1..9.
    private int OnEditorCallback(ref ImGuiInputTextCallbackData data)
    {
        if (pendingCaret is { } caret)
        {
            data.CursorPos = data.SelectionStart = data.SelectionEnd = Math.Clamp(caret, 0, data.BufTextLen);
            pendingCaret = null;
        }

        selStart = Math.Min(data.SelectionStart, data.SelectionEnd);
        selEnd = Math.Max(data.SelectionStart, data.SelectionEnd);
        if (!ImGui.GetIO().KeyCtrl) return 0;

        var markers = Config.Markers.Where(m => m.IsUsable).Take(9).ToList();
        for (var i = 0; i < markers.Count; i++)
        {
            if (!ImGui.IsKeyPressed(ImGuiKey.Key1 + i, false)) continue;
            var current = System.Text.Encoding.UTF8.GetString(data.BufTextSpan);
            var (text, caretChar) = Markup.WrapSmart(current, Markup.ByteToChar(current, selStart), Markup.ByteToChar(current, selEnd), markers[i], Config.Markers);
            if (Splitter.Bytes(text) >= data.BufSize) break; // InsertChars would silently drop it after the delete
            data.DeleteChars(0, data.BufTextLen);
            data.InsertChars(0, text);
            data.CursorPos = data.SelectionStart = data.SelectionEnd = Markup.CharToByte(text, caretChar);
            break;
        }

        return 0;
    }

    private void DrawSpelling(Draft d)
    {
        if (!Config.SpellCheck) return;
        if (plugin.Spell is not { } spell)
        {
            ImGui.TextDisabled(plugin.SpellLoadFailed ? "Spell check unavailable (word list failed to load)." : "Spell check loading…");
            return;
        }

        if (!ReferenceEquals(spellCache.Text, d.Text)) spellCache = (d.Text, spell.Check(d.Text, Config.UserWords));
        var words = spellCache.Found.Select(f => f.Word).Distinct().Take(12).ToList();
        if (words.Count == 0) return;

        ImGui.TextColored(ImGuiColors.DalamudYellow, "Spelling:");
        foreach (var w in words)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"{w}##sp")) { spellWord = w; spellSuggestions = spell.Suggest(w); ImGui.OpenPopup("##spell"); }
        }

        using var popup = ImRaii.Popup("##spell");
        if (!popup || spellWord is null) return;
        if (spellSuggestions.Count == 0) ImGui.TextDisabled("No suggestions");
        foreach (var s in spellSuggestions)
        {
            if (!ImGui.Selectable(s)) continue;
            d.Text = SpellChecker.ReplaceWord(d.Text, spellWord, s);
            plugin.SaveConfig();
        }

        ImGui.Separator();
        if (ImGui.Selectable($"Add \"{spellWord}\" to dictionary"))
        {
            Config.UserWords.Add(SpellChecker.Key(spellWord));
            spellCache = ("\u0000", []);
            plugin.SaveConfig();
        }
    }

    private static void DrawPreview(SplitResult split, string prefix)
    {
        foreach (var p in split.Problems) ImGui.TextColored(ImGuiColors.DalamudRed, p);
        foreach (var w in split.Warnings) ImGui.TextColored(ImGuiColors.DalamudYellow, w);
        if (split.Chunks.Count == 0) return;

        using var child = ImRaii.Child("##preview", new Vector2(-1, 150), true);
        if (!child) return;
        for (var i = 0; i < split.Chunks.Count; i++)
        {
            var line = $"{prefix} {split.Chunks[i]}";
            ImGui.TextDisabled($"{i + 1}/{split.Chunks.Count} · {Splitter.Bytes(line)}/{Channels.MaxLineBytes} B");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Copy##c{i}")) ImGui.SetClipboardText(line);
            ImGui.TextWrapped(split.Chunks[i]);
        }
    }

    private void DrawSend(Draft d, SplitResult split, string prefix)
    {
        var box = plugin.Outbox;
        if (plugin.Busy)
        {
            ImGui.TextUnformatted(box.Active ? $"Sending {box.Next}/{box.Count} → {prefix}" : "Checking…");
            if (box.WaitingForPress)
            {
                ImGui.SameLine();
                if (ImGui.Button($"Send next ({box.Next + 1}/{box.Count})")) plugin.ReleaseNext();
                ImGui.SameLine();
                if (ImGui.Button("Send rest")) plugin.ReleaseAll();
            }

            ImGui.SameLine();
            if (ImGui.Button("Stop")) plugin.Stop();
        }
        else
        {
            var blocked = split.Chunks.Count == 0 || split.Problems.Count > 0;
            using (ImRaii.Disabled(blocked))
            {
                if (ImGui.Button($"Send {split.Chunks.Count} → {prefix}")) Send(d);
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Ctrl+Enter in the text box. Chunks go out in order, spaced; Stop drops the rest.");
        }

        ImGui.SameLine();
        var step = Config.StepMode;
        if (ImGui.Checkbox("Step mode", ref step)) { Config.StepMode = step; plugin.SaveConfig(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Send one chunk per press (Send next, or /quill next from a macro).");
        ImGui.SameLine();
        if (ImGui.Button("Save as template"))
        {
            selected = new Template { Name = TemplateName(d.Text), Body = d.Text };
            Config.Templates.Add(selected);
            plugin.SaveConfig();
        }

        if (plugin.LastError is { } err) ImGui.TextColored(ImGuiColors.DalamudRed, err);

        ImGui.SetNextItemWidth(-90);
        var send = ImGui.InputTextWithHint("##ooc", "Quick OOC line (Enter sends)", ref oocLine, 400, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        send |= ImGui.Button("Send OOC");
        if (send && oocLine.Trim().Length > 0 && plugin.Post(oocLine, d.Channel, d.TellTarget, true)) oocLine = "";

        if (ImGui.SmallButton("Clear draft") && ImGui.GetIO().KeyCtrl) { d.Text = ""; plugin.SaveConfig(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Ctrl+click.");
        if (Config.Drafts.Count > 1)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Close draft") && ImGui.GetIO().KeyCtrl)
            {
                Config.Drafts.Remove(d);
                Config.ActiveDraft = Math.Clamp(Config.ActiveDraft, 0, Config.Drafts.Count - 1);
                plugin.SaveConfig();
            }

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Ctrl+click: delete this draft.");
        }
    }

    private void Send(Draft d)
    {
        plugin.Post(d.Text, d.Channel, d.TellTarget, d.WrapOoc);
    }

    private static string TemplateName(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3);
        var name = string.Join(' ', words).Trim('*', '"', '(', ')');
        return name.Length == 0 ? "New template" : name.Length > 30 ? name[..30] : name;
    }

    // ---------------- Templates ----------------

    private void DrawTemplates()
    {
        ImGui.SetNextItemWidth(-120);
        ImGui.InputTextWithHint("##search", "Search name, tags or text", ref search, 64);
        ImGui.SameLine();
        if (ImGui.Button("New template"))
        {
            selected = new Template { Name = "New template" };
            Config.Templates.Add(selected);
            plugin.SaveConfig();
        }

        var list = Templates.Rank(plugin.VisibleTemplates, search, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        using (var child = ImRaii.Child("##tlist", new Vector2(-1, 170), true))
        {
            if (child)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (ImGui.Selectable($"{(t.Pinned ? "★ " : "")}{t.Name}##t{i}", selected == t)) selected = t;
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) BeginTemplate(t, send: false);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(t.Body.Length > 200 ? t.Body[..200] + "…" : t.Body);
                    if (t.Tags.Length == 0) continue;
                    ImGui.SameLine();
                    ImGui.TextDisabled(t.Tags);
                }

                if (list.Count == 0) ImGui.TextDisabled("No templates match.");
            }
        }

        if (selected is null || !Config.Templates.Contains(selected))
        {
            ImGui.TextDisabled("Select a template to edit it. Double-click inserts it into the draft.");
            return;
        }

        DrawTemplateEditor(selected);
    }

    private void DrawTemplateEditor(Template t)
    {
        var name = t.Name;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("Name", ref name, 48)) t.Name = name;
        if (ImGui.IsItemDeactivatedAfterEdit()) plugin.SaveConfig();
        ImGui.SameLine();
        var tags = t.Tags;
        ImGui.SetNextItemWidth(-60);
        if (ImGui.InputText("Tags", ref tags, 96)) t.Tags = tags;
        if (ImGui.IsItemDeactivatedAfterEdit()) plugin.SaveConfig();

        var body = t.Body;
        if (ImGui.InputTextMultiline("##tbody", ref body, MaxDraftBytes, new Vector2(-1, 110))) t.Body = body;
        if (ImGui.IsItemDeactivatedAfterEdit()) plugin.SaveConfig();
        ImGui.TextDisabled("{me} {mefirst} {target} {targetfirst} {tell} {tellfirst} · {?fill-in} asks · {a|b|c} picks one");

        var ch = t.Channel is { } c ? (int)c + 1 : 0;
        ImGui.SetNextItemWidth(180);
        if (ImGui.Combo("Channel", ref ch, TemplateChannels)) { t.Channel = ch == 0 ? null : (Channel)(ch - 1); plugin.SaveConfig(); }
        ImGui.SameLine();
        var pinned = t.Pinned;
        if (ImGui.Checkbox("Pinned", ref pinned)) { t.Pinned = pinned; plugin.SaveConfig(); }
        ImGui.SameLine();
        var mine = t.CharacterId != 0;
        using (ImRaii.Disabled(plugin.MyContentId == 0 && !mine))
        {
            if (ImGui.Checkbox("This character only", ref mine)) { t.CharacterId = mine ? plugin.MyContentId : 0; plugin.SaveConfig(); }
        }

        if (ImGui.Button("Insert")) BeginTemplate(t, send: false);
        ImGui.SameLine();
        using (ImRaii.Disabled(plugin.Busy)) { if (ImGui.Button("Send")) BeginTemplate(t, send: true); }
        ImGui.SameLine();
        if (ImGui.Button("Duplicate"))
        {
            var copy = t.Copy();
            copy.Name += " (copy)";
            copy.Uses = 0;
            Config.Templates.Add(copy);
            selected = copy;
            plugin.SaveConfig();
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete") && ImGui.GetIO().KeyCtrl)
        {
            Config.Templates.Remove(t);
            selected = null;
            plugin.SaveConfig();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Ctrl+click.");
        ImGui.TextDisabled($"Used {t.Uses} time(s). Macro: /quill {t.Name}  ·  /quill send {t.Name}");
        if (plugin.LastError is { } err) ImGui.TextColored(ImGuiColors.DalamudRed, err);
    }

    private void DrawFillPopup()
    {
        if (openFillPopup) { ImGui.OpenPopup("Fill in###quillfill"); openFillPopup = false; }
        using var modal = ImRaii.PopupModal("Fill in###quillfill", ImGuiWindowFlags.AlwaysAutoResize);
        if (!modal) return;
        if (flowTemplate is null) { ImGui.CloseCurrentPopup(); return; }

        ImGui.TextUnformatted(flowTemplate.Name);
        foreach (var key in fills.Keys.ToList())
        {
            var v = fills[key];
            ImGui.SetNextItemWidth(260);
            if (ImGui.InputText(key, ref v, 200)) fills[key] = v;
        }

        if (ImGui.Button(flowSend ? "Send" : "Insert")) { CompleteTemplate(); ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) { flowTemplate = null; ImGui.CloseCurrentPopup(); }
    }

    // ---------------- History ----------------

    private void DrawHistory()
    {
        if (Config.History.Count == 0) { ImGui.TextDisabled("Nothing sent yet."); return; }
        if (ImGui.SmallButton("Clear history") && ImGui.GetIO().KeyCtrl) { Config.History.Clear(); plugin.SaveConfig(); return; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Ctrl+click.");

        for (var i = 0; i < Config.History.Count; i++)
        {
            var h = Config.History[i];
            ImGui.Separator();
            var when = DateTimeOffset.FromUnixTimeSeconds(h.SentUnix).ToLocalTime().ToString("MMM d HH:mm");
            ImGui.TextDisabled($"{when} · {Channels.Label(h.Channel)}{(h.Target.Length > 0 ? " → " + h.Target : "")}{(h.WrapOoc ? " · OOC" : "")}");
            ImGui.TextWrapped(h.Text.Length > 300 ? h.Text[..300] + "…" : h.Text);
            if (ImGui.SmallButton($"Open in draft##h{i}"))
            {
                var d = plugin.ActiveDraft;
                d.Text = h.Text;
                d.Channel = h.Channel;
                d.WrapOoc = h.WrapOoc;
                if (h.Channel == Channel.Tell) d.TellTarget = h.Target;
                plugin.SaveConfig();
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(plugin.Busy))
            {
                // A reply is resent to the person it went to, not to whoever told you last.
                if (ImGui.SmallButton($"Resend##r{i}"))
                    plugin.Post(h.Text, h.Channel == Channel.Reply ? Channel.Tell : h.Channel, h.Target, h.WrapOoc);
            }
        }
    }

    // ---------------- Settings ----------------

    private void DrawSettings()
    {
        ImGui.TextUnformatted("Continuation marker (added to every chunk but the last)");
        var cont = Config.Continuation;
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputText("##cont", ref cont, 24)) Config.Continuation = cont;
        if (ImGui.IsItemDeactivatedAfterEdit()) plugin.SaveConfig();
        foreach (var (label, value) in new[] { ("(c)", " (c)"), (">>>", " >>>"), ("(1/3)", " ({n}/{total})"), ("None", "") })
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(label)) { Config.Continuation = value; plugin.SaveConfig(); }
        }

        ImGui.TextDisabled("{n} = chunk number, {total} = chunk count.");

        ImGui.Spacing();
        ImGui.TextUnformatted("OOC brackets");
        var ooc = Config.Markers.FirstOrDefault(m => m.Ooc);
        foreach (var (open, close) in new[] { ("(", ")"), ("((", "))") })
        {
            ImGui.SameLine();
            if (!ImGui.SmallButton($"{open}text{close}")) continue;
            if (ooc is null) { ooc = new MarkerPair("OOC", open, close, true); Config.Markers.Add(ooc); }
            ooc.Open = open;
            ooc.Close = close;
            plugin.SaveConfig();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Markers (buttons, Ctrl+1…9, and kept balanced across splits)");
        using (var table = ImRaii.Table("##markers", 5, ImGuiTableFlags.SizingFixedFit))
        {
            if (table)
            {
                for (var i = 0; i < Config.Markers.Count; i++)
                {
                    var m = Config.Markers[i];
                    using var id = ImRaii.PushId(i);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var n = m.Name;
                    ImGui.SetNextItemWidth(110);
                    if (ImGui.InputText("##n", ref n, 24)) m.Name = n;
                    if (ImGui.IsItemDeactivatedAfterEdit()) plugin.SaveConfig();
                    ImGui.TableNextColumn();
                    var o = m.Open;
                    ImGui.SetNextItemWidth(50);
                    if (ImGui.InputText("##o", ref o, 8)) m.Open = o;
                    if (ImGui.IsItemDeactivatedAfterEdit()) plugin.SaveConfig();
                    ImGui.TableNextColumn();
                    var c = m.Close;
                    ImGui.SetNextItemWidth(50);
                    if (ImGui.InputText("##c", ref c, 8)) m.Close = c;
                    if (ImGui.IsItemDeactivatedAfterEdit()) plugin.SaveConfig();
                    ImGui.TableNextColumn();
                    if (ImGui.RadioButton("OOC", m.Ooc))
                    {
                        foreach (var other in Config.Markers) other.Ooc = other == m;
                        plugin.SaveConfig();
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton("Remove") && ImGui.GetIO().KeyCtrl)
                    {
                        Config.Markers.RemoveAt(i);
                        plugin.SaveConfig();
                        break;
                    }
                }
            }
        }

        if (ImGui.SmallButton("Add marker")) { Config.Markers.Add(new MarkerPair("Thought", "~", "~")); plugin.SaveConfig(); }
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset markers") && ImGui.GetIO().KeyCtrl) { Config.Markers = MarkerPair.Defaults(); plugin.SaveConfig(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Ctrl+click: back to Action, Dialogue, OOC.");

        ImGui.Spacing();
        var secs = Config.SpacingMs / 1000f;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Seconds between chunks", ref secs, Outbox.MinSpacingMs / 1000f, 10f, "%.1f s"))
            Config.SpacingMs = Math.Max(Outbox.MinSpacingMs, (int)(secs * 1000));
        if (ImGui.IsItemDeactivatedAfterEdit()) plugin.SaveConfig();
        ImGui.TextDisabled("Plus up to 1 s of random jitter. Never below 2 s.");

        ImGui.Spacing();
        if (plugin.MyContentId != 0)
        {
            Config.RpNames.TryGetValue(plugin.MyContentId, out var rp);
            rp ??= "";
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputTextWithHint("RP name for this character", plugin.MyName, ref rp, 40)) Config.RpNames[plugin.MyContentId] = rp;
            if (ImGui.IsItemDeactivatedAfterEdit()) plugin.SaveConfig();
            ImGui.TextDisabled("Used for {me} / {mefirst}. Empty = your character name.");
        }

        ImGui.Spacing();
        var sc = Config.SpellCheck;
        if (ImGui.Checkbox("Spell check (English)", ref sc)) { Config.SpellCheck = sc; plugin.SaveConfig(); }
        if (Config.UserWords.Count > 0)
        {
            ImGui.TextDisabled($"Your dictionary ({Config.UserWords.Count}) — click a word to remove it:");
            foreach (var w in Config.UserWords.Order().ToList())
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"{w}##uw")) { Config.UserWords.Remove(w); spellCache = ("\u0000", []); plugin.SaveConfig(); }
            }
        }
    }

    // ---------------- Help ----------------

    private static void DrawHelp()
    {
        Section("Splitting", "Long posts are cut into lines of at most 470 bytes, at a sentence end when possible, else at a space. A blank line starts a new chunk.",
            "The game refuses chat lines over 500 bytes; the margin and the prefix (/tell Name@World) are counted so every chunk fits.");
        Section("Markers", "*action*, \"dialogue\", (OOC) and your own pairs. A pair cut by a split is closed at the end of the chunk and reopened on the next.",
            "Each chunk then reads correctly on its own in other players' logs.");
        Section("Channels", "Say, Party, Tell and Reply only. Reply locks the person who last sent you a tell when you press Send and sends every chunk as /tell to them.",
            "A plain /r per chunk would send the rest of the post to whoever tells you mid-post.");
        Section("Sending", "Nothing is sent until you press Send (or Ctrl+Enter). Chunks go out in order, at least 2 s apart, with Stop. Every chunk is checked first; if the game would change any character, nothing is sent.",
            "A half-sent or altered scene post is worse than none; spacing keeps you clear of chat spam.");
        Section("Step mode", "One chunk per press: Send next in the window, or /quill next from a hotbar macro.",
            "Lets you pause mid-post when someone interrupts the scene.");
        Section("Templates", "{me} {target} {tell} (and …first), {?word} asks you, {a|b|c} picks one and avoids last time's pick. Search ranks pinned, then name matches, then recently and often used.",
            "Reused lines stop reading canned, and the ones you use most float up.");
        Section("Spell check", "English word list (dwyl/english-words, public domain), one-edit suggestions, your own dictionary. Capitalised words mid-sentence are taken as names and skipped; Japanese is not checked.",
            "Basic by design: it catches typos, not grammar.");
        ImGui.TextDisabled("/quill · /quill <template> · /quill send <template> · /quill ooc <text> · /quill next · /quill stop · /quill probe");
    }

    private static void Section(string title, string what, string why)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(title);
        ImGui.Separator();
        ImGui.TextWrapped(what);
        ImGui.TextWrapped($"Why: {why}");
    }
}
