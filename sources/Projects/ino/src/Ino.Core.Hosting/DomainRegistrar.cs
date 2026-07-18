using Ino.Core;

namespace Ino.Core.Hosting;

public static class DomainRegistrar
{
    public static SiloRegistration Build(RegistrationOptions options)
    {
        var canonicals = new List<CanonicalRegistration>();
        var reactives = new List<ReactiveRegistration>();

        foreach (var domain in options.Domains)
        {
            var assembly = domain.GetType().Assembly;
            foreach (var grainType in assembly.GetTypes())
            {
                if (grainType.IsAbstract || grainType.IsInterface) continue;

                var canonicalInterfaces = grainType.GetInterfaces()
                    .Where(IsGenericInterface(typeof(INeuron<>)))
                    .ToArray();

                foreach (var iface in canonicalInterfaces)
                {
                    var synapseType = iface.GetGenericArguments()[0];
                    var requiredCaps = domain.PerGrainCapabilities.TryGetValue(grainType, out var c)
                        ? c
                        : Array.Empty<Capability>();
                    canonicals.Add(new CanonicalRegistration(synapseType, grainType, domain.Id, requiredCaps));
                }

                var reactiveInterfaces = grainType.GetInterfaces()
                    .Where(IsGenericInterface(typeof(IReactsTo<>)))
                    .ToArray();

                foreach (var iface in reactiveInterfaces)
                {
                    var synapseType = iface.GetGenericArguments()[0];
                    reactives.Add(new ReactiveRegistration(synapseType, grainType, domain.Id));
                }
            }
        }

        foreach (var grainType in options.BuiltInGrainTypes)
        {
            var canonicalInterfaces = grainType.GetInterfaces()
                .Where(IsGenericInterface(typeof(INeuron<>)))
                .ToArray();

            foreach (var iface in canonicalInterfaces)
            {
                var synapseType = iface.GetGenericArguments()[0];
                canonicals.Add(new CanonicalRegistration(
                    synapseType, grainType, options.BuiltInDomainId, Array.Empty<Capability>()));
            }

            var reactiveInterfaces = grainType.GetInterfaces()
                .Where(IsGenericInterface(typeof(IReactsTo<>)))
                .ToArray();

            foreach (var iface in reactiveInterfaces)
            {
                var synapseType = iface.GetGenericArguments()[0];
                reactives.Add(new ReactiveRegistration(
                    synapseType, grainType, options.BuiltInDomainId));
            }
        }

        var neurons = options.Domains
            .SelectMany(d => d.DeclaredNeurons)
            .ToArray();

        return new SiloRegistration(options.Silo, canonicals, reactives, neurons);
    }

    private static Func<Type, bool> IsGenericInterface(Type openGeneric) =>
        t => t.IsGenericType && t.GetGenericTypeDefinition() == openGeneric;
}
