using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using KosHome.Apps.GateAlert.Core;
using KosHome.Apps.GateAlert.Windows;

namespace KosHome.Apps.GateAlert;

public sealed class GateAlertPlugin : IKosApp
{
    private const string CommandName = "/gatealert";
    private const string TestCommandName = "/gatetest";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;

    private readonly WindowSystem windowSystem = new("Gate Alert");
    private readonly Configuration configuration;
    private readonly GateAnnouncer announcer;
    private readonly GateWatcher watcher;
    private readonly ConfigWindow configWindow;

    public GateAlertPlugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IClientState clientState,
        IFramework framework,
        IToastGui toastGui,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.log = log;

        this.configuration = AppConfig.Load<Configuration>(pluginInterface, "GateAlert");
        this.configuration.Sanitize();
        this.announcer = new GateAnnouncer(chatGui, toastGui, log);
        this.watcher = new GateWatcher(chatGui, clientState, framework, log, this.configuration, this.announcer);
        this.configWindow = new ConfigWindow(this.configuration, this.watcher, this.SaveConfiguration);

        this.windowSystem.AddWindow(this.configWindow);

        this.commandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the GATE alert window. Subcommands: on | off | test [GATE name]",
        });

        this.commandManager.AddHandler(TestCommandName, new CommandInfo(this.OnTestCommand)
        {
            HelpMessage = "Fire a test GATE alert so you can see the chat line.",
        });


        this.watcher.Start();

        this.log.Information("Gate Alert loaded \u2014 watching the Gold Saucer Attendant");
    }

    private void OnCommand(string command, string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = parts.Length > 0 ? parts[0] : string.Empty;

        if (string.Equals(verb, "on", StringComparison.OrdinalIgnoreCase))
        {
            this.configuration.Enabled = true;
            this.SaveConfiguration();
            return;
        }

        if (string.Equals(verb, "off", StringComparison.OrdinalIgnoreCase))
        {
            this.configuration.Enabled = false;
            this.SaveConfiguration();
            return;
        }

        if (string.Equals(verb, "test", StringComparison.OrdinalIgnoreCase))
        {
            var name = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;
            this.watcher.FireTest(name, "the Event Square");
            return;
        }

        this.configWindow.Toggle();
    }

    private void OnTestCommand(string command, string args) =>
        this.watcher.FireTest(args.Trim(), "the Event Square");

    public void ToggleMain() => this.configWindow.Toggle();

    public bool MainOpen => this.configWindow.IsOpen;

    /// <summary>Drawn inside KosHome's window, not by this app's WindowSystem.</summary>
    public IReadOnlyList<IWindow> Windows => this.windowSystem.Windows;

    public void OpenSettings() => this.configWindow.IsOpen = true;

    private void SaveConfiguration()
    {
        this.configuration.Sanitize();
        AppConfig.Save(this.pluginInterface, "GateAlert", this.configuration);
    }

    public void Dispose()
    {
        this.watcher.Dispose();
        this.commandManager.RemoveHandler(TestCommandName);
        this.commandManager.RemoveHandler(CommandName);
        this.windowSystem.RemoveAllWindows();
    }
}
