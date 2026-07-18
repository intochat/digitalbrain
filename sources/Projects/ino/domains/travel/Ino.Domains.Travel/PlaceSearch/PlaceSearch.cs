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

namespace Ino.Domains.Travel.PlaceSearch;

/// <summary>
/// Mirror of HotelSearch.HotelSearch for points of interest. Slice 9
/// replaces the seed fixture with a real TripRadar HTTP call; the
/// Balanced-tier narrative wrapper stays the same.
/// </summary>
public sealed class PlaceSearch(
    IChatClientFactory chatFactory,
    ILogger<PlaceSearch> log) : Grain, INeuron<FindPlacesRequest>
{
    static readonly ActivitySource ActivitySource = new("ino");

    public async Task<NeuronResult> HandleAsync(
        FindPlacesRequest synapse,
        NeuronContext ctx,
        CancellationToken ct)
    {
        using var span = ActivitySource.StartActivity("ino.neuron.handle", ActivityKind.Internal);
        span?.SetTag("ino.neuron.type", nameof(PlaceSearch));
        span?.SetTag("ino.synapse.type", nameof(FindPlacesRequest));
        span?.SetTag("ino.correlation_id", ctx.CorrelationId.Value);

        var places = PlaceFixture.For(synapse.Query);
        var (description, data) = PlaceCardTemplate.BuildList(places);

        span?.SetTag("ino.places.count", places.Length);

        var summary = await NarrateAsync(synapse.Query, places, ct);

        var response = new PlaceCardResponse(
            Summary: summary,
            Places: places,
            RfwDescription: description,
            RfwData: data);

        span?.SetStatus(ActivityStatusCode.Ok);
        return NeuronResult.Ok(summary).With(response);
    }

    async Task<string> NarrateAsync(string query, PlaceSummary[] places, CancellationToken ct)
    {
        var fallback = $"Found {places.Length} places for '{query}'";
        if (places.Length == 0) return fallback;

        var system = new StringBuilder()
            .AppendLine("You are a concise travel assistant.")
            .AppendLine("The user asked about things to do. Available options:")
            .AppendLine(FormatPlaces(places))
            .Append("Reply in ONE short sentence summarising the highlights. ")
            .Append("Reference at least one place by name.")
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
            log.LogDebug("PlaceSearch narrative skipped: {Reason}", ex.GetType().Name);
            return fallback;
        }
    }

    static string FormatPlaces(PlaceSummary[] places) =>
        string.Join('\n', places.Take(5).Select(p =>
            $"- {p.Name} ({p.Type}, {p.Rating:0.0}/5): {p.Description}"));
}
