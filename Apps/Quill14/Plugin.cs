using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace KosHome.Apps.Quill14;

public sealed class Plugin : IKosApp
{
    private const string CommandName = "/quill";
    private const int JitterMs = 1000;

    // What the game's own sanitiser may keep; a chunk it would change is never sent altered (copied from StripDeathroll14).
    private const AllowedEntities TextEntities = AllowedEntities.UppercaseLetters | AllowedEntities.LowercaseLetters |
        AllowedEntities.Numbers | AllowedEntities.SpecialCharacters | AllowedEntities.CharacterList |
        AllowedEntities.OtherCharacters | AllowedEntities.Payloads | AllowedEntities.Unknown9 | AllowedEntities.CJK;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IFramework framework;
    private readonly ITargetManager targetManager;
    private readonly IPluginLog log;
    private readonly WindowSystem windowSystem = new("Quill14");
    private readonly MainWindow window;
    private readonly Outbox outbox = new();
    private readonly Dictionary<string, string> lastPicks = [];
    private (string Prefix, List<string> Bodies, HistoryEntry Entry)? pending;
    private bool probeRequested;
    private string lastTellRaw = "";
    private volatile SpellChecker? spell;
    private (string Text, string Prefix, string Cont, bool Wrap, string Markers, SplitResult Result) splitCache = ("", "", "", false, "", new SplitResult([], [], []));

    public Configuration Config { get; }
    public string? LastError { get; set; }

    /// <summary>Who Reply goes to: the sender of the last tell seen since the plugin loaded (Forename Surname@World).</summary>
    public string LastTellSender { get; private set; } = "";

    public SpellChecker? Spell => spell;
    public bool SpellLoadFailed { get; private set; }
    public Outbox Outbox => outbox;
    public bool Busy => outbox.Active || pending is not null;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IClientState clientState,
        IPlayerState playerState,
        IFramework framework,
        ITargetManager targetManager,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chatGui = chatGui;
        this.clientState = clientState;
        this.playerState = playerState;
        this.framework = framework;
        this.targetManager = targetManager;
        this.log = log;

        Config = AppConfig.Load<Configuration>(pluginInterface, "Quill14");
        Config.Normalize();

