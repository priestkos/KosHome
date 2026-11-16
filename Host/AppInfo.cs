using Dalamud.Plugin;

namespace KosHome;

/// <param name="Id">The old plugin's InternalName: config file name and the "old plugin still loaded" check.</param>
public sealed record AppInfo(string Id, string Name, string Summary, string Commands, bool HasSettings,
                             Func<IDalamudPluginInterface, IKosApp?> Create);
