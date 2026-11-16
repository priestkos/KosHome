using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace KosHome;

/// <summary>Per-app config (de)serialisation with Dalamud's own settings (PluginConfigurations.cs), plus a binder that
/// maps the old plugin's "$type" names ("Quill14.Template, Quill14") onto the folded-in types
/// ("KosHome.Apps.Quill14.Template, KosHome"), so a config saved by the old plugin loads unchanged.</summary>
public static class AppConfigJson
{
    public static string Serialize(object config) =>
        JsonConvert.SerializeObject(config, Formatting.Indented, Settings(null));

    public static T? Deserialize<T>(string json, string appId) where T : class =>
        JsonConvert.DeserializeObject<T>(json, Settings(appId));

    private static JsonSerializerSettings Settings(string? appId) => new()
    {
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        TypeNameHandling = TypeNameHandling.Objects,
        SerializationBinder = appId is null ? null : new OldPluginBinder(appId),
    };

    private sealed class OldPluginBinder(string appId) : DefaultSerializationBinder
    {
        public override Type BindToType(string? assemblyName, string typeName)
        {
            if (assemblyName == appId && (typeName == appId || typeName.StartsWith(appId + ".", StringComparison.Ordinal)))
            {
                return typeof(OldPluginBinder).Assembly.GetType("KosHome.Apps." + typeName)
                       ?? throw new JsonSerializationException($"No folded-in type for {typeName}, {assemblyName}");
            }

            return base.BindToType(assemblyName, typeName);
        }
    }
}
