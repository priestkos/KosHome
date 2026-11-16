namespace KosHome;

internal static class AppCatalog
{
    public static readonly AppInfo[] All =
    [
        new("Calc14", "Calc14", "A fast, pretty calculator.", "/calc", false,
            pi => pi.Create<Apps.Calc14.Calc14Plugin>()),
        new("Aetherpath", "Aetherpath", "Prices every way to get where you're going - and tells you where your free and favored slots earn the most gil.", "/aetherpath, /ap", true,
            pi => pi.Create<Apps.Aetherpath.Plugin>()),
        new("FrontlineAdvisor", "FrontlineAdvisor", "Who is winning, who to hit, who is about to matter.", "/fla", true,
            pi => pi.Create<Apps.FrontlineAdvisor.Plugin>()),
        new("GateAlert", "Gate Alert", "Yells at you the moment your GATE opens in the Gold Saucer.", "/gatealert, /gatetest", true,
            pi => pi.Create<Apps.GateAlert.GateAlertPlugin>()),
        new("NameplateOffset", "Nameplate Offset", "Raise or lower the nameplate above a character's head.", "/npoffset", false,
            pi => pi.Create<Apps.NameplateOffset.Plugin>()),
        new("Quill14", "Quill14", "Roleplay posts: smart splitting, markup, templates and paced sending to Say, Party, Tell or Reply.", "/quill", true,
            pi => pi.Create<Apps.Quill14.Plugin>()),
        new("VenueLedger", "VenueLedger", "Every venue you know, on one clock.", "/venue", false,
            pi => pi.Create<Apps.VenueLedger.Plugin>()),
        new("Wardrobe14", "Wardrobe14", "A cross-collection view of your Penumbra mods; bulk toggles and Glamourer looks to follow.", "/pmm", false,
            pi => pi.Create<Apps.Wardrobe14.Plugin>()),
        new("WorldClock14", "WorldClock14", "One time, every timezone, one click to chat.", "/tz, /tzconfig", true,
            pi => pi.Create<Apps.WorldClock14.Plugin>()),
    ];
}
