using System.IO.Compression;
using System.Text.RegularExpressions;

namespace KosHome.Apps.Quill14;

public sealed record Misspelling(string Word, int Index);

/// <summary>
/// Basic English spell check: a word list lookup plus one-edit suggestions. Latin words only (Japanese is
/// skipped), and a capitalised word that does not start a sentence is taken as a name and skipped.
/// shortcut: no word frequencies, so suggestions are ordered by edit kind only; add a permissively licensed
/// frequency list if the order turns out to matter.
/// </summary>
public sealed partial class SpellChecker
{
    private const string Letters = "abcdefghijklmnopqrstuvwxyz";
    private readonly HashSet<string> words;

    /// <summary>Chat shorthand and Eorzean words the English list lacks, so they are never flagged.</summary>
    public static readonly string[] ChatWords =
    [
        "lol", "lmao", "brb", "afk", "ooc", "ic", "rp", "rper", "rpers", "rping", "btw", "idk", "imo", "omg", "ty", "tyvm", "np",
        "irl", "gn", "gm", "gtg", "ttyl", "ofc", "pls", "plz", "thx", "ok", "okay", "hehe", "haha", "mhm", "hm", "hmm", "mmm",
        "ffxiv", "eorzea", "eorzean", "gridania", "gridanian", "limsa", "lominsa", "uldah", "ishgard", "ishgardian", "kugane",
        "doma", "ala", "mhigo", "mhigan", "sharlayan", "garlemald", "garlean", "thavnair", "tural", "miqote", "aura", "viera",
        "hrothgar", "lalafell", "roegadyn", "elezen", "hyur", "gil", "aether", "aetheryte", "aetheric", "primal", "primals",
        "chocobo", "chocobos", "moogle", "moogles", "kupo", "mog", "ascian", "ascians", "allagan", "scion", "scions",
    ];

    /// <summary>Very common words, offered before rarer one-edit neighbours (hte → the, not het).</summary>
    private static readonly HashSet<string> Common =
    [
        "the", "be", "to", "of", "and", "a", "in", "that", "have", "it", "for", "not", "on", "with", "he", "as", "you", "do",
        "at", "this", "but", "his", "by", "from", "they", "we", "say", "her", "she", "or", "an", "will", "my", "one", "all",
        "would", "there", "their", "what", "so", "up", "out", "if", "about", "who", "get", "which", "go", "me", "when", "make",
        "can", "like", "time", "no", "just", "him", "know", "take", "people", "into", "year", "your", "good", "some", "could",
        "them", "see", "other", "than", "then", "now", "look", "only", "come", "its", "over", "think", "also", "back", "after",
        "use", "two", "how", "our", "work", "first", "well", "way", "even", "new", "want", "because", "any", "these", "give",
        "day", "most", "us", "was", "were", "is", "are", "has", "had", "did", "said", "does", "been", "here", "where", "why",
        "through", "very", "before", "around", "again", "friend", "going", "really", "right", "thing", "little", "eyes",
    ];

    public SpellChecker(IEnumerable<string> wordList) =>
        words = new HashSet<string>(wordList.Concat(ChatWords).Select(w => w.Trim().ToLowerInvariant()).Where(w => w.Length > 0), StringComparer.Ordinal);

    public int Count => words.Count;

    public static SpellChecker LoadGzip(string path)
    {
        using var file = File.OpenRead(path);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var list = new List<string>(380_000);
        while (reader.ReadLine() is { } line) list.Add(line);
        return new SpellChecker(list);
    }

    public bool IsKnown(string word, ICollection<string> userWords)
    {
        var key = Key(word);
        return key.Length < 2 || words.Contains(key) || userWords.Contains(key);
    }

    public List<Misspelling> Check(string text, ICollection<string> userWords)
    {
        var result = new List<Misspelling>();
        foreach (Match m in Word().Matches(text))
        {
            var w = m.Value;
            if (w.Skip(1).Any(char.IsUpper)) continue;                          // ALLCAPS, CamelCase
            if (char.IsUpper(w[0]) && !StartsSentence(text, m.Index)) continue; // a name
            if (InsideBraces(text, m.Index)) continue;                          // {template} tokens
            if (!IsKnown(w, userWords)) result.Add(new Misspelling(w, m.Index));
        }

        return result;
    }

    /// <summary>
    /// Known words one edit away, most likely typo first: swapped letters, then one wrong letter, then one extra,
    /// then one missing. Max five.
    /// </summary>
    public List<string> Suggest(string word, int max = 5)
    {
        var w = Key(word);
        var swaps = new List<string>();
        var replaces = new List<string>();
        var deletes = new List<string>();
        var inserts = new List<string>();
        for (var i = 0; i <= w.Length; i++)
        {
            if (i < w.Length - 1) swaps.Add(w[..i] + w[i + 1] + w[i] + w[(i + 2)..]);
            if (i < w.Length) deletes.Add(w.Remove(i, 1));
            foreach (var c in Letters)
            {
                if (i < w.Length && c != w[i]) replaces.Add(w[..i] + c + w[(i + 1)..]);
                inserts.Add(w.Insert(i, c.ToString()));
            }
        }

        // a swap that keeps the first letter (teh → the) beats one that moves it (teh → eth)
        return swaps.OrderByDescending(s => s.Length > 0 && w.Length > 0 && s[0] == w[0]).Concat(replaces).Concat(deletes).Concat(inserts)
            .Where(s => s != w && words.Contains(s))
            .Distinct()
            .OrderByDescending(Common.Contains) // stable: edit-kind order is kept within each group
            .Take(max)
            .Select(s => char.IsUpper(word[0]) ? char.ToUpperInvariant(s[0]) + s[1..] : s)
            .ToList();
    }

    /// <summary>Replaces every whole-word, same-case occurrence of wrong.</summary>
    public static string ReplaceWord(string text, string wrong, string right) =>
        Regex.Replace(text, $@"(?<![A-Za-z']){Regex.Escape(wrong)}(?![A-Za-z'])", right.Replace("$", "$$"));

    /// <summary>The dictionary key: lowercase, possessive 's dropped, apostrophes removed (don't → dont).</summary>
    public static string Key(string word)
    {
        var w = word.ToLowerInvariant().Replace('’', '\'');
        if (w.EndsWith("'s")) w = w[..^2];
        return w.Replace("'", "");
    }

    private static bool StartsSentence(string text, int index)
    {
        var i = index - 1;
        while (i >= 0 && " \t\r\n\"*(~_".Contains(text[i])) i--;
        return i < 0 || ".!?…".Contains(text[i]);
    }

    private static bool InsideBraces(string text, int index)
    {
        var open = text.LastIndexOf('{', index);
        return open >= 0 && text.IndexOf('}', open) is var close && close >= index && text.LastIndexOf('}', index) < open;
    }

    [GeneratedRegex(@"[A-Za-z][A-Za-z'’]*[A-Za-z]|[A-Za-z]")]
    private static partial Regex Word();
}
