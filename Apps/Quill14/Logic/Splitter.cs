using System.Text;
using System.Text.RegularExpressions;

namespace KosHome.Apps.Quill14;

/// <summary>A markup pair such as *action*, "dialogue" or (OOC). Open may equal Close.</summary>
public sealed class MarkerPair
{
    public string Name { get; set; } = "";
    public string Open { get; set; } = "";
    public string Close { get; set; } = "";

    /// <summary>The pair the whole-post OOC toggle and the quick OOC line use.</summary>
    public bool Ooc { get; set; }

    public MarkerPair() { }
    public MarkerPair(string name, string open, string close, bool ooc = false) => (Name, Open, Close, Ooc) = (name, open, close, ooc);

    public bool IsUsable => Open.Length > 0 && Close.Length > 0;

    public static List<MarkerPair> Defaults() =>
    [
        new("Action", "*", "*"),
        new("Dialogue", "\"", "\""),
        new("OOC", "(", ")", true),
    ];

    public static MarkerPair OocOf(IReadOnlyList<MarkerPair> markers) =>
        markers.FirstOrDefault(m => m.Ooc && m.IsUsable) ?? new MarkerPair("OOC", "(", ")", true);
}

/// <summary>Problems block sending; warnings (an unclosed marker) are shown but do not.</summary>
public sealed record SplitResult(List<string> Chunks, List<string> Problems, List<string> Warnings);

public static partial class Splitter
{
    public const int MaxChunks = 10;
    private const string SentenceEnd = ".!?…";
    private const string CjkSentenceEnd = "。！？";
    private const string CjkSoftBreak = "、，";
    private const string Trailers = "\"'*)]」』~_"; // may follow sentence punctuation: "Hi." / *waves.*

    public static string FormatContinuation(string marker, int n, int total) =>
        marker.Replace("{n}", n.ToString()).Replace("{total}", total.ToString());

    /// <summary>
    /// Splits a post into chat-sized chunks for a given command prefix. Breaks at a sentence end, else a space,
    /// else between characters; a marker pair cut by a split is closed at the chunk end and reopened on the next.
    /// A blank line forces a new chunk; single line breaks become spaces.
    /// </summary>
    public static SplitResult Split(string text, string prefix, IReadOnlyList<MarkerPair> markers, string continuation, bool wrapOoc)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        var bodies = new List<string>();
        var usable = markers.Where(m => m.IsUsable).OrderByDescending(m => Math.Max(m.Open.Length, m.Close.Length)).ToList();
        var ooc = MarkerPair.OocOf(markers);

        var lineBudget = Channels.MaxLineBytes - Bytes(prefix) - 1 - (wrapOoc ? Bytes(ooc.Open) + Bytes(ooc.Close) : 0);
        var bodyBudget = lineBudget - Bytes(FormatContinuation(continuation, MaxChunks, MaxChunks));
        if (bodyBudget < 60)
        {
            problems.Add("The prefix, OOC brackets and continuation marker leave too little room per line.");
            return new SplitResult([], problems, warnings);
        }

        var paragraphs = ParagraphBreak().Split(text.Replace("\r\n", "\n"))
            .Select(p => Whitespace().Replace(p, " ").Trim()).Where(p => p.Length > 0).ToList();

        for (var p = 0; p < paragraphs.Count; p++)
        {
            var para = paragraphs[p];
            var finalBudget = p == paragraphs.Count - 1 ? lineBudget : bodyBudget;
            var stack = new List<MarkerPair>();
            var pos = 0;
            while (pos < para.Length)
            {
                var reopen = string.Concat(stack.Select(m => m.Open));
                var scan = Scan(para, pos, stack, usable, finalBudget - Bytes(reopen));
                if (!scan.Full) scan = Scan(para, pos, stack, usable, bodyBudget - Bytes(reopen));
                if (scan.Cut <= pos) { problems.Add("Could not split the text (a marker is longer than a line)."); return new SplitResult([], problems, warnings); }

                var piece = para[pos..scan.Cut].TrimEnd();
                bodies.Add(reopen + piece + (scan.Full ? "" : string.Concat(Enumerable.Reverse(scan.State).Select(m => m.Close))));
                stack = scan.State;
                pos = scan.Cut;
                while (pos < para.Length && para[pos] == ' ') pos++;
            }

            foreach (var m in stack) warnings.Add($"Unclosed {m.Name} marker {m.Open}…{m.Close}.");
        }

