using Dalamud.Configuration;
using Newtonsoft.Json;

namespace KosHome.Apps.Quill14;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const int MaxHistory = 20;

    public int Version { get; set; } = 1;

    // Replace: Json.NET would otherwise append saved items to these defaults on every load.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<MarkerPair> Markers { get; set; } = MarkerPair.Defaults();

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<Template> Templates { get; set; } = Template.Defaults();

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<Draft> Drafts { get; set; } = [new Draft { Name = "Scene 1" }];

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<HistoryEntry> History { get; set; } = [];

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public HashSet<string> UserWords { get; set; } = [];

    /// <summary>RP alias per character (content id) used for {me}.</summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<ulong, string> RpNames { get; set; } = [];

    public string Continuation { get; set; } = " (c)";
    public int SpacingMs { get; set; } = 2500;
    public bool StepMode { get; set; }
    public bool SpellCheck { get; set; } = true;
    public int ActiveDraft { get; set; }

    /// <summary>Pulls hand-edited values back inside what the UI offers.</summary>
    public void Normalize()
    {
        Markers ??= MarkerPair.Defaults();
        Markers.RemoveAll(m => m is null);
        foreach (var m in Markers) { m.Name ??= ""; m.Open ??= ""; m.Close ??= ""; }
        Templates ??= [];
        Templates.RemoveAll(t => t is null);
        Drafts ??= [];
        Drafts.RemoveAll(d => d is null);
        if (Drafts.Count == 0) Drafts.Add(new Draft { Name = "Scene 1" });
        foreach (var d in Drafts)
        {
            if (!Enum.IsDefined(d.Channel)) d.Channel = Channel.Say;
            d.Text ??= "";
            d.TellTarget ??= "";
            d.Name ??= "Scene";
        }

        foreach (var t in Templates)
        {
            t.Name ??= "";
            t.Tags ??= "";
            t.Body ??= "";
            if (t.Channel is { } c && !Enum.IsDefined(c)) t.Channel = null;
        }
        History ??= [];
        History.RemoveAll(h => h is null || !Enum.IsDefined(h.Channel));
        foreach (var h in History) { h.Text ??= ""; h.Target ??= ""; }
        if (History.Count > MaxHistory) History.RemoveRange(MaxHistory, History.Count - MaxHistory);
        UserWords ??= [];
        RpNames ??= [];
        Continuation ??= "";
        SpacingMs = Math.Clamp(SpacingMs, Outbox.MinSpacingMs, 10_000);
        ActiveDraft = Math.Clamp(ActiveDraft, 0, Drafts.Count - 1);
    }
}
