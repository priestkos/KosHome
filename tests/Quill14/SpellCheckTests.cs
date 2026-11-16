using Xunit;

namespace KosHome.Apps.Quill14.Tests;

public class SpellCheckTests
{
    private static readonly SpellChecker Small = new(["the", "receive", "smile", "smiles", "at", "her", "dont", "im", "and", "wave", "waves", "a", "hello", "there"]);
    private static readonly HashSet<string> None = [];

    [Fact]
    public void FindsTypos()
    {
        var found = Small.Check("She smiels at teh door.", None).Select(m => m.Word).ToList();
        Assert.Contains("smiels", found);
        Assert.Contains("teh", found);
        Assert.DoesNotContain("at", found);
    }

    [Fact]
    public void SkipsNamesAllCapsTemplateTokensAndJapanese()
    {
        var found = Small.Check("Hello there Zanthor. OK, {targetfirst} waves at Mira こんにちは", None).Select(m => m.Word).ToList();
        Assert.DoesNotContain("Zanthor", found);
        Assert.DoesNotContain("OK", found);
        Assert.DoesNotContain("targetfirst", found);
        Assert.DoesNotContain("Mira", found);
    }

    [Fact]
    public void CapitalisedFirstWordOfASentenceIsChecked()
    {
        Assert.Contains("Teh", Small.Check("Teh door.", None).Select(m => m.Word));
        Assert.Contains("Smiels", Small.Check("Hello. Smiels.", None).Select(m => m.Word));
        Assert.Contains("Smiels", Small.Check("*Smiels*", None).Select(m => m.Word));
    }

    [Fact]
    public void ApostrophesAndPossessives()
    {
        Assert.Empty(Small.Check("dont don't I'm smile's", None));
        Assert.Equal("dont", SpellChecker.Key("Don’t"));
        Assert.Equal("ann", SpellChecker.Key("Ann's"));
    }

    [Fact]
    public void UserDictionary()
    {
        Assert.NotEmpty(Small.Check("zorblax", None));
        Assert.Empty(Small.Check("zorblax", new HashSet<string> { "zorblax" }));
    }

    [Fact]
    public void Suggestions()
    {
        Assert.Contains("the", Small.Suggest("teh"));
        Assert.Contains("receive", Small.Suggest("recieve"));
        Assert.Contains("smiles", Small.Suggest("smiels"));
        Assert.Contains("The", Small.Suggest("Teh"));
        Assert.True(Small.Suggest("zzzzzz").Count == 0);
    }

    [Fact]
    public void ReplaceWordIsWholeWordOnly()
    {
        Assert.Equal("the bathe the", SpellChecker.ReplaceWord("teh bathe teh", "teh", "the"));
        Assert.Equal("cost $5", SpellChecker.ReplaceWord("cost x", "x", "$5"));
    }

    [Fact]
    public void ShippedWordListLoads()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "words.txt.gz");
        var real = SpellChecker.LoadGzip(path);
        Assert.True(real.Count > 300_000);
        Assert.Empty(real.Check("She smiles warmly and whispered gently. I don't know.", None));
        Assert.Equal(["teh", "recieve"], real.Check("teh recieve", None).Select(m => m.Word));
        Assert.Equal("the", real.Suggest("teh")[0]);
        Assert.Equal("the", real.Suggest("hte")[0]);
        Assert.Empty(real.Check("lol brb, sorry. Lol. Kupo! Limsa.", None));
        Assert.Contains("receive", real.Suggest("recieve"));
    }
}
