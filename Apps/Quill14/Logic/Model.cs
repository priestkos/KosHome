using System.Text;

namespace KosHome.Apps.Quill14;

public sealed class Draft
{
    public string Name { get; set; } = "Scene";
    public string Text { get; set; } = "";
    public Channel Channel { get; set; } = Channel.Say;
    public string TellTarget { get; set; } = "";
    public bool WrapOoc { get; set; }
}

public sealed class HistoryEntry
{
    public string Text { get; set; } = "";
    public Channel Channel { get; set; }
    public string Target { get; set; } = "";
    public long SentUnix { get; set; }
    public bool WrapOoc { get; set; }
}

public static class Markup
{
    /// <summary>Wraps the selection in a pair, or inserts an empty pair at the cursor. Returns the new text and a cursor inside the pair.</summary>
    public static (string Text, int Cursor) Wrap(string text, int selStart, int selEnd, MarkerPair pair)
    {
        var a = Math.Clamp(Math.Min(selStart, selEnd), 0, text.Length);
        var b = Math.Clamp(Math.Max(selStart, selEnd), 0, text.Length);
        var result = text[..a] + pair.Open + text[a..b] + pair.Close + text[b..];
        return (result, b + pair.Open.Length);
    }

    /// <summary>
    /// What a marker button does. With a selection, wraps it. Without one, wraps the plain run of text around the
    /// cursor on its line (between the neighbouring marked spans, trimmed): "*waves* Hello|" + Dialogue gives
    /// "*waves* \"Hello\"". Inside a marked span, or where the run is empty, it inserts an empty pair.
    /// The cursor lands after the closer when text was wrapped, inside the pair when it was empty.
    /// </summary>
    public static (string Text, int Cursor) WrapSmart(string text, int selStart, int selEnd, MarkerPair pair, IReadOnlyList<MarkerPair> markers)
    {
        var a = Math.Clamp(Math.Min(selStart, selEnd), 0, text.Length);
        var b = Math.Clamp(Math.Max(selStart, selEnd), 0, text.Length);
        var added = pair.Open.Length + pair.Close.Length;
        if (a != b) return (Wrap(text, a, b, pair).Text, b + added);

        var spans = Splitter.MarkedSpans(text, markers);
        if (spans.Any(s => s.Start < a && a < s.End)) return Wrap(text, a, a, pair);
        var lineStart = a == 0 ? 0 : text.LastIndexOf('\n', a - 1) + 1;
        var nl = text.IndexOf('\n', a);
        var start = Math.Max(spans.Where(s => s.End <= a).Select(s => s.End).DefaultIfEmpty(0).Max(), lineStart);
        var end = Math.Min(spans.Where(s => s.Start >= a).Select(s => s.Start).DefaultIfEmpty(text.Length).Min(), nl < 0 ? text.Length : nl);
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        if (start >= end) return Wrap(text, a, a, pair);
        return (Wrap(text, start, end, pair).Text, end + added);
    }

    public static int CharToByte(string text, int charIndex) => Encoding.UTF8.GetByteCount(text.AsSpan(0, Math.Clamp(charIndex, 0, text.Length)));

    /// <summary>ImGui reports positions as UTF-8 byte offsets; C# strings index UTF-16 chars.</summary>
    public static int ByteToChar(string text, int byteOffset)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Encoding.UTF8.GetCharCount(bytes, 0, Math.Clamp(byteOffset, 0, bytes.Length));
    }
}

/// <summary>
/// One post in flight. Send-all releases every chunk; step mode releases one per press. Chunks leave in order,
/// never closer than the spacing, and nothing is retried: Stop drops what is left.
/// </summary>
public sealed class Outbox
{
    public const int MinSpacingMs = 2000;

    private List<string> lines = [];
    private long nextAt;

    public int Next { get; private set; }
    public int Released { get; private set; }
    public int Count => lines.Count;
    public bool Active => Next < lines.Count;

    public bool Start(IReadOnlyList<string> fullLines, bool stepMode)
    {
        if (Active || fullLines.Count == 0) return false;
        lines = [.. fullLines];
        Next = 0;
        Released = stepMode ? 1 : lines.Count;
        return true;
    }

    public void ReleaseNext()
    {
        if (Active && Released < lines.Count) Released = Math.Max(Released, Next + 1);
    }

    public void ReleaseAll()
    {
        if (Active) Released = lines.Count;
    }

    public void Stop()
    {
        lines = [];
        Next = Released = 0;
    }

    /// <summary>The next line due at this time, or null. spacingMs is floored at the house minimum.</summary>
    public string? Due(long nowMs, int spacingMs)
    {
        if (!Active || Next >= Released || nowMs < nextAt) return null;
        nextAt = nowMs + Math.Max(MinSpacingMs, spacingMs);
        return lines[Next++];
    }

    public bool WaitingForPress => Active && Next >= Released;
}
