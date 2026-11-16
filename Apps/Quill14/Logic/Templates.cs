using System.Text.RegularExpressions;

namespace KosHome.Apps.Quill14;

public sealed class Template
{
    public string Name { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Body { get; set; } = "";

    /// <summary>Null = use the current draft's channel.</summary>
    public Channel? Channel { get; set; }

    public bool Pinned { get; set; }
    public int Uses { get; set; }
    public long LastUsedUnix { get; set; }

    /// <summary>0 = every character; otherwise only shown for that content id.</summary>
    public ulong CharacterId { get; set; }

    public Template Copy() => (Template)MemberwiseClone();

    public static List<Template> Defaults() =>
    [
        new() { Name = "Greeting", Tags = "hello arrive", Body = "*{mefirst} {offers a small wave|nods politely|smiles warmly} at {targetfirst}.* \"Well met.\"" },
        new() { Name = "BRB", Tags = "ooc away", Body = "(brb, {?reason})" },
        new() { Name = "Farewell", Tags = "leave goodbye", Body = "*{mefirst} {gathers their things|bows their head} and takes their leave.* (Thanks for the RP!)" },
    ];
}

/// <summary>Values the plugin knows at the moment a template is used. Empty means "not available".</summary>
public sealed record TemplateContext(string Me, string Target, string Tell);

public sealed record Resolved(string Text, List<string> Missing);

public static partial class Templates
{
    /// <summary>The {?name} fill-ins a body asks for, in first-seen order.</summary>
    public static List<string> FillIns(string body) =>
        Token().Matches(body).Select(m => m.Groups[1].Value).Where(v => v.StartsWith('?') && v.Length > 1)
            .Select(v => v[1..]).Distinct().ToList();

    /// <summary>
    /// Resolves {me} {mefirst} {target} {targetfirst} {tell} {tellfirst}, {?fill-ins} and {a|b|c} choices.
    /// A choice avoids the option it picked last time (lastPicks is keyed by the choice text), so a reused
    /// line does not repeat itself. Unknown {words} are left as typed; an unavailable variable is left in
    /// place and reported in Missing so the caller can refuse to send it.
    /// </summary>
    public static Resolved Resolve(string body, TemplateContext ctx, IReadOnlyDictionary<string, string> fills,
        Dictionary<string, string> lastPicks, Random rng)
    {
        var missing = new List<string>();
        var text = Token().Replace(body, m =>
        {
            var key = m.Groups[1].Value;
            if (key.Contains('|'))
            {
                var options = key.Split('|');
                var pool = options.Where(o => !lastPicks.TryGetValue(key, out var last) || o != last).ToArray();
                if (pool.Length == 0) pool = options;
                var pick = pool[rng.Next(pool.Length)];
                lastPicks[key] = pick;
                return pick;
            }

            if (key.StartsWith('?'))
                return fills.TryGetValue(key[1..], out var f) && f.Trim().Length > 0 ? f.Trim() : Missed(m.Value);

            string? value = key.ToLowerInvariant() switch
            {
                "me" => ctx.Me,
                "mefirst" => First(ctx.Me),
                "target" => ctx.Target,
                "targetfirst" => First(ctx.Target),
                "tell" => ctx.Tell,
                "tellfirst" => First(ctx.Tell),
                _ => null,
            };
            return value is null ? m.Value : value.Length > 0 ? value : Missed(m.Value);
        });
        return new Resolved(text, missing);

        string Missed(string token)
        {
            if (!missing.Contains(token)) missing.Add(token);
            return token;
        }
    }

    /// <summary>"Ann Lee@World" → "Ann"; an RP alias with no space stays whole.</summary>
    public static string First(string name)
    {
        var at = name.IndexOf('@');
        var bare = at >= 0 ? name[..at] : name;
        var space = bare.IndexOf(' ');
        return space > 0 ? bare[..space] : bare;
    }

    /// <summary>
    /// Search order: pinned first, then match quality (name subsequence beats tag/body substring), then
    /// recently-and-often used. A non-matching template is dropped when the query is not empty.
    /// </summary>
    public static List<Template> Rank(IEnumerable<Template> templates, string query, long nowUnix)
    {
        var q = query.Trim().ToLowerInvariant();
        return templates
            .Select(t => (t, score: Score(t, q)))
            .Where(x => q.Length == 0 || x.score > 0)
            .OrderByDescending(x => x.t.Pinned)
            .ThenByDescending(x => x.score)
            .ThenByDescending(x => Frecency(x.t, nowUnix))
            .ThenBy(x => x.t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.t).ToList();
    }

    public static double Frecency(Template t, long nowUnix) =>
        t.Uses / (1.0 + Math.Max(0, nowUnix - t.LastUsedUnix) / 86400.0);

    private static int Score(Template t, string q)
    {
        if (q.Length == 0) return 0;
        var name = t.Name.ToLowerInvariant();
        if (name.StartsWith(q)) return 4;
        if (name.Contains(q)) return 3;
        if (IsSubsequence(q, name)) return 2;
        return t.Tags.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Body.Contains(q, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static bool IsSubsequence(string q, string s)
    {
        var i = 0;
        foreach (var c in s) if (i < q.Length && c == q[i]) i++;
        return i == q.Length;
    }

    /// <summary>Exact name (case-insensitive), else the only name starting with it.</summary>
    public static Template? Find(IEnumerable<Template> templates, string name)
    {
        var list = templates.ToList();
        var exact = list.FirstOrDefault(t => t.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var starts = list.Where(t => t.Name.StartsWith(name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return starts.Count == 1 ? starts[0] : null;
    }

    [GeneratedRegex(@"\{([^{}\r\n]+)\}")]
    private static partial Regex Token();
}
