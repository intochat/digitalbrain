using System.Reflection;
using System.Text.Json;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;

namespace IntoChat.Tests;

public sealed class ModuleOptionsFacts
{
    private static IEnumerable<Type> ProductModules()
    {
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "DigitalBrain.Modules.*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.EndsWith(".Contracts", StringComparison.Ordinal)
                || name.EndsWith(".Testing", StringComparison.Ordinal)
                || name.Contains(".Tests.", StringComparison.Ordinal))
            {
                continue;
            }

            var assembly = Assembly.Load(AssemblyName.GetAssemblyName(file));
            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsAbstract && !type.IsInterface && type.GetConstructor(Type.EmptyTypes) is not null && typeof(IModule).IsAssignableFrom(type))
                {
                    yield return type;
                }
            }
        }
    }

    private static Type? OptionsTypeOf(Type module) => module.GetInterfaces()
        .SingleOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IModule<>))?.GetGenericArguments()[0];

    public static IEnumerable<TheoryDataRow<Type>> ModulesWithOptions()
        => ProductModules().Where(module => OptionsTypeOf(module) is not null)
            .OrderBy(module => module.FullName, StringComparer.Ordinal).Select(module => new TheoryDataRow<Type>(module));

    [Fact]
    public void TheProductCompositionIncludesTheOptionsModules()
        => Assert.True(ModulesWithOptions().Count() >= 10, "Every module that has topology knobs declares IModule<TOptions>.");

    [Theory]
    [MemberData(nameof(ModulesWithOptions))]
    public void DefaultOptionsRoundTripThroughTheOneJsonConfigurationValue(Type module)
    {
        var optionsType = OptionsTypeOf(module)!;
        var original = Activator.CreateInstance(optionsType)!;
        var compile = typeof(ModuleOptionsSerialization).GetMethod(nameof(ModuleOptionsSerialization.Compile))!.MakeGenericMethod(module, optionsType);
        var definition = (ModuleDefinition)compile.Invoke(null, [original])!;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(definition.Configuration).Build();
        var bind = typeof(ModuleOptionsSerialization).GetMethod(nameof(ModuleOptionsSerialization.GetModuleOptions))!.MakeGenericMethod(optionsType);
        var bound = bind.Invoke(null, [configuration, module.Name])!;

        Assert.Equal(JsonSerializer.Serialize(original, optionsType), JsonSerializer.Serialize(bound, optionsType));
        ModuleSettingsValidation.ValidatePublicSettings([definition]);
    }

    [Fact]
    public void NoOptionsTypeCarriesAJsonIgnoredWritableMember()
    {
        var offenders = ModulesWithOptions().Select(row => OptionsTypeOf(row.Data)!)
            .SelectMany(options => options.GetProperties()
                .Where(property => property.SetMethod is { IsPublic: true } && property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() is not null)
                .Select(property => $"{options.Name}.{property.Name}"))
            .ToList();

        Assert.Empty(offenders);
    }
}
