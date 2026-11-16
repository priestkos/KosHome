using Dalamud.Plugin;
using Dalamud.Utility;

namespace KosHome;

/// <summary>Replaces GetPluginConfig/SavePluginConfig for apps: KosHome is one plugin with one config slot, so each
/// app keeps its own file at pluginConfigs/KosHome/&lt;Id&gt;.json. First load falls back to the old plugin's
/// pluginConfigs/&lt;Id&gt;.json, which is read and never changed.</summary>
public static class AppConfig
{
    public static T Load<T>(IDalamudPluginInterface pi, string appId) where T : class, new()
    {
        var path = Path.Combine(pi.GetPluginConfigDirectory(), appId + ".json");
        var oldPath = Path.Combine(pi.ConfigFile.DirectoryName!, appId + ".json");
        var source = File.Exists(path) ? path : File.Exists(oldPath) ? oldPath : null;
        if (source is null)
            return new T();

        try
        {
            return AppConfigJson.Deserialize<T>(File.ReadAllText(source), appId) ?? new T();
        }
        catch (Exception ex)
        {
            // Keep the unreadable file: the next Save would otherwise overwrite the user's only copy.
            if (source == path)
                File.Copy(path, $"{path}.bad-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: false);
            KosHomePlugin.Log.Error(ex, "{App}: could not read settings from {Path}; starting from defaults", appId, source);
            return new T();
        }
    }

    public static void Save(IDalamudPluginInterface pi, string appId, object config) =>
        FilesystemUtil.WriteAllTextSafe(Path.Combine(pi.GetPluginConfigDirectory(), appId + ".json"),
                                        AppConfigJson.Serialize(config));
}
