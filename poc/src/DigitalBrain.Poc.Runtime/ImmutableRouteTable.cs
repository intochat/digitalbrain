using DigitalBrain.Poc.Abstractions;

namespace DigitalBrain.Poc.Runtime;

public sealed class ImmutableRouteTable
{
    private readonly RouteBinding[] _routes;

    public ImmutableRouteTable(IEnumerable<RouteBinding> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _routes = routes
            .OrderBy(route => route.OwnerId, StringComparer.Ordinal)
            .ThenBy(route => route.ContractAlias, StringComparer.Ordinal)
            .ThenBy(route => route.CandidateFamily?.Value, StringComparer.Ordinal)
            .ThenBy(route => route.TargetRevision, StringComparer.Ordinal)
            .ThenBy(route => route.NeuronType, StringComparer.Ordinal)
            .ToArray();
        if (_routes.Select(route => route.Key).Distinct(StringComparer.Ordinal).Count() != _routes.Length)
        {
            throw new InvalidOperationException("The immutable route table contains a duplicate activation key.");
        }
    }

    public IReadOnlyList<SynapseEnvelope> ExpandTrustedInput(
        string ownerId,
        string inputReceiptId,
        Synapse input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputReceiptId);
        ArgumentNullException.ThrowIfNull(input);
        var alias = ContractAlias.For(input.GetType());
        return _routes
            .Where(route =>
                route.OwnerId.Equals(ownerId, StringComparison.Ordinal) &&
                route.ContractAlias.Equals(alias, StringComparison.Ordinal))
            .Select((route, ordinal) => SynapseEnvelope.Trusted(
                ownerId,
                inputReceiptId,
                input,
                route,
                ordinal))
            .ToArray();
    }

    internal IReadOnlyList<RouteBinding> Routes => _routes;
}
