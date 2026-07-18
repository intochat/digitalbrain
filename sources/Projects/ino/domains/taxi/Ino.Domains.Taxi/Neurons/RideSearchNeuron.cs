using System.Diagnostics;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Taxi.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Taxi.Neurons;

/// <summary>
/// Scaffold neuron for the Taxi neuron. Returns an LLM-narrated
/// acknowledgement so the Taxi bundle is discoverable through
/// <c>IDiscovery</c> and the end-to-end install / route / fire pipeline
/// exercises more than one neuron.
///
/// A later slice replaces this with a real Uber MCP tool call backed by
/// the user's Google OAuth token, or a mocked provider if no real MCP
/// server is available at integration time. The LLM narrative wrapper
/// stays — only the structured fan-out body changes.
/// </summary>
public sealed class RideSearchNeuron(
    IChatClientFactory chatFactory,
    ILogger<RideSearchNeuron> log) : Grain, INeuron<FindRideRequest>
{
    static readonly ActivitySource ActivitySource = new("ino");

    public async Task<NeuronResult> HandleAsync(
        FindRideRequest synapse,
        NeuronContext ctx,
        CancellationToken ct)
    {
        using var span = ActivitySource.StartActivity("ino.neuron.handle", ActivityKind.Internal);
        span?.SetTag("ino.neuron.type", nameof(RideSearchNeuron));
        span?.SetTag("ino.synapse.type", nameof(FindRideRequest));
        span?.SetTag("ino.correlation_id", ctx.CorrelationId.Value);

        var summary = await NarrateAsync(synapse, ct);

        span?.SetStatus(ActivityStatusCode.Ok);
        return NeuronResult.Ok(summary);
    }

    async Task<string> NarrateAsync(FindRideRequest synapse, CancellationToken ct)
    {
        var fallback = string.IsNullOrWhiteSpace(synapse.Dropoff)
            ? $"Taxi scaffold: would search rides for '{synapse.Pickup}'."
            : $"Taxi scaffold: would search rides from '{synapse.Pickup}' to '{synapse.Dropoff}'.";

        var system =
            "You are a concise ride-hailing assistant. The user wants a taxi or ride-share. " +
            "Reply in ONE short sentence acknowledging the request and noting that v0.1 is a " +
            "scaffold (no real provider integration yet). If the user mentioned a destination, " +
            "echo it; otherwise ask where they want to go.";

        try
        {
            var chat = chatFactory.ForTier(LlmTier.Balanced);
            var response = await chat.GetResponseAsync(
                new[]
                {
                    new ChatMessage(ChatRole.System, system),
                    new ChatMessage(ChatRole.User, synapse.Pickup),
                },
                options: null,
                ct);
            return string.IsNullOrWhiteSpace(response.Text) ? fallback : response.Text.Trim();
        }
        catch (Exception ex) when (ex is BddMockMissException or NotSupportedException)
        {
            log.LogDebug("RideSearch narrative skipped: {Reason}", ex.GetType().Name);
            return fallback;
        }
    }
}
