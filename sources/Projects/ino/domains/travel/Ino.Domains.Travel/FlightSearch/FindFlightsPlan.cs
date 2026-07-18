using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Travel.Contracts;
using Ino.Domains.Travel.FlightSearch.Rfw;
using Ino.Domains.Travel.Rfw;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Travel.FlightSearch;

/// <summary>
/// One-hop plan for the <c>travel.find-flights</c> neuron. Returns three
/// mock flight options as an RFW <c>FlightCard</c> column. The real
/// tripradar &harr; Travel structured-data flow is a follow-up slice.
///
/// Body extracted as a <see langword="static"/> method for unit-testability,
/// matching the <c>OrderRideHomePlan</c> shape from Phase 3 Slice B.
/// </summary>
public sealed class FindFlightsPlan(
    IFirePort firePort,
    IGrainFactory grainFactory,
    IChatClient chatClient,
    ILogger<FindFlightsPlan> log) : Grain, IFindFlightsPlan
{
    public Task<NeuronResult> ExecuteAsync(NeuronPlanContext input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var ctx = input.Caller with { FirePort = firePort, Logger = log };
        var engine = new TraversalEngine(grainFactory, firePort, ctx, chatClient);
        return ExecuteAsync(input.Prompt, engine, ct);
    }

    /// <summary>
    /// Pure plan body. Tests drive it directly. The <see cref="ITraversalEngine"/>
    /// parameter is retained so a follow-up slice can swap the mock corpus for a
    /// real <c>FindFlightsRequest</c> fire without changing the dispatch shape.
    /// </summary>
    public static Task<NeuronResult> ExecuteAsync(
        string prompt,
        ITraversalEngine engine,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var flights = MockFlightCorpus.For(prompt);
        var rfw = FlightCardListBuilder.Build(flights);
        var summary = $"I found {flights.Count} flights for you.";
        return Task.FromResult(NeuronResult.Ok(summary).WithRfwPayload(rfw));
    }
}
