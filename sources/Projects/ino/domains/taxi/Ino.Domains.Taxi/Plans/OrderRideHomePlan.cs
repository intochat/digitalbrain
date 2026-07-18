using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Location.Contracts;
using Ino.Domains.Taxi.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Taxi.Plans;

/// <summary>
/// First multi-hop neuron plan: <c>taxi.ride-home</c>. Walks the neuron
/// graph to resolve "home" + current pickup, then fires
/// <see cref="FindRideRequest"/>.
///
/// BFS:
/// <list type="number">
///   <item>Visit <see cref="ILocationNeuron"/> (user-keyed) with a
///   <see cref="RecallQuery{TEvent}"/> filtering for <c>Label == "home"</c>.
///   The most recent matching entry wins.</item>
///   <item>Visit the same neuron with <c>LastN = 1</c> for the current
///   pickup (the user's last observed place).</item>
///   <item>Fire <see cref="FindRideRequest"/> via the engine, threading the
///   resolved <c>(Pickup, Dropoff)</c> through to <c>RideSearchNeuron</c>.</item>
/// </list>
///
/// When no home anchor exists, the plan returns a friendly message asking
/// the user to set one — recording the user's home is a future slice
/// (cross-domain reactor on a "home is X" utterance).
///
/// Placed naturally on the Taxi silo (the only silo that registers this
/// grain class). The cross-silo hop happens once, on the
/// <see cref="ITraversalEngine.VisitAsync"/> call into Location.
/// </summary>
public sealed class OrderRideHomePlan(
    IFirePort firePort,
    IGrainFactory grainFactory,
    IChatClient chatClient,
    ILogger<OrderRideHomePlan> log) : Grain, IOrderRideHomePlan
{
    public Task<NeuronResult> ExecuteAsync(NeuronPlanContext input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // NeuronContextSurrogate strips FirePort + Logger on the wire — rebuild
        // from the silo-local DI before threading through TraversalEngine.
        var ctx = input.Caller with { FirePort = firePort, Logger = log };
        var key = !string.IsNullOrWhiteSpace(ctx.UserId) ? ctx.UserId : ctx.CorrelationId.Value;

        var engine = new TraversalEngine(grainFactory, firePort, ctx, chatClient);
        return ExecuteAsync(key, engine, log, ct);
    }

    /// <summary>
    /// Pure BFS body, decoupled from the grain shell so tests can drive it
    /// against a <see cref="TraversalEngine"/> backed by the test silo without
    /// activating <see cref="OrderRideHomePlan"/> as an Orleans grain. The grain
    /// surface above just builds the engine + key and forwards.
    /// </summary>
    public static async Task<NeuronResult> ExecuteAsync(
        string userKey,
        ITraversalEngine engine,
        ILogger log,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(log);

        // Step 1: find an explicit "home" anchor in the user's location journal.
        var homeVisits = await engine.VisitAsync<LocationVisited>(
            userKey,
            new RecallQuery<LocationVisited>
            {
                Where = e => string.Equals(e.Label, "home", StringComparison.OrdinalIgnoreCase),
            },
            ct);

        if (homeVisits.Count == 0)
        {
            log.LogInformation(
                "OrderRideHomePlan: no 'home' anchor in user {User}'s location journal — asking",
                userKey);
            return NeuronResult.Ok(
                "I don't know your home address yet. Tell me where home is and I'll remember for next time.");
        }

        // Most recent home anchor wins — supports the user moving and
        // re-anchoring without us reading stale state.
        var home = homeVisits[^1].Payload.Place;

        // Step 2: find current pickup — most recent visit, regardless of label.
        var recent = await engine.VisitAsync<LocationVisited>(
            userKey,
            RecallQuery<LocationVisited>.Last(1),
            ct);
        var pickup = recent.Count > 0 ? recent[^1].Payload.Place : "current location";

        log.LogInformation(
            "OrderRideHomePlan: routing user {User} from {Pickup} to {Home}",
            userKey, pickup, home);

        // Step 3: fire the canonical ride-search synapse with resolved endpoints.
        // RideSearchNeuron narrates and (later) calls the Uber MCP — the plan's
        // job is done once the typed request is on the wire.
        return await engine.FireAsync(new FindRideRequest(pickup, home), ct);
    }
}
