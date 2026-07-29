using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

internal static class ModelContracts
{
    private static readonly Dictionary<string, Type> ContractsByModelName = Discover();

    internal static Type Resolve(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return ContractsByModelName.TryGetValue(model.Trim(), out var contract)
            ? contract
            : throw new InvalidOperationException(
                $"Model '{model}' is not a model this build knows. Known models: {string.Join(", ", KnownModelNames())}. Knowing a model is not the same as it being provisioned here: a known model still fails on its first turn if this deployment has no endpoint or key configured for it.");
    }

    internal static string ModelNameOf(Type contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return contract.Name[1..];
    }

    internal static string LabelFor(NeuronId participant)
        => ContractsByModelName.TryGetValue(participant.Type, out var contract)
            ? $"{ModelNameOf(contract)} '{participant.Name}'"
            : $"{participant.Type} '{participant.Name}'";

    internal static IReadOnlyList<string> KnownModelNames()
        => [.. ContractsByModelName.Keys.Order(StringComparer.Ordinal)];

    private static Dictionary<string, Type> Discover()
    {
        var contracts = typeof(ILLM).Assembly.GetExportedTypes();
        Dictionary<string, Type> byModelName = new(StringComparer.OrdinalIgnoreCase);

        foreach (var model in typeof(LLM).Assembly
            .GetTypes()
            .Where(candidate => candidate is { IsClass: true, IsAbstract: false }
                && candidate.IsSubclassOf(typeof(LLM))))
        {
            var contract = ContractOf(model, contracts);

            if (byModelName.TryGetValue(model.Name, out var claimed))
            {
                throw new InvalidOperationException(
                    $"Model name '{model.Name}' is claimed by both '{claimed.FullName}' and '{contract.FullName}'; a model name must select exactly one model.");
            }

            byModelName.Add(model.Name, contract);
        }

        return byModelName;
    }

    private static Type ContractOf(Type model, IReadOnlyList<Type> contracts)
        => contracts.SingleOrDefault(candidate =>
            candidate.IsInterface
            && candidate.Namespace == model.Namespace
            && string.Equals(candidate.Name, $"I{model.Name}", StringComparison.Ordinal)
            && typeof(ILLM).IsAssignableFrom(candidate))
        ?? throw new InvalidOperationException(
            $"Model '{model.FullName}' has no 'I{model.Name}' contract extending {nameof(ILLM)} in namespace '{model.Namespace}'.");
}
