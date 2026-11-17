using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;

namespace KosHome.Apps.Calc14;

public sealed class Calc14Plugin : IKosApp
{
    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private const string CommandName = "/calc";
    private readonly WindowSystem windowSystem;
    private readonly CalculatorWindow calculatorWindow;

    public Calc14Plugin()
    {
        calculatorWindow = new CalculatorWindow();
        windowSystem = new WindowSystem("Calc14");
        windowSystem.AddWindow(calculatorWindow);


        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the calculator. Usage: /calc [expression] to evaluate inline.",
        });

        Log.Debug("Calc14 loaded");
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        calculatorWindow.Dispose();
        windowSystem.RemoveAllWindows();
    }

    public void ToggleMain() => calculatorWindow.Toggle();

    public bool MainOpen => calculatorWindow.IsOpen;

    /// <summary>Drawn inside KosHome's window, not by this app's WindowSystem.</summary>
    public IReadOnlyList<IWindow> Windows => windowSystem.Windows;

    public void OpenSettings() => calculatorWindow.Toggle();

    private void OnCommand(string command, string args)
    {
        args = args.Trim();
        if (args.Length > 0)
        {
            var result = CalculatorState.Evaluate(args);
            if (result is not null)
            {
                // Invariant formatting so history clicks parse via TrySetEntry.
                var formatted = CalculatorState.FormatResult(result.Value);
                Log.Info($"{args} = {formatted}");
                calculatorWindow.AddHistory($"{args} = {formatted}");
                return;
            }

            Log.Error($"Could not evaluate: {args}");
            return;
        }

        calculatorWindow.Toggle();
    }
}
