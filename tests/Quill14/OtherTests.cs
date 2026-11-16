using Xunit;

namespace KosHome.Apps.Quill14.Tests;

public class ChannelTests
{
    [Fact]
    public void EnumNumbersArePinned()
    {
        Assert.Equal(0, (int)Channel.Say);
        Assert.Equal(1, (int)Channel.Party);
        Assert.Equal(2, (int)Channel.Tell);
        Assert.Equal(3, (int)Channel.Reply);
    }

    [Fact]
    public void Prefixes()
    {
        Assert.Equal("/s", Channels.Prefix(Channel.Say, "", ""));
        Assert.Equal("/p", Channels.Prefix(Channel.Party, "", ""));
        Assert.Equal("/tell Ann Lee@Phoenix", Channels.Prefix(Channel.Tell, " Ann Lee@Phoenix ", ""));
        Assert.Null(Channels.Prefix(Channel.Tell, "Ann", ""));
    }

    [Fact]
    public void ReplyUsesTheLockedSenderAsATell()
    {
        Assert.Equal("/tell Bo Ray@Odin", Channels.Prefix(Channel.Reply, "Ann Lee@Phoenix", "Bo Ray@Odin"));
        Assert.Null(Channels.Prefix(Channel.Reply, "Ann Lee@Phoenix", ""));
    }

    [Theory]
    [InlineData("Ann Lee@World /random 999")]
    [InlineData("Ann Lee@World\n/shout hi")]
    [InlineData("Ann Lee")]
    [InlineData("A Lee@World")]
    [InlineData("Ann Lee@W1")]
    [InlineData("")]
    [InlineData(null)]
    public void BadTargetsAreRejected(string? target) => Assert.False(Channels.IsValidTarget(target));

    [Theory]
    [InlineData("Ann Lee@Phoenix")]
    [InlineData("Y'shtola Rhul@Sargatanas")]
    [InlineData("Jean-Luc Pic@Odin")]
    public void GoodTargets(string target) => Assert.True(Channels.IsValidTarget(target));

    [Fact]
    public void Sendable()
    {
        Assert.True(Channels.IsSendable("/s", "hello"));
        Assert.False(Channels.IsSendable("/s", ""));
        Assert.False(Channels.IsSendable("/s", "/shout hi"));
        Assert.False(Channels.IsSendable("/s", "a\nb"));
        Assert.False(Channels.IsSendable("/s", new string('x', 468))); // 2 + 1 + 468 = 471 bytes
        Assert.True(Channels.IsSendable("/s", new string('x', 467)));  // exactly 470
    }
}

public class TemplateTests
{
    private static readonly TemplateContext Ctx = new("Ann Lee", "Bo Ray", "Cy Fox@Odin");

    private static Resolved R(string body, Dictionary<string, string>? fills = null, Dictionary<string, string>? last = null, int seed = 1) =>
        Templates.Resolve(body, Ctx, fills ?? [], last ?? [], new Random(seed));

    [Fact]
    public void Variables()
    {
        var r = R("{me}/{mefirst} {target}/{targetfirst} {tell}/{tellfirst}");
        Assert.Equal("Ann Lee/Ann Bo Ray/Bo Cy Fox@Odin/Cy", r.Text);
        Assert.Empty(r.Missing);
    }

    [Fact]
    public void MissingTargetIsReportedAndLeftInPlace()
    {
        var r = Templates.Resolve("*waves at {targetfirst}*", Ctx with { Target = "" }, new Dictionary<string, string>(), [], new Random(1));
        Assert.Equal("*waves at {targetfirst}*", r.Text);
        Assert.Equal(["{targetfirst}"], r.Missing);
    }

    [Fact]
    public void UnknownBracesAreUntouched() => Assert.Equal("{hello} world", R("{hello} world").Text);

    [Fact]
    public void FillIns()
    {
        Assert.Equal(["reason", "mood"], Templates.FillIns("({?reason}) {?mood} {?reason} {me}"));
        var r = R("(brb, {?reason})", new Dictionary<string, string> { ["reason"] = " food " });
        Assert.Equal("(brb, food)", r.Text);
        Assert.Equal(["{?reason}"], R("(brb, {?reason})").Missing);
    }

