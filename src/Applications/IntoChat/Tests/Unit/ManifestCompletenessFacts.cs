using System.Reflection;
using DigitalBrain.Apps;
using DigitalBrain.Apps.Manifests;
using DigitalBrain.Contracts;
using DigitalBrain.Core;

namespace IntoChat.Tests;

public sealed class ManifestCompletenessFacts
{
    // Modules with no app-facing neuron contract: the identity directory is a plain string-keyed
    // grain, the broker gateway and the Aspire host have no INeuron surface of their own.
    private static readonly HashSet<string> InfrastructureModules =
        ["DigitalBrain.Modules.Identity", "DigitalBrain.Modules.Broker", "DigitalBrain.Modules.Microsoft.Aspire"];

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

    [Fact]
    public void EveryProductModuleEmitsAGeneratedManifest()
    {
        var uncovered = new List<string>();
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in ProductModules())
        {
            var assemblyName = module.Assembly.GetName().Name!;
            if (InfrastructureModules.Contains(assemblyName)) { continue; }

            var manifests = GeneratedManifests(module.Assembly).ToList();
            if (manifests.Count == 0)
            {
                uncovered.Add(assemblyName);
                continue;
            }

            Assert.All(manifests, ManifestValidator.Validate);
            covered.Add(assemblyName);
        }

        Assert.Empty(uncovered);
        foreach (var expected in new[]
        {
            "DigitalBrain.Modules.AI", "DigitalBrain.Modules.Apps", "DigitalBrain.Modules.Compute",
            "DigitalBrain.Modules.Connections", "DigitalBrain.Modules.Discovery", "DigitalBrain.Modules.Flutter",
            "DigitalBrain.Modules.MyData", "DigitalBrain.Modules.Receipts", "DigitalBrain.Modules.Supabase",
            "DigitalBrain.Modules.Time",
        })
        {
            Assert.Contains(expected, covered);
        }
    }

    // A module emits a manifest for every neuron interface in its companion contracts assembly.
    private static IEnumerable<AppManifest> GeneratedManifests(Assembly module)
    {
        var contracts = ContractsAssembly(module);
        if (contracts is null) { yield break; }

        foreach (var type in contracts.GetTypes()
            .Where(type => type.IsInterface && type != typeof(INeuron) && typeof(INeuron).IsAssignableFrom(type)))
        {
            yield return ManifestGenerator.FromInterface(type, new AppManifestSeed
            {
                Id = "generated." + type.Name.ToLowerInvariant(),
                Version = "1.0.0",
                Publisher = "intochat",
                Kind = AppKind.Declarative,
                DescriptionForPeople = type.Name,
                DescriptionForModel = type.Name,
            });
        }
    }

    private static Assembly? ContractsAssembly(Assembly module)
    {
        var path = Path.Combine(AppContext.BaseDirectory, module.GetName().Name + ".Contracts.dll");
        return File.Exists(path) ? Assembly.Load(AssemblyName.GetAssemblyName(path)) : null;
    }
}