using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Core;

public static class ModuleOptionsSerialization
{
    public const string OptionsKeySuffix = ":Options";

    // Options are ordinary configuration: every property flattens to a key under
    // DigitalBrain:Modules:{Name}:Options, so a host's own configuration (env, args, files)
    // overrides a code-declared default by standard precedence — no side channel.
    public static ModuleDefinition Compile<TModule, TOptions>(TOptions options)
        where TModule : IModule<TOptions> where TOptions : class, IModuleOptions, new()
    {
        ArgumentNullException.ThrowIfNull(options);
        var credentialProperty = typeof(TOptions).GetProperties().FirstOrDefault(p => ModuleSettingsValidation.IsCredentialName(p.Name));
        if (credentialProperty is not null)
        {
            throw new ArgumentException(
                $"{typeof(TOptions).Name}.{credentialProperty.Name} looks like a credential; credentials belong to the integrations registration, not module options.");
        }

        var ignoredProperty = typeof(TOptions).GetProperties().FirstOrDefault(property =>
            property.SetMethod is { IsPublic: true }
            && property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: not JsonIgnoreCondition.Never });
        if (ignoredProperty is not null)
        {
            throw new ArgumentException($"{typeof(TOptions).Name}.{ignoredProperty.Name} is writable but excluded from module options serialization.");
        }

        options.Validate();
        return new ModuleDefinition(typeof(TModule), FlattenOptions(options, typeof(TModule).Name));
    }

    public static Dictionary<string, string?> FlattenOptions<TOptions>(TOptions options, string moduleName)
        where TOptions : class
    {
        var keys = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Flatten(JsonSerializer.SerializeToNode(options), OptionsKey(moduleName), keys);
        return keys;
    }

    public static TOptions GetModuleOptions<TOptions>(this IConfiguration configuration, string moduleName)
        where TOptions : class, IModuleOptions, new()
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = configuration.GetSection(OptionsKey(moduleName)).Get<TOptions>() ?? new TOptions();
        options.Validate();
        return options;
    }

    // The module list is the array under DigitalBrain:Modules; the per-module Options keys live beside its numeric entries.
    public static string[] SelectedModuleNames(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection("DigitalBrain:Modules").Get<string[]>() ?? [];
    }

    // Copies the bound options onto an instance the options system already created: every public read-write property.
    public static void PopulateModuleOptions<TOptions>(this IConfiguration configuration, string moduleName, TOptions target)
        where TOptions : class, IModuleOptions, new()
    {
        ArgumentNullException.ThrowIfNull(target);
        var bound = configuration.GetModuleOptions<TOptions>(moduleName);
        foreach (var property in typeof(TOptions).GetProperties().Where(p => p.GetMethod is { IsPublic: true } && p.SetMethod is { IsPublic: true }))
        {
            property.SetValue(target, property.GetValue(bound));
        }
    }

    internal static bool IsOptionsKey(string key)
        => key.Split(':') is ["DigitalBrain", "Modules", var name, "Options", ..]
            && name.Length > 0 && !name.Contains("__", StringComparison.Ordinal) && !int.TryParse(name, out _);

    private static void Flatten(JsonNode? node, string prefix, Dictionary<string, string?> into)
    {
        switch (node)
        {
            case JsonObject members:
                foreach (var (name, value) in members) { Flatten(value, $"{prefix}:{name}", into); }
                break;
            case JsonArray items:
                for (var i = 0; i < items.Count; i++) { Flatten(items[i], $"{prefix}:{i}", into); }
                break;
            case JsonValue value:
                into[prefix] = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
                break;
            case null:
                into[prefix] = null;
                break;
        }
    }

    public static string OptionsKey(string moduleName) => $"DigitalBrain:Modules:{moduleName}{OptionsKeySuffix}";
}
