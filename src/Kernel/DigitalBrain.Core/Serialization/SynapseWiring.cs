using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

internal static class SynapseWiring
{
    private static readonly ConcurrentDictionary<Assembly, DispatchManifest> Manifests = new();

    internal static bool TryGetManifest(Assembly assembly, out DispatchManifest manifest)
    {
        manifest = Manifests.GetOrAdd(assembly, static probed => Discover(probed));
        return true;
    }

    private static DispatchManifest Discover(Assembly assembly)
    {
        var handleOpen = typeof(IHandle<>);
        var handlers = new List<SynapseWiringEntry>();

        foreach (var type in SafeGetTypes(assembly))
        {
            if (type is not { IsClass: true, IsAbstract: false })
            {
                continue;
            }

            var neuronName = type.FullName;
            if (neuronName is null)
            {
                continue;
            }

            foreach (var contract in type.GetInterfaces())
            {
                if (!contract.IsGenericType
                    || contract.GetGenericTypeDefinition() != handleOpen)
                {
                    continue;
                }

                var synapseName = contract.GenericTypeArguments[0].FullName;
                if (synapseName is null)
                {
                    continue;
                }

                handlers.Add(new SynapseWiringEntry(neuronName, synapseName));
            }
        }

        return new DispatchManifest(handlers);
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
