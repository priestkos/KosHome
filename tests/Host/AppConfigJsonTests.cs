using System.Collections;
using Newtonsoft.Json;
using Xunit;
using Quill = KosHome.Apps.Quill14;
using Clock = KosHome.Apps.WorldClock14;

namespace KosHome.Host.Tests;

public class AppConfigJsonTests
{
    // Same shape as a config written by the standalone Quill14 plugin (synthetic values, real "$type" layout).
    private const string OldQuill = """
        {
          "$type": "Quill14.Configuration, Quill14",
          "Version": 1,
          "Markers": [ { "$type": "Quill14.MarkerPair, Quill14", "Name": "Speech", "Open": "\"", "Close": "\"", "Ooc": false } ],
          "Templates": [ { "$type": "Quill14.Template, Quill14", "Name": "Greet", "Tags": "hello", "Body": "Hi {name}", "Pinned": true, "Uses": 4 } ],
          "Drafts": [ { "$type": "Quill14.Draft, Quill14", "Name": "Scene 9", "Text": "Once upon a time", "Channel": 1 } ],
          "UserWords": [ "Eorzea" ],
          "RpNames": {
            "$type": "System.Collections.Generic.Dictionary`2[[System.UInt64, System.Private.CoreLib],[System.String, System.Private.CoreLib]], System.Private.CoreLib",
            "123456789": "Aria"
          },
          "SpacingMs": 1700
        }
        """;

    private const string OldClock = """
        {
          "$type": "WorldClock14.Configuration, WorldClock14",
          "Version": 1,
          "Zones": [ { "$type": "WorldClock14.TimeZoneEntry, WorldClock14", "Label": "JST", "Id": "Asia/Tokyo", "Enabled": true } ],
          "BaseZone": { "$type": "WorldClock14.TimeZoneEntry, WorldClock14", "Label": "Local", "Id": "", "IsLocal": true },
          "Format": { "$type": "WorldClock14.FormatOptions, WorldClock14", "Use24Hour": true, "Separator": " | " }
        }
        """;

    [Fact]
    public void OldQuill14ConfigLoadsWithNestedTypesAndDictionary()
    {
        var c = AppConfigJson.Deserialize<Quill.Configuration>(OldQuill, "Quill14")!;
        Assert.Equal("Speech", Assert.Single(c.Markers).Name);
        var t = Assert.Single(c.Templates);
        Assert.Equal(("Greet", "Hi {name}", true, 4), (t.Name, t.Body, t.Pinned, t.Uses));
        Assert.Equal("Once upon a time", Assert.Single(c.Drafts).Text);
        Assert.Contains("Eorzea", c.UserWords);
        Assert.Equal("Aria", c.RpNames[123456789UL]);
        Assert.Equal(1700, c.SpacingMs);
    }

    [Fact]
    public void OldWorldClock14ConfigLoadsWithNestedTypes()
    {
        var c = AppConfigJson.Deserialize<Clock.Configuration>(OldClock, "WorldClock14")!;
        Assert.Equal("Asia/Tokyo", Assert.Single(c.Zones).Id);
        Assert.True(c.BaseZone.IsLocal);
        Assert.True(c.Format.Use24Hour);
        Assert.Equal(" | ", c.Format.Separator);
    }

    [Fact]
    public void SavedConfigUsesKosHomeNamesAndReloads()
    {
        var json = AppConfigJson.Serialize(AppConfigJson.Deserialize<Quill.Configuration>(OldQuill, "Quill14")!);
        Assert.Contains("\"KosHome.Apps.Quill14.Template, KosHome\"", json);
        Assert.DoesNotContain(", Quill14\"", json);
        var again = AppConfigJson.Deserialize<Quill.Configuration>(json, "Quill14")!;
        Assert.Equal("Hi {name}", Assert.Single(again.Templates).Body);
        Assert.Equal("Aria", again.RpNames[123456789UL]);
    }

