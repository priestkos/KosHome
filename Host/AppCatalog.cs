namespace KosHome;

internal static class AppCatalog
{
    public static readonly AppInfo[] All =
    [
        new("Calc14", "Calc14", "A fast, pretty calculator.", "/calc", false,
            pi => pi.Create<Apps.Calc14.Calc14Plugin>()),
        new("Aetherpath", "Aetherpath", "Prices every way to get where you're going - and tells you where your free and favored slots earn the most gil.", "/aetherpath, /ap", true,
            pi => pi.Create<Apps.Aetherpath.Plugin>()),
    ];
}
