using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace KosHome;

public sealed class KosHomePlugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/koshome";
    private readonly WindowSystem windowSystem = new("KosHome");
    private readonly MainWindow mainWindow;

    internal Configuration Config { get; }

    internal List<AppSlot> Apps { get; } = AppCatalog.All.Select(a => new AppSlot(a)).ToList();

    public KosHomePlugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += mainWindow.Toggle;
        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => mainWindow.Toggle())
        {
            HelpMessage = "Open KosHome: switch apps on and off, open them and their settings.",
        });

        foreach (var app in Apps.Where(a => !Config.DisabledApps.Contains(a.Info.Id)))
            Start(app);
    }

    public void Dispose()
    {
        for (var i = Apps.Count - 1; i >= 0; i--)
            Stop(Apps[i]);
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.OpenConfigUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        windowSystem.RemoveAllWindows();
    }

    internal void SetEnabled(AppSlot app, bool on)
    {
        if (on) Config.DisabledApps.Remove(app.Info.Id); else Config.DisabledApps.Add(app.Info.Id);
        PluginInterface.SavePluginConfig(Config);
        if (on) Start(app); else Stop(app);
    }

    internal void Start(AppSlot app)
    {
        if (app.Instance is not null)
            return;

        // Two copies would double every command handler and, for NameplateOffset, fight over the same game memory.
        if (PluginInterface.InstalledPlugins.Any(p => p.InternalName == app.Info.Id && p.IsLoaded))
        {
            app.Status = $"The old {app.Info.Id} plugin is still loaded. Disable it in /xlplugins, then press Retry.";
            return;
        }

        app.Instance = app.Info.Create(PluginInterface); // Create logs the exception and returns null on failure
        app.Status = app.Instance is null ? "Failed to start; the reason is in /xllog." : null;
        if (app.Instance is not null)
            Log.Information("{App} started", app.Info.Id);
    }

    internal void Stop(AppSlot app)
    {
        var instance = app.Instance;
        app.Instance = null;
        app.Status = null;
        if (instance is null)
            return;

        try
        {
            instance.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{App} threw while stopping", app.Info.Id);
        }
    }
}

internal sealed class AppSlot(AppInfo info)
{
    public AppInfo Info { get; } = info;

    public IKosApp? Instance { get; set; }

    public string? Status { get; set; }
}
