using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Core;

public static class ModuleOptionsSerialization
{
    public const string OptionsKeySuffix = ":Options";

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
        return new ModuleDefinition(typeof(TModule), new Dictionary<string, string?>
        {
            [OptionsKey(typeof(TModule).Name)] = JsonSerializer.Serialize(options),
        });
    }

    public static TOptions GetModuleOptions<TOptions>(this IConfiguration configuration, string moduleName)
        where TOptions : class, IModuleOptions, new()
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var json = configuration[OptionsKey(moduleName)];
        var options = string.IsNullOrWhiteSpace(json) ? new TOptions() : Deserialize<TOptions>(json, moduleName);
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
    {
        var segments = key.Split(':');
        return segments is ["DigitalBrain", "Modules", var name, "Options"]
            && name.Length > 0 && !name.Contains("__", StringComparison.Ordinal) && !int.TryParse(name, out _)
            && string.Equals(segments[0], "DigitalBrain", StringComparison.OrdinalIgnoreCase);
    }

    private static TOptions Deserialize<TOptions>(string json, string moduleName) where TOptions : class
    {
        try
        {
            return JsonSerializer.Deserialize<TOptions>(json)
                ?? throw new InvalidOperationException($"Options for module {moduleName} are JSON null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Options for module {moduleName} are not valid JSON.", exception);
        }
    }

    public static string OptionsKey(string moduleName) => $"DigitalBrain:Modules:{moduleName}{OptionsKeySuffix}";
}
