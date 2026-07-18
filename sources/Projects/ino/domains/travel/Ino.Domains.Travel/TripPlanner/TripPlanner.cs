using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Travel.Contracts;
using Ino.Domains.Travel.FlightSearch;
using Ino.Domains.Travel.HotelSearch;
using Ino.Domains.Travel.HotelSearch.Rfw;
using Ino.Domains.Travel.Rfw;
using Ino.Domains.Travel.TripPlanner.Rfw;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Travel.TripPlanner;

/// <summary>
/// Multi-step plan for the <c>travel.plan-trip</c> neuron. Walks six
/// hops backed by mock corpora — when <c>INO_TEST_MODE</c> drives Cortex
/// routing, the same flow executes end-to-end without touching a real LLM
/// or external API.
///
/// Hops:
/// <list type="number">
///   <item>Initial — emits weather climatology + flight cards (TripIntroBuilder)</item>
///   <item><c>flight.selected</c> — emits hotel cards</item>
///   <item><c>hotel.selected</c> — emits event cards with a Skip affordance</item>
///   <item><c>event.selected</c> or <c>events.skipped</c> — emits weather-aware activity cards</item>
///   <item><c>activity.selected</c> — emits trip summary card (no further interaction)</item>
/// </list>
///
/// State is in-memory and ephemeral — silo restart resets the trip.
/// Persistence lives behind issue #22 and is out of scope for v0.1.
/// </summary>
public sealed class TripPlanner(
    IFirePort firePort,
    IGrainFactory grainFactory,
    IChatClient chatClient,
    ILogger<TripPlanner> log) : Grain, ITripPlanner, INeuron<PlanTripRequest>, IRfwEventHandler
{
    static readonly NeuronId TravelPlanTripId = NeuronId.From("travel.plan-trip");

    public Task<NeuronResult> HandleAsync(
        PlanTripRequest synapse,
        NeuronContext ctx,
        CancellationToken ct) =>
        ExecuteAsync(new NeuronPlanContext(synapse.Query, ctx, TravelPlanTripId), ct);


    // Held for the LlmNeuron rewrite slice — once tools become real, the
    // plan delegates fan-out via firePort + grainFactory and the
    // chatClient drives narration. Today we route through static
    // corpora, so these are parked rather than ripped out.
    readonly IFirePort _firePort = firePort;
    readonly IGrainFactory _grainFactory = grainFactory;
    readonly IChatClient _chatClient = chatClient;

    string _destination = "your destination";
    string _month = "this season";
    WeatherClimatology? _climatology;
    FlightOption? _selectedFlight;
    HotelOption? _selectedHotel;
    EventOption? _selectedEvent;
    ActivityOption? _selectedActivity;

    public Task<NeuronResult> ExecuteAsync(NeuronPlanContext input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Reset trip state on a fresh execute — the user is starting over.
        ResetState();
        _destination = PlanTripPromptParser.ExtractDestination(input.Prompt);
        _month = PlanTripPromptParser.ExtractMonth(input.Prompt);
        _climatology = MockWeatherCorpus.GetClimatology(_destination, _month);

        var flights = MockFlightCorpus.For(input.Prompt);
        var rfw = TripIntroBuilder.Build(_climatology, flights);
        var headline =
            $"Planning your {_month} trip to {_destination}. Weather: {_climatology.Season} season, " +
            $"~{_climatology.AvgTempC}°C. First — pick a flight:";
        return Task.FromResult(NeuronResult.Ok(headline).WithRfwPayload(rfw));
    }

    public Task<NeuronResult> HandleRfwEventAsync(
        string eventName,
        IReadOnlyDictionary<string, string> args,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(args);

        switch (eventName)
        {
            case "flight.selected":
                _selectedFlight = LookupFlight(args.GetValueOrDefault("flightId"));
                var hotels = MockHotelCorpus.For(string.Empty);
                return Task.FromResult(
                    NeuronResult.Ok($"Got it — flight {_selectedFlight?.Airline ?? "selected"}. Now pick a hotel:")
                        .WithRfwPayload(HotelCardListBuilder.Build(hotels)));

            case "hotel.selected":
                _selectedHotel = LookupHotel(args.GetValueOrDefault("hotelId"));
                var events = MockEventsCorpus.For(_destination);
                return Task.FromResult(
                    NeuronResult.Ok($"{_selectedHotel?.Name ?? "Hotel"} booked. Anything happening while you're there?")
                        .WithRfwPayload(EventCardListBuilder.Build(events)));

            case "event.selected":
                _selectedEvent = LookupEvent(args.GetValueOrDefault("eventId"));
                return EmitActivities(
                    headline: $"Saved \"{_selectedEvent?.Title ?? "the event"}\". Pick something to do:");

            case "events.skipped":
                _selectedEvent = null;
                return EmitActivities(
                    headline: "No events this trip. Pick something to do:");

            case "activity.selected":
                _selectedActivity = LookupActivity(args.GetValueOrDefault("activityId"));
                var summary = TripSummaryBuilder.Build(
                    destination: _destination,
                    weather: _climatology ?? MockWeatherCorpus.GetClimatology(_destination, _month),
                    flightAirline: _selectedFlight?.Airline,
                    hotelName: _selectedHotel?.Name,
                    eventTitle: _selectedEvent?.Title,
                    activityName: _selectedActivity?.Name);
                return Task.FromResult(
                    NeuronResult.Ok(
                        $"Trip to {_destination} sketched out — flight, hotel, " +
                        $"and {_selectedActivity?.Name ?? "your pick"}. Have a great time.")
                        .WithRfwPayload(summary));

            default:
                log.LogDebug("plan-trip: unknown rfw event {EventName}", eventName);
                return Task.FromResult(NeuronResult.Ok($"(unknown event {eventName})"));
        }
    }

    Task<NeuronResult> EmitActivities(string headline)
    {
        var climatology = _climatology ?? MockWeatherCorpus.GetClimatology(_destination, _month);
        var activities = MockActivityCorpus.For(_destination, climatology);
        return Task.FromResult(
            NeuronResult.Ok(headline)
                .WithRfwPayload(ActivityCardListBuilder.Build(activities)));
    }

    void ResetState()
    {
        _destination = "your destination";
        _month = "this season";
        _climatology = null;
        _selectedFlight = null;
        _selectedHotel = null;
        _selectedEvent = null;
        _selectedActivity = null;
    }

    FlightOption? LookupFlight(string? id) =>
        string.IsNullOrEmpty(id) ? null
            : MockFlightCorpus.For(string.Empty).FirstOrDefault(f => f.Id == id);

    HotelOption? LookupHotel(string? id) =>
        string.IsNullOrEmpty(id) ? null
            : MockHotelCorpus.For(string.Empty).FirstOrDefault(h => h.Id == id);

    EventOption? LookupEvent(string? id) =>
        string.IsNullOrEmpty(id) ? null
            : MockEventsCorpus.For(_destination).FirstOrDefault(e => e.Id == id);

    ActivityOption? LookupActivity(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var climatology = _climatology ?? MockWeatherCorpus.GetClimatology(_destination, _month);
        return MockActivityCorpus.For(_destination, climatology).FirstOrDefault(a => a.Id == id);
    }
}
