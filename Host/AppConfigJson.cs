using System.Reflection;
using System.Reflection.Metadata;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace KosHome;

/// <summary>Per-app config (de)serialisation with Dalamud's own settings (PluginConfigurations.cs: Objects type names,
/// simple assembly names). Reading goes through <see cref="AppTypeBinder"/>, which maps the old plugin's names
/// ("Quill14.Template, Quill14") onto the folded-in types ("KosHome.Apps.Quill14.Template, KosHome").</summary>
public static class AppConfigJson
{
    public static string Serialize(object config) =>
        JsonConvert.SerializeObject(config, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            TypeNameHandling = TypeNameHandling.Objects,
        });

    public static T? Deserialize<T>(string json, string appId) where T : class =>
        JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings
        {
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            TypeNameHandling = TypeNameHandling.Objects,
            SerializationBinder = new AppTypeBinder(appId),
        });

    /// <summary>Resolves "$type" without ever loading an assembly by name: only KosHome's own types (under either the
    /// old app assembly name or KosHome's), and System / System.Collections.Generic types from CoreLib, are allowed.
    /// Anything else in a settings file is refused rather than instantiated.</summary>
    private sealed class AppTypeBinder(string appId) : ISerializationBinder
    {
        private static readonly Assembly Kos = typeof(AppTypeBinder).Assembly;
        private static readonly Assembly Core = typeof(object).Assembly;
        private static readonly string KosName = Kos.GetName().Name!;

        public Type BindToType(string? assemblyName, string typeName)
        {
            var full = assemblyName is null ? typeName : $"{typeName}, {assemblyName}";
            Type? type = null;
            if (TypeName.TryParse(full, out var parsed))
                type = Resolve(parsed);
            return type ?? throw new JsonSerializationException($"Settings type not allowed: {full}");
        }

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName) =>
            throw new NotSupportedException("Reading only; Serialize uses the default binder.");

        private Type? Resolve(TypeName n)
        {
            if (n.IsConstructedGenericType)
            {
                var def = Resolve(n.GetGenericTypeDefinition());
                var args = n.GetGenericArguments().Select(Resolve).ToArray();
                return def is null || args.Any(a => a is null) ? null : def.MakeGenericType(args!);
            }

            if (n.IsSZArray)
                return Resolve(n.GetElementType())?.MakeArrayType();
            if (!n.IsSimple)
                return null;

            var asm = n.AssemblyName?.Name;
            var name = n.FullName;
            if (asm == appId && (name == appId || name.StartsWith(appId + ".", StringComparison.Ordinal)))
                return Kos.GetType("KosHome.Apps." + name);
            if (asm == KosName && name.StartsWith("KosHome.", StringComparison.Ordinal))
                return Kos.GetType(name);
            if (asm is null or "System.Private.CoreLib" or "mscorlib")
            {
                var t = Core.GetType(name);
                return t is not null && (t.Namespace == "System.Collections.Generic" ||
                                         (t.Namespace == "System" && (t.IsPrimitive || t.IsEnum || t == typeof(string) ||
                                          t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset) ||
                                          t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(Nullable<>))))
                           ? t
                           : null;
            }

            return null;
        }
    }
}