    [Theory]
    [InlineData("System.IO.FileInfo, System.Private.CoreLib")]       // CoreLib, but not a plain value/collection type
    [InlineData("Evil.Payload, Evil")]                                // unknown assembly: never loaded
    [InlineData("GateAlert.Configuration, GateAlert")]                // another app's old name is not this app's
    [InlineData("KosHome.KosHomePlugin, Quill14")]                    // old assembly name must still carry its own namespace
    [InlineData("System.Collections.Generic.List`1[[System.IO.FileInfo, System.Private.CoreLib]], System.Private.CoreLib")]
    public void UnexpectedTypeNamesAreRefused(string type)
    {
        var json = OldQuill.Replace("\"Quill14.Draft, Quill14\"", JsonConvert.ToString(type));
        Assert.Throws<JsonSerializationException>(() => AppConfigJson.Deserialize<Quill.Configuration>(json, "Quill14"));
    }

    /// <summary>Apps with a settings class (Calc14 has none).</summary>
    public static IEnumerable<object[]> Apps() =>
        AppCatalog.All.Where(a => typeof(AppCatalog).Assembly.GetType($"KosHome.Apps.{a.Id}.Configuration") is not null)
                  .Select(a => new object[] { a.Id });

    [Fact]
    public void EightOfNineAppsHaveSettings() =>
        Assert.Equal((9, 8), (AppCatalog.All.Length, Apps().Count()));

    [Theory]
    [MemberData(nameof(Apps))]
    public void EveryAppConfigRoundTrips(string appId)
    {
        var type = typeof(AppCatalog).Assembly.GetType($"KosHome.Apps.{appId}.Configuration");
        Assert.NotNull(type);
        var config = Activator.CreateInstance(type)!;
        FillLists(config); // one default element per list of KosHome objects, so nested "$type"s are exercised too
        var json = AppConfigJson.Serialize(config);
        var back = typeof(AppConfigJson).GetMethod(nameof(AppConfigJson.Deserialize))!.MakeGenericMethod(type)
                                        .Invoke(null, [json, appId]);
        Assert.IsType(type, back);
        Assert.Equal(json, AppConfigJson.Serialize(back!));
    }

    private static void FillLists(object config)
    {
        foreach (var p in config.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            if (p.GetValue(config) is not IList list || !p.PropertyType.IsGenericType)
                continue;
            var item = p.PropertyType.GetGenericArguments()[0];
            if (item.Assembly == typeof(AppCatalog).Assembly && item.GetConstructor(Type.EmptyTypes) is not null)
                list.Add(Activator.CreateInstance(item));
        }
    }

    /// <summary>The configs on this machine, read in place (never copied into the public repo). No-op elsewhere.</summary>
    [Theory]
    [MemberData(nameof(Apps))]
    public void LocalOldConfigLoads(string appId)
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        var path = appData is null ? null : Path.Combine(appData, "XIVLauncher", "pluginConfigs", appId + ".json");
        if (path is null || !File.Exists(path))
            return;
        var type = typeof(AppCatalog).Assembly.GetType($"KosHome.Apps.{appId}.Configuration")!;
        var loaded = typeof(AppConfigJson).GetMethod(nameof(AppConfigJson.Deserialize))!.MakeGenericMethod(type)
                                          .Invoke(null, [File.ReadAllText(path), appId]);
        Assert.IsType(type, loaded);
    }

    [Fact]
    public void WorldClockDropsExactRepeatZonesKeepingOrder()
    {
        // The standalone plugin saved its seeded zones twice over (IST JST UTC ET IST JST UTC ET).
        var c = new Clock.Configuration { Zones = [.. Clock.Defaults.CreateZones(), .. Clock.Defaults.CreateZones()] };
        var offTwin = Clock.TimeZoneEntry.Iana("JST", "Asia/Tokyo");
        offTwin.Enabled = false; // differs in one field, so it is the user's own entry and stays
        c.Zones.Add(offTwin);
        c.Normalize();
        Assert.Equal(["IST", "JST", "UTC", "ET", "JST"], c.Zones.Select(z => z.Label));
        Assert.False(c.Zones[^1].Enabled);
    }
}