    [Fact]
    public void ChoiceNeverRepeatsTheLastPick()
    {
        var last = new Dictionary<string, string>();
        string? prev = null;
        for (var i = 0; i < 200; i++)
        {
            var pick = R("{a|b|c}", last: last, seed: i).Text;
            Assert.Contains(pick, new[] { "a", "b", "c" });
            Assert.NotEqual(prev, pick);
            prev = pick;
        }
    }

    [Fact]
    public void SingleOptionChoiceStillWorks()
    {
        var last = new Dictionary<string, string>();
        Assert.Equal("x", R("{x|x}", last: last).Text);
        Assert.Equal("x", R("{x|x}", last: last).Text);
    }

    [Fact]
    public void FirstName()
    {
        Assert.Equal("Ann", Templates.First("Ann Lee@Phoenix"));
        Assert.Equal("Ann", Templates.First("Ann Lee"));
        Assert.Equal("Moonshadow", Templates.First("Moonshadow"));
        Assert.Equal("", Templates.First(""));
    }

    [Fact]
    public void RankPinnedFirstThenMatchThenFrecency()
    {
        var now = 1_000_000L;
        var greet = new Template { Name = "Greeting", Tags = "hello" };
        var bye = new Template { Name = "Farewell", Tags = "goodbye", Uses = 10, LastUsedUnix = now };
        var brb = new Template { Name = "BRB", Pinned = true };
        var all = new[] { greet, bye, brb };

        Assert.Equal([brb, bye, greet], Templates.Rank(all, "", now));
        Assert.Equal([greet], Templates.Rank(all, "gre", now));
        Assert.Equal([bye], Templates.Rank(all, "goodbye", now));
        Assert.Equal([greet], Templates.Rank(all, "gtg", now)); // subsequence of "greeting"
    }

    [Fact]
    public void Find()
    {
        var list = new[] { new Template { Name = "Greeting" }, new Template { Name = "Greet bow" }, new Template { Name = "BRB" } };
        Assert.Equal("BRB", Templates.Find(list, "brb")!.Name);
        Assert.Equal("Greeting", Templates.Find(list, "greeting")!.Name);
        Assert.Null(Templates.Find(list, "gre")); // ambiguous
        Assert.Equal("Greet bow", Templates.Find(list, "greet b")!.Name);
    }

    [Fact]
    public void DefaultTemplatesResolve()
    {
        foreach (var t in Template.Defaults())
        {
            var fills = Templates.FillIns(t.Body).ToDictionary(n => n, _ => "x");
            var r = Templates.Resolve(t.Body, Ctx, fills, [], new Random(1));
            Assert.Empty(r.Missing);
            Assert.DoesNotContain("{", r.Text);
        }
    }
}

public class MarkupTests
{
    private static readonly MarkerPair Star = new("Action", "*", "*");

    [Fact]
    public void WrapsSelection() => Assert.Equal(("I *wave* hi", 7), Markup.Wrap("I wave hi", 2, 6, Star));

    [Fact]
    public void ReversedSelectionIsTheSame() => Assert.Equal(Markup.Wrap("I wave hi", 2, 6, Star), Markup.Wrap("I wave hi", 6, 2, Star));

    [Fact]
    public void EmptySelectionInsertsPairWithCursorInside() => Assert.Equal(("ab**", 3), Markup.Wrap("ab", 2, 2, Star));

    [Fact]
    public void OutOfRangeIsClamped() => Assert.Equal(("ab**", 3), Markup.Wrap("ab", 10, 99, Star));

    private static readonly List<MarkerPair> All = MarkerPair.Defaults();
    private static readonly MarkerPair Quote = All[1];
    private static readonly MarkerPair Ooc = All[2];

    private static (string, int) Smart(string text, int a, int b, MarkerPair p) => Markup.WrapSmart(text, a, b, p, All);

    [Fact]
    public void SmartWrapsSelectionWithCursorAfterCloser() => Assert.Equal(("I *wave* hi", 8), Smart("I wave hi", 2, 6, Star));

    [Fact]
    public void SmartWrapsTheWholePlainDraft() => Assert.Equal(("*waves*", 7), Smart("waves", 5, 5, Star));

    [Fact]
    public void SmartWrapsOnlyTheRunAfterTheLastMarkedSpan()
    {
        var text = "*waves* Hello there ";
        Assert.Equal(("*waves* \"Hello there\" ", 21), Smart(text, text.Length, text.Length, Quote));
    }

