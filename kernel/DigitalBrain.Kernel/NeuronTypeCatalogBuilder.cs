using DigitalBrain;

namespace DigitalBrain.Kernel;

public static class NeuronTypeCatalogBuilder
{
    public static IReadOnlyList<NeuronRegistration> Build(IEnumerable<Type> types)
    {
        var typeList = types.Distinct().ToList();
        var candidates = typeList
            .Where(IsCandidateContract)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        var leaves = candidates
            .Where(candidate => candidates.All(other =>
                other == candidate || !candidate.IsAssignableFrom(other)))
            .ToList();

        var registrations = new List<NeuronRegistration>();
        foreach (var leaf in leaves)
        {
            if (leaf.IsGenericTypeDefinition || leaf.IsGenericType)
            {
                throw new InvalidOperationException(
                    $"Generic leaf neuron contract '{leaf.FullName}' is not supported.");
            }

            var implementors = typeList
                .Where(type => type is { IsClass: true, IsAbstract: false }
                    && leaf.IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList();

            if (implementors.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Missing neuron implementation for contract '{leaf.FullName}'.");
            }

            var nonNeuron = implementors.FirstOrDefault(type => !typeof(Neuron).IsAssignableFrom(type));
            if (nonNeuron is not null)
            {
                throw new InvalidOperationException(
                    $"Neuron implementation '{nonNeuron.FullName}' for contract '{leaf.FullName}' does not derive from {nameof(Neuron)}.");
            }

            if (implementors.Count > 1)
            {
                var names = string.Join(
                    ", ",
                    implementors.Select(type => $"'{type.FullName}'"));
                throw new InvalidOperationException(
                    $"Duplicate neuron implementations for contract '{leaf.FullName}': {names}.");
            }

            registrations.Add(new NeuronRegistration(leaf, implementors[0]));
        }

        return registrations
            .OrderBy(registration => registration.Contract.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsCandidateContract(Type type) =>
        type.IsInterface
        && (type.IsPublic || type.IsNestedPublic)
        && type != typeof(INeuron)
        && typeof(INeuron).IsAssignableFrom(type);
}
