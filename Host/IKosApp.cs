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
}
