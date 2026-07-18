using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Taxi.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Taxi.Plans;

/// <summary>
/// One-hop plan for the <c>taxi.find-ride</c> neuron. Preserves the legacy
/// switch behaviour: the prompt is the Pickup, Dropoff is empty until the
/// integration target (Uber MCP) supplies structured slots. The richer
/// <c>taxi.ride-home</c> neuron uses <see cref="OrderRideHomePlan"/>.
/// </summary>
public sealed class FindRidePlan(
    IFirePort firePort,
    IGrainFactory grainFactory,
    IChatClient chatClient,
    ILogger<FindRidePlan> log) : Grain, IFindRidePlan
{
    public Task<NeuronResult> ExecuteAsync(NeuronPlanContext input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var ctx = input.Caller with { FirePort = firePort, Logger = log };
        var engine = new TraversalEngine(grainFactory, firePort, ctx, chatClient);
        return ExecuteAsync(input.Prompt, engine, ct);
    }

    public static Task<NeuronResult> ExecuteAsync(
        string prompt,
        ITraversalEngine engine,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.FireAsync(new FindRideRequest(prompt, string.Empty), ct);
    }
}
