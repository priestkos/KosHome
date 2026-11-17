using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace KosHome;

/// <summary>One folded-in plugin. Built by <see cref="IDalamudPluginInterface.Create{T}"/>, so its
/// [PluginService] members are injected exactly as they were when it was a plugin of its own.</summary>
public interface IKosApp : IDisposable
{
    /// <summary>A tap on the home screen: opens the app's main window, or closes it when it is already open.</summary>
    void ToggleMain();

    /// <summary>Whether the main window is open now (the running dot under its icon; Open or Close on the Apps page).</summary>
    bool MainOpen { get; }

    void OpenSettings();

    /// <summary>The app's windows. KosHome draws them inside its own window, so the app never draws them itself.</summary>
    IReadOnlyList<IWindow> Windows { get; }
}

/// <summary>An app that lives on the home screen as a widget instead of an icon (see <see cref="AppInfo.Widget"/>).
/// Drawn inside KosHome's window, in a glass card the host provides.</summary>
public interface IKosWidget
{
    void DrawWidget(WidgetFrame frame);
}

/// <summary>What the host tells a widget about its card: the inner width to lay out in (the window's content region is
/// wider than the card, so a widget must never measure against it), and the clock card's large display font.</summary>
public readonly record struct WidgetFrame(float Width, Dalamud.Interface.ManagedFontAtlas.IFontHandle DisplayFont);
