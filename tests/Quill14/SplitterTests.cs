using System.Text;
using Xunit;

namespace KosHome.Apps.Quill14.Tests;

public class SplitterTests
{
    private static readonly List<MarkerPair> Markers = MarkerPair.Defaults();
    private const string Say = "/s";

    private static SplitResult Split(string text, string prefix = Say, string cont = " (c)", bool ooc = false, List<MarkerPair>? markers = null) =>
        Splitter.Split(text, prefix, markers ?? Markers, cont, ooc);

    private static string Sentences(int n) =>
        string.Join(' ', Enumerable.Range(0, n).Select(i => $"Sentence number {i} walks slowly across the quiet plaza."));

    private static int LineBytes(string prefix, string chunk) => Encoding.UTF8.GetByteCount(prefix) + 1 + Encoding.UTF8.GetByteCount(chunk);

    [Fact]
    public void ShortPostIsOneChunkWithoutMarker()
    {
        var r = Split("*waves* \"Hello there.\"");
        Assert.Equal(["*waves* \"Hello there.\""], r.Chunks);
        Assert.Empty(r.Problems);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void EmptyTextHasNoChunks() => Assert.Empty(Split("   \n\n  ").Chunks);

    [Fact]
    public void LongPostFitsAndKeepsEveryWord()
    {
        var text = Sentences(30);
        var r = Split(text);
        Assert.True(r.Chunks.Count > 2);
        Assert.All(r.Chunks, c => Assert.True(LineBytes(Say, c) <= Channels.MaxLineBytes));
        var rebuilt = string.Join(' ', r.Chunks.Select(c => c.EndsWith(" (c)") ? c[..^4] : c));
        Assert.Equal(text, rebuilt);
    }

    [Fact]
    public void BreaksAtSentenceEnd()
    {
        var r = Split(Sentences(30));
        Assert.All(r.Chunks.SkipLast(1), c => Assert.EndsWith(". (c)", c));
    }

    [Fact]
    public void ContinuationOnAllButLastWithNumbering()
    {
        var r = Split(Sentences(30), cont: " ({n}/{total})");
        var n = r.Chunks.Count;
        for (var i = 0; i < n - 1; i++) Assert.EndsWith($" ({i + 1}/{n})", r.Chunks[i]);
        Assert.DoesNotContain("/", r.Chunks[^1].Replace("/s", ""));
    }

    [Fact]
    public void ArrowMarkerPreset()
    {
        var r = Split(Sentences(20), cont: " >>>");
        Assert.All(r.Chunks.SkipLast(1), c => Assert.EndsWith(" >>>", c));
        Assert.False(r.Chunks[^1].EndsWith(">>>"));
    }

    [Fact]
    public void LongerPrefixMeansSmallerChunks()
    {
        var text = Sentences(30);
        var tell = "/tell Abcdefghijklmno Abcdefghijklmno@Phoenix";
        var r = Split(text, tell);
        Assert.All(r.Chunks, c => Assert.True(LineBytes(tell, c) <= Channels.MaxLineBytes));
        Assert.True(r.Chunks.Count >= Split(text).Chunks.Count);
    }

    [Fact]
    public void ActionSpanningASplitIsClosedAndReopened()
    {
        var text = "*" + Sentences(15) + "*";
        var r = Split(text);
        Assert.True(r.Chunks.Count > 1);
        foreach (var c in r.Chunks)
        {
            var body = c.EndsWith(" (c)") ? c[..^4] : c;
            Assert.StartsWith("*", body);
            Assert.EndsWith("*", body);
            Assert.Equal(0, body.Count(ch => ch == '*') % 2);
        }

        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void NestedPairsCloseInReverseOrder()
    {
        var text = "*She leans in.* \"" + Sentences(12) + " (" + Sentences(6) + ") end.\"";
        var r = Split(text);
        Assert.Empty(r.Warnings);
        foreach (var c in r.Chunks)
        {
            var body = c.EndsWith(" (c)") ? c[..^4] : c;
            Assert.Equal(body.Count(ch => ch == '('), body.Count(ch => ch == ')'));
            Assert.Equal(0, body.Count(ch => ch == '"') % 2);
        }
    }

    [Fact]
    public void DoubleParenOoc()
    {
        var markers = new List<MarkerPair> { new("OOC", "((", "))", true) };
        var r = Split("((" + Sentences(15) + "))", markers: markers);
        foreach (var c in r.Chunks)
        {
            var body = c.EndsWith(" (c)") ? c[..^4] : c;
            Assert.StartsWith("((", body);
            Assert.EndsWith("))", body);
        }
    }

    [Fact]
    public void WholePostOocWrapsEveryChunk()
    {
        var r = Split(Sentences(20), ooc: true);
        Assert.All(r.Chunks.SkipLast(1), c => Assert.Matches(@"^\(.*\) \(c\)$", c));
        Assert.Matches(@"^\(.*\)$", r.Chunks[^1]);
        Assert.All(r.Chunks, c => Assert.True(LineBytes(Say, c) <= Channels.MaxLineBytes));
    }

    [Fact]
    public void UnclosedMarkerWarnsButDoesNotBlock()
    {
        var r = Split("*waves and smiles");
        Assert.Single(r.Chunks);
        Assert.Empty(r.Problems);
        Assert.Contains(r.Warnings, w => w.Contains("Action"));
    }

    [Fact]
    public void BlankLineForcesNewChunkAndSingleNewlineIsASpace()
    {
        var r = Split("Line one\nstill one.\n\nParagraph two.");
        Assert.Equal(["Line one still one. (c)", "Paragraph two."], r.Chunks);
    }

    [Fact]
    public void JapaneseWithoutSpacesSplitsAfterKutenWithinBudget()
    {
        var text = string.Concat(Enumerable.Repeat("今日はとても良い天気ですね。", 40));
        var r = Split(text);
        Assert.True(r.Chunks.Count > 1);
        Assert.All(r.Chunks, c => Assert.True(LineBytes(Say, c) <= Channels.MaxLineBytes));
        Assert.All(r.Chunks.SkipLast(1), c => Assert.EndsWith("。 (c)", c));
        Assert.Equal(text, string.Concat(r.Chunks.Select(c => c.EndsWith(" (c)") ? c[..^4] : c)));
    }

    [Fact]
    public void SurrogatePairsAreNeverCut()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 400));
        var r = Split(text, cont: "");
        Assert.All(r.Chunks, c => Assert.False(char.IsHighSurrogate(c[^1]) || char.IsLowSurrogate(c[0])));
        Assert.Equal(text, string.Concat(r.Chunks));
    }

    [Fact]
    public void TooManyChunksIsAProblem()
    {
        var r = Split(Sentences(200));
        Assert.True(r.Chunks.Count > Splitter.MaxChunks);
        Assert.Contains(r.Problems, p => p.Contains("limit"));
    }

    [Fact]
    public void LeadingSlashIsAProblem() => Assert.Contains(Split("/shout hi").Problems, p => p.Contains("/"));

    [Fact]
    public void RandomPostsKeepAllInvariants()
    {
        var words = new[] { "the", "lantern", "flickers,", "softly.", "\"Hello", "there.\"", "*She", "smiles*", "(brb)", "どうぞ。", "x", "Mmm...", "really?!" };
        var rng = new Random(1234);
        for (var seed = 0; seed < 2000; seed++)
        {
            var n = rng.Next(1, 260);
            var text = string.Join(' ', Enumerable.Range(0, n).Select(_ => words[rng.Next(words.Length)]));
            var prefix = rng.Next(3) switch { 0 => "/s", 1 => "/p", _ => "/tell Ann Lee@Phoenix" };
            var ooc = rng.Next(4) == 0;
            var r = Splitter.Split(text, prefix, Markers, " (c)", ooc);
            Assert.All(r.Chunks, c => Assert.True(LineBytes(prefix, c) <= Channels.MaxLineBytes, $"seed {seed}"));
            Assert.All(r.Chunks, c => Assert.DoesNotContain("  ", c));
            if (r.Warnings.Count == 0)
            {
                foreach (var c in r.Chunks)
                {
                    var body = c.EndsWith(" (c)") ? c[..^4] : c;
                    Assert.True(body.Count(ch => ch == '*') % 2 == 0, $"seed {seed}: {body}");
                    Assert.True(body.Count(ch => ch == '"') % 2 == 0, $"seed {seed}: {body}");
                }
            }
        }
    }
}