        if (bodies.Count > MaxChunks) problems.Add($"{bodies.Count} chunks — the limit is {MaxChunks} per post. Split it into two posts.");
        if (bodies.Count > 0 && bodies[0].StartsWith('/')) problems.Add("A post cannot start with / (it would read as a command).");

        var chunks = new List<string>(bodies.Count);
        for (var i = 0; i < bodies.Count; i++)
        {
            var body = wrapOoc ? ooc.Open + bodies[i] + ooc.Close : bodies[i];
            chunks.Add(i < bodies.Count - 1 ? body + FormatContinuation(continuation, i + 1, bodies.Count) : body);
        }

        return new SplitResult(chunks, problems, warnings);
    }

    private readonly record struct ScanResult(int Cut, List<MarkerPair> State, bool Full);

    // Walks forward from pos while the text plus the closers it would need still fits the budget,
    // remembering the furthest sentence, word and character break.
    private static ScanResult Scan(string para, int pos, List<MarkerPair> start, List<MarkerPair> markers, int budget)
    {
        var state = new List<MarkerPair>(start);
        var bytes = 0;
        (int Cut, List<MarkerPair> State, int Bytes) sentence = (-1, start, 0), word = (-1, start, 0), hard = (-1, start, 0);
        var i = pos;
        while (i < para.Length)
        {
            var next = new List<MarkerPair>(state);
            var len = ApplyMarker(para, i, next, markers);
            if (len == 0) len = char.IsHighSurrogate(para[i]) && i + 1 < para.Length ? 2 : 1;
            var unit = Encoding.UTF8.GetByteCount(para.AsSpan(i, len));
            if (bytes + unit + next.Sum(m => Bytes(m.Close)) > budget) break;

            state = next;
            bytes += unit;
            i += len;
            if (i == para.Length) return new ScanResult(i, state, true);

            var prev = para[i - 1];
            if (para[i] == ' ')
            {
                word = (i, state, bytes);
                var k = i - 1;
                while (k > pos && Trailers.Contains(para[k])) k--;
                if (SentenceEnd.Contains(para[k])) sentence = (i, state, bytes);
            }

            if (CjkSentenceEnd.Contains(prev)) sentence = (i, state, bytes);
            else if (CjkSoftBreak.Contains(prev)) word = (i, state, bytes);
            if (para[i] != ' ') hard = (i, state, bytes);
        }

        var pick = sentence.Cut > 0 && sentence.Bytes * 2 >= budget ? sentence : word.Cut > 0 ? word : hard;
        return new ScanResult(pick.Cut, pick.State, false);
    }

    /// <summary>Top-level marked spans [Start, End) such as *…* or "…"; an unclosed opener makes no span.</summary>
    public static List<(int Start, int End)> MarkedSpans(string text, IReadOnlyList<MarkerPair> markers)
    {
        var usable = markers.Where(m => m.IsUsable).OrderByDescending(m => Math.Max(m.Open.Length, m.Close.Length)).ToList();
        var spans = new List<(int, int)>();
        var stack = new List<MarkerPair>();
        var start = 0;
        for (var i = 0; i < text.Length;)
        {
            var before = stack.Count;
            var len = ApplyMarker(text, i, stack, usable);
            if (len == 0) { i++; continue; }
            if (before == 0) start = i;
            i += len;
            if (before > 0 && stack.Count == 0) spans.Add((start, i));
        }

        return spans;
    }

    // Applies a marker token at i to the open-marker stack and returns its length, or 0 when none starts here.
    private static int ApplyMarker(string s, int i, List<MarkerPair> stack, List<MarkerPair> markers)
    {
        foreach (var m in markers)
        {
            var open = stack.LastIndexOf(m);
            if (open >= 0 && s.AsSpan(i).StartsWith(m.Close, StringComparison.Ordinal))
            {
                stack.RemoveAt(open);
                return m.Close.Length;
            }

            if (s.AsSpan(i).StartsWith(m.Open, StringComparison.Ordinal) && (m.Open != m.Close || open < 0))
            {
                stack.Add(m);
                return m.Open.Length;
            }
        }

        return 0;
    }

    public static int Bytes(string s) => Encoding.UTF8.GetByteCount(s);

    [GeneratedRegex(@"\n[ \t]*\n\s*")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
