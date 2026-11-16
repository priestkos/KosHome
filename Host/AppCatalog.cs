namespace KosHome;

internal static class AppCatalog
{
    public static readonly AppInfo[] All =
    [
        new("Calc14", "Calc14", "A fast, pretty calculator.", "/calc", false,
            pi => pi.Create<Apps.Calc14.Calc14Plugin>()),
    ];
}
