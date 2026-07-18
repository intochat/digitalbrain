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

namespace Ino.Domains.Travel.HotelSearch;

/// <summary>
/// Mirror of <see cref="FlightSearch.FlightSearch"/>: destination-keyed seed fixture
/// + Balanced-tier LLM narrative summary grounded in the matched hotels.
/// Slice 9 replaces the fixture with a real TripRadar HTTP call; the LLM
/// hook stays the same.
/// </summary>
public sealed class HotelSearch(
    IChatClientFactory chatFactory,
    ILogger<HotelSearch> log) : Grain, INeuron<FindHotelsRequest>
{
    static readonly ActivitySource ActivitySource = new("ino");

    public async Task<NeuronResult> HandleAsync(
        FindHotelsRequest synapse,
        NeuronContext ctx,
        CancellationToken ct)
    {
        using var span = ActivitySource.StartActivity("ino.neuron.handle", ActivityKind.Internal);
        span?.SetTag("ino.neuron.type", nameof(HotelSearch));
        span?.SetTag("ino.synapse.type", nameof(FindHotelsRequest));
        span?.SetTag("ino.correlation_id", ctx.CorrelationId.Value);

        var hotels = HotelFixture.For(synapse.Query);
        var (description, data) = HotelCardTemplate.BuildList(hotels);

        span?.SetTag("ino.hotels.count", hotels.Length);

        var summary = await NarrateAsync(synapse.Query, hotels, ct);

        var response = new HotelCardResponse(
            Summary: summary,
            Hotels: hotels,
            RfwDescription: description,
            RfwData: data);

        span?.SetStatus(ActivityStatusCode.Ok);
        return NeuronResult.Ok(summary).With(response);
    }

    async Task<string> NarrateAsync(string query, HotelSummary[] hotels, CancellationToken ct)
    {
        var fallback = $"Found {hotels.Length} hotels for '{query}'";
        if (hotels.Length == 0) return fallback;

        var system = new StringBuilder()
            .AppendLine("You are a concise travel assistant.")
            .AppendLine("The user asked about hotels. Available options:")
            .AppendLine(FormatHotels(hotels))
            .Append("Reply in ONE short sentence summarising the options. ")
            .Append("Reference at least one hotel name, star rating, or nightly price.")
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
            log.LogDebug("HotelSearch narrative skipped: {Reason}", ex.GetType().Name);
            return fallback;
        }
    }

    static string FormatHotels(HotelSummary[] hotels) =>
        string.Join('\n', hotels.Take(5).Select(h =>
            $"- {h.Name} ({h.Stars}★, {h.Rating:0.0}/5) in {h.Location}, ${h.PricePerNightUsd}/night"));
}
