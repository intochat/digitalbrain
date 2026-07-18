using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Travel.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Travel.HotelSearch;

/// <summary>
/// One-hop plan for the <c>travel.find-hotels</c> neuron. Forwards the
/// user prompt to <see cref="FindHotelsRequest"/>. See
/// FlightSearch.FindFlightsPlan for the pattern.
/// </summary>
public sealed class FindHotelsPlan(
    IFirePort firePort,
    IGrainFactory grainFactory,
    IChatClient chatClient,
    ILogger<FindHotelsPlan> log) : Grain, IFindHotelsPlan
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
        return engine.FireAsync(new FindHotelsRequest(prompt), ct);
    }
}
