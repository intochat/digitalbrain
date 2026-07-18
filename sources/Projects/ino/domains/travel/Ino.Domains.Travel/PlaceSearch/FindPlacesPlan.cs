using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Travel.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Travel.PlaceSearch;

/// <summary>
/// One-hop plan for the <c>travel.find-places</c> neuron. Forwards the
/// user prompt to <see cref="FindPlacesRequest"/>.
/// </summary>
public sealed class FindPlacesPlan(
    IFirePort firePort,
    IGrainFactory grainFactory,
    IChatClient chatClient,
    ILogger<FindPlacesPlan> log) : Grain, IFindPlacesPlan
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
        return engine.FireAsync(new FindPlacesRequest(prompt), ct);
    }
}
