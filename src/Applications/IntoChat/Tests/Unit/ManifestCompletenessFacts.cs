using System.Reflection;
using DigitalBrain.Core;

namespace IntoChat.Tests;

public sealed class ManifestCompletenessFacts
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
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                if (typeof(IModule).IsAssignableFrom(type))
                {
                    yield return type;
                }
            }
        }
    }

    [Fact]
    public void EveryProductModuleDeclaresAConfigurationContract()
    {
        var modules = ProductModules().OrderBy(module => module.FullName, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(modules);

        var missing = modules
            .Where(module => module.GetCustomAttribute<ModuleConfigurationAttribute>() is null)
            .Select(module => module.FullName)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryConfigurationContractBelongsToItsDeclaringModule()
    {
        foreach (var module in ProductModules())
        {
            var attribute = module.GetCustomAttribute<ModuleConfigurationAttribute>();
            Assert.NotNull(attribute);

            var contract = Assert.IsAssignableFrom<IModuleConfigurationContract>(
                Activator.CreateInstance(attribute!.ContractType));
            Assert.Equal(module, contract.ModuleType);
            Assert.Equal(contract.OptionsType, contract.CreateDefaults().GetType());
        }
    }
}