    [Fact]
    public void SmartWrapsTheRunBetweenSpans()
    {
        var text = "*waves* brb *sits*";
        Assert.Equal(("*waves* (brb) *sits*", 13), Smart(text, 10, 10, Ooc));
    }

    [Fact]
    public void SmartInsideASpanInsertsAnEmptyPair() => Assert.Equal(("*wa\"\"ves*", 4), Smart("*waves*", 3, 3, Quote));

    [Fact]
    public void SmartAfterASpanWithNothingTypedInsertsAnEmptyPair() => Assert.Equal(("*waves* \"\"", 9), Smart("*waves* ", 8, 8, Quote));

    [Fact]
    public void SmartOnEmptyDraft() => Assert.Equal(("**", 1), Smart("", 0, 0, Star));

    [Fact]
    public void SmartStaysOnTheCursorLine()
    {
        var text = "first line\nsecond";
        Assert.Equal(("first line\n*second*", 19), Smart(text, text.Length, text.Length, Star));
        Assert.Equal(("*first line*\nsecond", 12), Smart(text, 0, 0, Star));
    }

    [Fact]
    public void MarkedSpansIgnoreUnclosedOpeners()
    {
        Assert.Equal([(0, 7), (8, 12), (13, 16)], Splitter.MarkedSpans("*waves* \"hi\" (x) *open", All));
    }

    [Fact]
    public void CharToByteRoundTrips()
    {
        foreach (var s in new[] { "abc", "あいう", "😀x*" })
            for (var i = 0; i <= s.Length; i++)
                if (i == 0 || !char.IsHighSurrogate(s[i - 1])) Assert.Equal(i, Markup.ByteToChar(s, Markup.CharToByte(s, i)));
    }

    [Fact]
    public void ByteOffsetsToCharIndex()
    {
        Assert.Equal(1, Markup.ByteToChar("あいう", 3));
        Assert.Equal(3, Markup.ByteToChar("あいう", 9));
        Assert.Equal(2, Markup.ByteToChar("😀x", 4));
        Assert.Equal(3, Markup.ByteToChar("abc", 99));
    }
}

public class OutboxTests
{
    [Fact]
    public void SendAllIsOrderedAndSpaced()
    {
        var o = new Outbox();
        Assert.True(o.Start(["a", "b", "c"], stepMode: false));
        Assert.Equal("a", o.Due(0, 2500));
        Assert.Null(o.Due(2499, 2500));
        Assert.Equal("b", o.Due(2500, 2500));
        Assert.Equal("c", o.Due(5000, 2500));
        Assert.False(o.Active);
        Assert.Null(o.Due(99999, 2500));
    }

    [Fact]
    public void SpacingNeverBelowFloor()
    {
        var o = new Outbox();
        o.Start(["a", "b"], false);
        o.Due(0, 0);
        Assert.Null(o.Due(Outbox.MinSpacingMs - 1, 0));
        Assert.Equal("b", o.Due(Outbox.MinSpacingMs, 0));
    }

    [Fact]
    public void StepModeWaitsForEachPress()
    {
        var o = new Outbox();
        o.Start(["a", "b", "c"], stepMode: true);
        Assert.Equal("a", o.Due(0, 2000));
        Assert.True(o.WaitingForPress);
        Assert.Null(o.Due(10_000, 2000));
        o.ReleaseNext();
        o.ReleaseNext(); // a double press releases one, not two
        Assert.Equal("b", o.Due(10_000, 2000));
        Assert.Null(o.Due(20_000, 2000));
        o.ReleaseAll();
        Assert.Equal("c", o.Due(20_000, 2000));
        Assert.False(o.Active);
    }

    [Fact]
    public void PressDuringSpacingStillWaits()
    {
        var o = new Outbox();
        o.Start(["a", "b"], true);
        o.Due(0, 3000);
        o.ReleaseNext();
        Assert.Null(o.Due(1000, 3000));
        Assert.Equal("b", o.Due(3000, 3000));
    }

    [Fact]
    public void StopDropsTheRestAndNoSecondPostWhileActive()
    {
        var o = new Outbox();
        o.Start(["a", "b"], false);
        Assert.False(o.Start(["x"], false));
        o.Due(0, 2000);
        o.Stop();
        Assert.False(o.Active);
        Assert.Null(o.Due(99_999, 2000));
        Assert.True(o.Start(["x"], false));
        Assert.False(new Outbox().Start([], false));
    }
}
