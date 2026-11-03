using System.Reflection;
using DigitalBrain.Modules.Configuration;
using DigitalBrain.Modules.Validation;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Modules.Options;

public static class ModuleOptionsSerialization
{
    public const string OptionsKeySuffix = ":Options";

    // Options are ordinary configuration: every property flattens to a key under
    // {Root}:Modules:{Name}:Options, so a host's own configuration (env, args, files)
    // overrides a code-declared default by standard precedence — no side channel.
    public static Dictionary<string, string?> FlattenOptions<TOptions>(TOptions options, string moduleName)
        where TOptions : class
        => ModuleConfiguration.Flatten(options!, OptionsKey(moduleName));

    public static TOptions GetModuleOptions<TOptions>(this IConfiguration configuration, string moduleName)
        where TOptions : class, IModuleOptions, new()
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = configuration.GetSection(OptionsKey(moduleName)).Get<TOptions>() ?? new TOptions();
        Validate(options, moduleName);
        return options;
    }

    public static void Validate<TOptions>(TOptions options, string moduleName) where TOptions : class, IModuleOptions
    {
        ValidateRequired(options, moduleName);
        try { options.Validate(); }
        catch (ModuleValidationException failure)
        {
            var prefix = OptionsKey(moduleName);
            throw new ModuleValidationException([.. failure.Issues.Select(issue => issue with
            {
                Path = issue.Path.Length == 0 ? prefix
                    : issue.Path.Equals(prefix, StringComparison.Ordinal) || issue.Path.StartsWith(prefix + ":", StringComparison.Ordinal)
                        ? issue.Path : prefix + ":" + issue.Path,
            })]);
        }
    }

    // Eager, at bind time: a [Setting(Required = true)] member that is null, default or blank fails
    // before Validate(). The message names the configuration key, never a value.
    public static void ValidateRequired<TOptions>(TOptions options, string moduleName) where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(options);
        List<ValidationIssue> issues = [];
        foreach (var property in typeof(TOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<SettingAttribute>() is not { Required: true }) { continue; }
            var value = property.GetValue(options);
            var missing = value is null
                || value is string text && string.IsNullOrWhiteSpace(text)
                || property.PropertyType.IsValueType && value.Equals(Activator.CreateInstance(property.PropertyType));
            if (missing)
            {
                issues.Add(new($"{OptionsKey(moduleName)}:{property.Name}", "required", "A value is required."));
            }
        }
        if (issues.Count != 0) { throw new ModuleValidationException([.. issues]); }
    }

    public static string OptionsKey(string moduleName) => ModuleConfiguration.ModuleOptionsSection(moduleName);
}
