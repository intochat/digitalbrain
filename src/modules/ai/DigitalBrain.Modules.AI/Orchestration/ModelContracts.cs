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
                $"Model '{model}' is not one of this brain's models. The models it can run are {string.Join(", ", AvailableModelNames())}.");
    }

    internal static string ModelNameOf(Type contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return contract.Name[1..];
    }

    private static IEnumerable<string> AvailableModelNames()
        => ContractsByModelName.Keys.Order(StringComparer.Ordinal);

    private static Dictionary<string, Type> Discover()
    {
        var contracts = typeof(ILLM).Assembly.GetExportedTypes();

        return typeof(LLM).Assembly
            .GetTypes()
            .Where(model => model is { IsClass: true, IsAbstract: false } && model.IsSubclassOf(typeof(LLM)))
            .ToDictionary(
                model => model.Name,
                model => ContractOf(model, contracts),
                StringComparer.OrdinalIgnoreCase);
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
