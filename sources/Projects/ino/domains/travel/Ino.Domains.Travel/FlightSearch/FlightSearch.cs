using System.Diagnostics;
using System.Text;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Travel.Contracts;
using Ino.Domains.Travel.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Travel.FlightSearch;

/// <summary>
/// Returns a destination-keyed seed flight list and asks the Balanced-tier
/// LLM for a one-line user-facing summary grounded in those flights. Under
/// <c>INO_TEST_MODE</c> the BDD mock matches on the user's original query
/// and returns the canned reply from the matching <c>travel-intent.feature</c>
/// scenario; in production the configured xAI factory grounds the summary
/// in the actual airlines and prices. LLM failures fall back to an f-string
/// so the demo never breaks if the model is unavailable.
/// </summary>
public sealed class FlightSearch(
    IChatClientFactory chatFactory,
    ILogger<FlightSearch> log) : Grain, INeuron<FindFlightsRequest>
{
    /// <summary>
    /// Neuron-side ActivitySource — name <c>"ino"</c> matches
    /// <c>Ino.ServiceDefaults.AddSource("ino")</c>. Span name
    /// <c>ino.neuron.handle</c> sits as the deepest link of the four-span
    /// chain: <c>flutter.chat → Ino/Chat → ino.gateway.chat → ino.neuron.handle</c>.
    /// </summary>
    static readonly ActivitySource ActivitySource = new("ino");

    public async Task<NeuronResult> HandleAsync(
        FindFlightsRequest synapse,
        NeuronContext ctx,
        CancellationToken ct)
    {
        using var span = ActivitySource.StartActivity("ino.neuron.handle", ActivityKind.Internal);
        span?.SetTag("ino.neuron.type", nameof(FlightSearch));
        span?.SetTag("ino.synapse.type", nameof(FindFlightsRequest));
        span?.SetTag("ino.correlation_id", ctx.CorrelationId.Value);

        var flights = FlightFixture.For(synapse.Query);
        var (description, data) = FlightCardTemplate.BuildList(flights);

        span?.SetTag("ino.flights.count", flights.Length);

        var summary = await NarrateAsync(synapse.Query, flights, ct);

        var response = new FlightCardResponse(
            Summary: summary,
            Flights: flights,
            RfwDescription: description,
            RfwData: data);

        span?.SetStatus(ActivityStatusCode.Ok);
        return NeuronResult.Ok(summary).With(response);
    }

    async Task<string> NarrateAsync(string query, FlightSummary[] flights, CancellationToken ct)
    {
        var fallback = $"Found {flights.Length} flights for '{query}'";
        if (flights.Length == 0) return fallback;

        var system = new StringBuilder()
            .AppendLine("You are a concise travel assistant.")
            .AppendLine("The user asked about flights. Available options:")
            .AppendLine(FormatFlights(flights))
            .Append("Reply in ONE short sentence summarising the options for the user. ")
            .Append("Reference at least one airline or price. Do not list every flight.")
            .ToString();

        try
        {
            var chat = chatFactory.ForTier(LlmTier.Balanced);
            var response = await chat.GetResponseAsync(
                new[]
                {
                    new ChatMessage(ChatRole.System, system),
                    new ChatMessage(ChatRole.User, query),
                },
                options: null,
                ct);
            return string.IsNullOrWhiteSpace(response.Text) ? fallback : response.Text.Trim();
        }
        catch (Exception ex) when (ex is BddMockMissException or NotSupportedException)
        {
            log.LogDebug("FlightSearch narrative skipped: {Reason}", ex.GetType().Name);
            return fallback;
        }
    }

    static string FormatFlights(FlightSummary[] flights) =>
        string.Join('\n', flights.Take(5).Select(f =>
            $"- {f.Airline}: {f.FromCode} → {f.ToCode}, ${f.PriceUsd}, departs {f.DepartTime}, {f.Duration}"));
}
