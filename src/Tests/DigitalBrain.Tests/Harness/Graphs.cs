using DigitalBrain.Abstractions;
using DigitalBrain.Client;

namespace DigitalBrain.Tests.Harness;

internal static class Graphs
{
    private static readonly TimeSpan DefaultPatience = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    internal static Task<IReadOnlyCollection<SynapseRoute>> RoutesAsync(
        IDigitalBrain brain,
        NeuronId source,
        string synapseAlias)
        => brain.GetGrainProxy<ISynapseGraph>(ISynapseGraph.InstanceName).RoutesFor(source, synapseAlias);

    internal static async Task<IReadOnlyCollection<SynapseRoute>> WaitForRoutesAsync(
        IDigitalBrain brain,
        NeuronId source,
        string synapseAlias,
        int routeCount = 1,
        TimeSpan? patience = null)
    {
        var deadline = DateTime.UtcNow + (patience ?? DefaultPatience);

        while (DateTime.UtcNow < deadline)
        {
            var routes = await RoutesAsync(brain, source, synapseAlias);
            if (routes.Count >= routeCount)
            {
                return routes;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"The graph reported fewer than {routeCount} route(s) for ({source}, {synapseAlias}) within {patience ?? DefaultPatience}.");
    }

    internal static async Task WaitForRouteTargetAsync(
        IDigitalBrain brain,
        NeuronId source,
        string synapseAlias,
        NeuronId target,
        TimeSpan? patience = null)
    {
        var deadline = DateTime.UtcNow + (patience ?? DefaultPatience);

        while (DateTime.UtcNow < deadline)
        {
            var routes = await RoutesAsync(brain, source, synapseAlias);
            if (routes.Any(route => route.Target == target))
            {
                return;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"The graph never routed ({source}, {synapseAlias}) at {target} within {patience ?? DefaultPatience}.");
    }

    internal static async Task WaitForNoRoutesAsync(
        IDigitalBrain brain,
        NeuronId source,
        string synapseAlias,
        TimeSpan? patience = null)
    {
        var deadline = DateTime.UtcNow + (patience ?? DefaultPatience);

        while (DateTime.UtcNow < deadline)
        {
            var routes = await RoutesAsync(brain, source, synapseAlias);
            if (routes.Count == 0)
            {
                return;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"The graph kept reporting routes for ({source}, {synapseAlias}) within {patience ?? DefaultPatience}.");
    }
}
