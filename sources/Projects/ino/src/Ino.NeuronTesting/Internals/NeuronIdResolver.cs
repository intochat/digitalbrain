using System.Reflection;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.NeuronTesting.Attributes;

namespace Ino.NeuronTesting.Internals;

public static class NeuronIdResolver
{
    public static NeuronId Resolve(Type neuronType)
    {
        var attr = neuronType.GetCustomAttribute<NeuronIdAttribute>();
        if (attr is not null) return NeuronId.From(attr.Value);

        // Walk loaded assemblies to find any IDomain whose DeclaredNeurons
        // contains an entry matching by PlanType or CanonicalSynapseType.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); }

            foreach (var t in types.Where(t => !t.IsAbstract && typeof(IDomain).IsAssignableFrom(t)))
            {
                IDomain? domain = null;
                try { domain = (IDomain?)Activator.CreateInstance(t); }
                catch (MissingMethodException) { /* no parameterless ctor — domain is DI-only, skip */ }
                if (domain is null) continue;

                foreach (var n in domain.DeclaredNeurons)
                {
                    if (n.PlanType is not null && n.PlanType.IsAssignableFrom(neuronType))
                        return n.Id;

                    // Match by synapse: neuronType implements INeuron<CanonicalSynapseType>
                    if (n.CanonicalSynapseType is not null &&
                        neuronType.GetInterfaces().Any(i =>
                            i.IsGenericType &&
                            i.GetGenericTypeDefinition() == typeof(INeuron<>) &&
                            i.GetGenericArguments()[0] == n.CanonicalSynapseType))
                        return n.Id;
                }
            }
        }

        throw new InvalidOperationException(
            $"{neuronType.FullName} has no NeuronId: " +
            $"add [NeuronId(\"domain.verb\")] or register the type in an IDomain.DeclaredNeurons.");
    }
}
