using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Core;
using Orleans.Journaling.Json;

namespace DigitalBrain.Kernel.Kernel;

public static class JournalJson
{
    public static void Configure(JsonJournalOptions options) =>
        options.AddTypeInfoResolver(CreateTypeInfoResolver());

    public static IJsonTypeInfoResolver CreateTypeInfoResolver()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type != typeof(Synapse))
                return;

            var polymorphism = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$synapseType",
                IgnoreUnrecognizedTypeDiscriminators = false,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
            };

            foreach (var synapseType in DiscoverSynapseTypes())
            {
                polymorphism.DerivedTypes.Add(new JsonDerivedType(synapseType, synapseType.FullName!));
            }

            typeInfo.PolymorphismOptions = polymorphism;
        });
        return resolver;
    }

    private static IReadOnlyList<Type> DiscoverSynapseTypes()
    {
        LoadReferencedDigitalBrainAssemblies(typeof(JournalJson).Assembly);

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("DigitalBrain.", StringComparison.Ordinal) == true)
            .SelectMany(GetLoadableTypes)
            .Where(type => type != typeof(Synapse)
                && !type.IsAbstract
                && !type.ContainsGenericParameters
                && typeof(Synapse).IsAssignableFrom(type)
                && type.FullName is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static void LoadReferencedDigitalBrainAssemblies(Assembly root)
    {
        foreach (var name in root.GetReferencedAssemblies())
        {
            if (name.Name?.StartsWith("DigitalBrain.", StringComparison.Ordinal) != true)
                continue;

            try
            {
                Assembly.Load(name);
            }
            catch
            {
                // Optional integrations can be absent in focused test hosts; loaded assemblies still contribute metadata.
            }
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }
}
