using Newtonsoft.Json;
using Xunit;

namespace KosHome.Host.Tests;

public class TabletTests
{
    [Fact]
    public void OpenTrackerReportsEachTransitionOnceAndForgetsStoppedItems()
    {
        var t = new OpenTracker<string>();
        List<string> opened = [], closed = [];
        void Step(params (string, bool)[] now) { opened.Clear(); closed.Clear(); t.Step(now, opened, closed); }

        Step(("calc", false), ("quill", true)); // first sight: open counts as opening, closed is nothing
        Assert.Equal(["quill"], opened);
        Assert.Empty(closed);

        Step(("calc", true), ("quill", true));
        Assert.Equal(["calc"], opened);
        Assert.Empty(closed);

        Step(("calc", false), ("quill", true));
        Assert.Empty(opened);
        Assert.Equal(["calc"], closed);

        Step(("calc", false)); // quill's app stopped: no OnClose for a disposed window
        Assert.Empty(opened);
        Assert.Empty(closed);

        Step(("calc", false), ("quill", true)); // started again: opens afresh
        Assert.Equal(["quill"], opened);
    }

    [Fact]
    public void QuickAppsDefaultOnceAndARemovedShortcutStaysRemoved()
    {
        var fresh = new Configuration();
        Assert.True(fresh.Sanitize());
        Assert.Equal(Configuration.DefaultQuickApps.Order(), fresh.QuickApps!.Order());

        // Saved with only Calc14 left: reloads as exactly that, not merged back with the defaults.
        fresh.QuickApps!.IntersectWith(["Calc14"]);
        var loaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(fresh))!;
        Assert.False(loaded.Sanitize());
        Assert.Equal(["Calc14"], loaded.QuickApps!);

        // Every shortcut removed is a choice too.
        loaded.QuickApps!.Clear();
        var empty = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(loaded))!;
        empty.Sanitize();
        Assert.Empty(empty.QuickApps!);
    }

    [Fact]
    public void DefaultShortcutsAreRealApps() =>
        Assert.All(Configuration.DefaultQuickApps, id => Assert.Contains(AppCatalog.All, a => a.Id == id));
}
