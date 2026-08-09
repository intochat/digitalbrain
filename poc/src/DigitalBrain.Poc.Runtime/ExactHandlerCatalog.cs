using DigitalBrain.Poc.Abstractions;

namespace DigitalBrain.Poc.Runtime;

public sealed class ExactHandlerCatalog
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ExactHandler>> _byAlias;

    private ExactHandlerCatalog(IReadOnlyDictionary<string, IReadOnlyList<ExactHandler>> byAlias)
    {
        _byAlias = byAlias;
    }

    public static ExactHandlerCatalog Create(IEnumerable<Type> neuronTypes)
    {
        ArgumentNullException.ThrowIfNull(neuronTypes);
        var handlers = new List<ExactHandler>();
        foreach (var neuronType in neuronTypes.Distinct())
        {
            if (!typeof(Neuron).IsAssignableFrom(neuronType) || neuronType.IsAbstract)
            {
                continue;
            }

            foreach (var contract in neuronType.GetInterfaces()
                .Where(candidate =>
                    candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() == typeof(IHandle<>)))
            {
                var inputType = contract.GetGenericArguments()[0];
                if (inputType == typeof(Synapse))
                {
                    throw new InvalidOperationException(
                        $"Neuron '{neuronType.FullName}' handles the Synapse base type; only exact contracts are allowed.");
                }

                handlers.Add(new ExactHandler(
                    ContractAlias.For(inputType),
                    inputType,
                    neuronType,
                    contract));
            }
        }

        var byAlias = handlers
            .GroupBy(handler => handler.ContractAlias, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ExactHandler>)group.ToArray(),
                StringComparer.Ordinal);
        return new ExactHandlerCatalog(byAlias);
    }

    public IReadOnlyList<ExactHandler> Resolve(string contractAlias) =>
        _byAlias.TryGetValue(contractAlias, out var handlers)
            ? handlers
            : throw new UnknownSynapseAliasException(contractAlias);

    internal IReadOnlyList<ExactHandler> All => _byAlias.Values.SelectMany(value => value).ToArray();
}