        window = new MainWindow(this);
        windowSystem.AddWindow(window);

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        chatGui.ChatMessage += OnChatMessage;
        clientState.Logout += OnLogout;
        framework.Update += OnUpdate;

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Quill. /quill <template> inserts it · /quill send <template> · /quill ooc <text> · /quill next · /quill stop · /quill probe",
        });

        var words = Path.Combine(pluginInterface.AssemblyLocation.Directory!.FullName, "Data", "words.txt.gz");
        Task.Run(() =>
        {
            try { spell = SpellChecker.LoadGzip(words); }
            catch (Exception e) { SpellLoadFailed = true; log.Warning(e, "Spell check word list could not be loaded"); }
        });
    }

    public void Dispose()
    {
        commandManager.RemoveHandler(CommandName);
        framework.Update -= OnUpdate;
        clientState.Logout -= OnLogout;
        chatGui.ChatMessage -= OnChatMessage;
        pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        windowSystem.RemoveAllWindows();
    }

    public void ToggleMain() => window.Toggle();

    public bool MainOpen => window.IsOpen;

    public void OpenSettings() => window.OpenSettings();

    public void SaveConfig() => AppConfig.Save(pluginInterface, "Quill14", Config);

    public Draft ActiveDraft => Config.Drafts[Math.Clamp(Config.ActiveDraft, 0, Config.Drafts.Count - 1)];

    public string MyName => playerState.IsLoaded ? playerState.CharacterName : "";
    public ulong MyContentId => playerState.IsLoaded ? playerState.ContentId : 0;

    public string Me => Config.RpNames.TryGetValue(MyContentId, out var rp) && rp.Trim().Length > 0 ? rp.Trim() : MyName;

    public TemplateContext Context() => new(Me, targetManager.Target?.Name.TextValue ?? "", LastTellSender);

    public string? Prefix(Channel channel, string tellTarget) => Channels.Prefix(channel, tellTarget, LastTellSender);

    /// <summary>Split preview for the window, cached so an unchanged draft is not re-split every frame.</summary>
    public SplitResult Split(string text, string prefix, bool wrapOoc)
    {
        var markers = string.Join('\u0001', Config.Markers.Select(m => $"{m.Open}\u0002{m.Close}\u0002{m.Ooc}"));
        var c = splitCache;
        if (c.Text != text || c.Prefix != prefix || c.Cont != Config.Continuation || c.Wrap != wrapOoc || c.Markers != markers)
            splitCache = (text, prefix, Config.Continuation, wrapOoc, markers, Splitter.Split(text, prefix, Config.Markers, Config.Continuation, wrapOoc));
        return splitCache.Result;
    }

    public IEnumerable<Template> VisibleTemplates =>
        Config.Templates.Where(t => t.CharacterId == 0 || t.CharacterId == MyContentId);

    /// <summary>Resolves a template for use and counts the use (for search order).</summary>
    public Resolved UseTemplate(Template t, IReadOnlyDictionary<string, string> fills)
    {
        var r = Templates.Resolve(t.Body, Context(), fills, lastPicks, Random.Shared);
        if (r.Missing.Count == 0)
        {
            t.Uses++;
            t.LastUsedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            SaveConfig();
        }

        return r;
    }

    /// <summary>
    /// Queues a post. Reply is resolved to the locked last-tell sender now, so every chunk goes to the same person.
    /// Every chunk is sanitiser-checked on the next tick before the first one is sent; one bad chunk sends nothing.
    /// </summary>
    public bool Post(string text, Channel channel, string tellTarget, bool wrapOoc)
    {
        LastError = null;
        if (Busy) return Fail("A post is still being sent. Stop it or wait.");
        var prefix = Prefix(channel, tellTarget);
        if (prefix is null)
            return Fail(channel == Channel.Reply ? "No tell received since Quill loaded — nobody to reply to." : "Tell target must be Forename Surname@World.");

        var split = Splitter.Split(text, prefix, Config.Markers, Config.Continuation, wrapOoc);
        if (split.Problems.Count > 0) return Fail(split.Problems[0]);
        if (split.Chunks.Count == 0) return Fail("Nothing to send.");
        var bad = split.Chunks.FindIndex(c => !Channels.IsSendable(prefix, c));
        if (bad >= 0) return Fail($"Chunk {bad + 1} cannot be sent (line break, control character, or too long).");

        var target = channel == Channel.Reply ? LastTellSender : channel == Channel.Tell ? tellTarget.Trim() : "";
        pending = (prefix, split.Chunks, new HistoryEntry
        {
            Text = text, Channel = channel, Target = target, WrapOoc = wrapOoc,
            SentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        return true;
    }

    public void ReleaseNext() => outbox.ReleaseNext();
    public void ReleaseAll() => outbox.ReleaseAll();

    public void Stop()
    {
        var left = outbox.Count - outbox.Next;
        outbox.Stop();
        pending = null;
        if (left > 0) Alert($"Stopped: {left} chunk(s) not sent.");
    }

    private bool Fail(string message)
    {
        LastError = message;
        return false;
    }

    public void Notify(string text) => Alert(text);

    /// <summary>The current target as a tell target, when it is a player.</summary>
    public string? TargetAsTell() =>
        targetManager.Target is IPlayerCharacter { HomeWorld.IsValid: true } pc ? $"{pc.Name.TextValue}@{pc.HomeWorld.Value.Name.ExtractText()}" : null;

    private void Alert(string text) => chatGui.Print(new XivChatEntry { Type = XivChatType.Echo, Message = $"[Quill] {text}" });

    // ---- commands ----

    private void OnCommand(string _, string args)
    {
        var a = args.Trim();
        var (verb, rest) = a.IndexOf(' ') is var sp and > 0 ? (a[..sp].ToLowerInvariant(), a[(sp + 1)..].Trim()) : (a.ToLowerInvariant(), "");
        switch (verb)
        {
            case "":
                window.Toggle();
                return;
            case "probe":
                probeRequested = true;
                return;
            case "next":
                if (pending is null && !outbox.Active) Alert("Nothing waiting to send.");
                ReleaseNext();
                return;
            case "stop":
                Stop();
                return;
            case "ooc" when rest.Length > 0:
                var d = ActiveDraft;
                if (!Post(rest, d.Channel, d.TellTarget, true)) Alert(LastError ?? "Not sent.");
                return;
            case "send" when rest.Length > 0:
                if (Templates.Find(VisibleTemplates, rest) is { } ts) window.BeginTemplate(ts, send: true);
                else Alert($"No template named \"{rest}\".");
                return;
        }

        if (Templates.Find(VisibleTemplates, a) is { } t) window.BeginTemplate(t, send: false);
        else Alert($"No template named \"{a}\". /quill opens the window.");
    }

    // ---- chat and tick ----

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (message.LogKind != XivChatType.TellIncoming) return;
        lastTellRaw = message.Sender.TextValue;
        var payload = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        // RowRef.Value throws on a bad row; an exception in a chat handler must never reach the game
        var sender = payload is { World.IsValid: true } ? $"{payload.PlayerName}@{payload.World.Value.Name.ExtractText()}" : null;
        if (Channels.IsValidTarget(sender)) LastTellSender = sender!;
    }

    private void OnLogout(int type, int code)
    {
        outbox.Stop();
        pending = null;
        LastTellSender = "";
    }

    private void OnUpdate(IFramework _)
    {
        if (probeRequested)
        {
            probeRequested = false;
            Probe();
        }

        if (!clientState.IsLoggedIn) return;

        if (pending is { } p)
        {
            pending = null;
            var dirty = p.Bodies.FindIndex(b => !IsClean(b));
            if (dirty >= 0)
            {
                LastError = $"Nothing sent: the game would change some characters in chunk {dirty + 1}.";
                Alert(LastError);
                return;
            }

            outbox.Start(p.Bodies.Select(b => $"{p.Prefix} {b}").ToList(), Config.StepMode);
            Config.History.Insert(0, p.Entry);
            if (Config.History.Count > Configuration.MaxHistory) Config.History.RemoveAt(Configuration.MaxHistory);
            SaveConfig();
        }

        if (outbox.Due(Environment.TickCount64, Config.SpacingMs + Random.Shared.Next(JitterMs)) is { } line && !Send(line))
        {
            outbox.Stop();
            LastError = "The chat box was not available; the rest of the post was not sent.";
        }
    }

    private void Probe()
    {
        var d = ActiveDraft;
        var prefix = Prefix(d.Channel, d.TellTarget);
        Alert($"Last tell sender: \"{LastTellSender}\" (raw sender text \"{lastTellRaw}\"). Target: \"{Context().Target}\". Me: \"{Me}\".");
        Alert($"Draft \"{d.Name}\": channel {Channels.Label(d.Channel)}, prefix {prefix ?? "(none — target invalid)"}.");
        if (prefix is null) return;
        var split = Splitter.Split(d.Text, prefix, Config.Markers, Config.Continuation, d.WrapOoc);
        for (var i = 0; i < split.Chunks.Count; i++)
            Alert($"Chunk {i + 1}/{split.Chunks.Count}: {Splitter.Bytes(prefix) + 1 + Splitter.Bytes(split.Chunks[i])} bytes, sanitiser {(IsClean(split.Chunks[i]) ? "clean" : "WOULD CHANGE IT")}.");
        Alert($"Spell check: {(spell is { } s ? $"{s.Count} words loaded" : SpellLoadFailed ? "failed to load" : "loading")}.");
    }

    // Only plugin-built lines reach here: a fixed prefix plus a chunk that passed IsSendable and the sanitiser check.
    private static unsafe bool Send(string text)
    {
        var ui = UIModule.Instance();
        if (ui == null) return false;
        var utf8 = Utf8String.FromString(text);
        ui->ProcessChatBoxEntry(utf8);
        utf8->Dtor(true);
        return true;
    }

    private static unsafe bool IsClean(string text)
    {
        var utf8 = Utf8String.FromString(text);
        utf8->SanitizeString(TextEntities);
        var clean = utf8->ToString() == text;
        utf8->Dtor(true);
        return clean;
    }
}
