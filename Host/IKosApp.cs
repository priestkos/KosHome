using Dalamud.Plugin;

namespace KosHome;

/// <summary>One folded-in plugin. Built by <see cref="IDalamudPluginInterface.Create{T}"/>, so its
/// [PluginService] members are injected exactly as they were when it was a plugin of its own.</summary>
public interface IKosApp : IDisposable
{
    void OpenMain();

    void OpenSettings();
}
