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
        var options = string.IsNullOrWhiteSpace(json)
            ? new TOptions()
            : JsonSerializer.Deserialize<TOptions>(json) ?? new TOptions();
        options.Validate();
        return options;
    }

    internal static bool IsOptionsKey(string key) =>
        key.StartsWith("DigitalBrain:Modules:", StringComparison.OrdinalIgnoreCase)
        && key.EndsWith(OptionsKeySuffix, StringComparison.OrdinalIgnoreCase);

    private static string OptionsKey(string moduleName) => $"DigitalBrain:Modules:{moduleName}{OptionsKeySuffix}";
}
