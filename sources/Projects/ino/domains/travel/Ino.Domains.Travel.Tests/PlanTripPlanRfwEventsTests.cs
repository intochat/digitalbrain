using Ino.Core;
using Ino.Core.Hosting;
using Ino.Kernel.Contracts;
using Ino.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Xunit;
using TripPlannerAgent = Ino.Domains.Travel.TripPlanner.TripPlanner;

namespace Ino.Domains.Travel.Tests;

/// <summary>
/// Locks the rich six-hop state machine in <see cref="TripPlannerAgent"/>:
/// initial execute emits the trip-intro card (weather + flights), then
/// <c>flight.selected</c> → hotels, <c>hotel.selected</c> → events with
/// skip, <c>event.selected</c>/<c>events.skipped</c> → activities,
/// <c>activity.selected</c> → trip summary. Unknown event names get a
/// friendly fallback response without throwing.
///
/// The grain is instantiated directly (outside Orleans's TestCluster) — the
/// event handler only touches its own instance fields and the logger, so it
/// runs cleanly without grain runtime context.
/// </summary>
public sealed class PlanTripPlanRfwEventsTests
{
    static TripPlannerAgent MakePlan() => new(
        firePort: Substitute.For<IFirePort>(),
        grainFactory: Substitute.For<IGrainFactory>(),
        chatClient: Substitute.For<IChatClient>(),
        log: NullLogger<TripPlannerAgent>.Instance);

    static NeuronPlanContext MakeContext(string prompt) => new(
        Prompt: prompt,
        Caller: NeuronContextForTest.Create(
            source: new Caller.FromDomain(DomainId.From("kernel")),
            userId: "test-user"),
        NeuronId: NeuronId.From("travel.plan-trip"));

    [Fact]
    public async Task Initial_execute_emits_trip_intro_with_weather_and_flights()
    {
        var plan = MakePlan();

        var result = await plan.ExecuteAsync(
            MakeContext("plan a trip to Bali next month"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Rfw);
        Assert.Equal("ino.travel.intro", result.Rfw!.LibraryName);
        // DSL imports both weather + flights libraries — substring is the
        // contract the gateway/E2E tests will match against.
        var dsl = System.Text.Encoding.UTF8.GetString(result.Rfw.DescriptionDsl);
        Assert.Contains("import ino.weather", dsl);
        Assert.Contains("import ino.flights", dsl);
    }

    [Fact]
    public async Task FlightSelected_advances_plan_and_emits_hotel_cards()
    {
        IRfwEventHandler plan = MakePlan();

        var result = await plan.HandleRfwEventAsync(
            "flight.selected",
            new Dictionary<string, string> { ["flightId"] = "FL-001" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Rfw);
        Assert.Equal("ino.travel.hotels", result.Rfw!.LibraryName);
    }

    [Fact]
    public async Task HotelSelected_advances_plan_and_emits_event_cards_with_skip()
    {
        IRfwEventHandler plan = MakePlan();

        var result = await plan.HandleRfwEventAsync(
            "hotel.selected",
            new Dictionary<string, string> { ["hotelId"] = "H-001" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Rfw);
        Assert.Equal("ino.travel.events", result.Rfw!.LibraryName);
        var dsl = System.Text.Encoding.UTF8.GetString(result.Rfw.DescriptionDsl);
        Assert.Contains("EventSkipButton", dsl);
        Assert.Contains("events.skipped", dsl);
    }

    [Fact]
    public async Task EventSelected_advances_plan_and_emits_activity_cards()
    {
        var plan = MakePlan();

        // Drive the prefix so destination is set on the grain instance.
        await plan.ExecuteAsync(MakeContext("plan a trip to Bali next month"), CancellationToken.None);
        await ((IRfwEventHandler)plan).HandleRfwEventAsync("flight.selected",
            new Dictionary<string, string> { ["flightId"] = "FL-001" }, CancellationToken.None);
        await ((IRfwEventHandler)plan).HandleRfwEventAsync("hotel.selected",
            new Dictionary<string, string> { ["hotelId"] = "H-001" }, CancellationToken.None);

        var result = await ((IRfwEventHandler)plan).HandleRfwEventAsync(
            "event.selected",
            new Dictionary<string, string> { ["eventId"] = "EV-001" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Rfw);
        Assert.Equal("ino.travel.activities", result.Rfw!.LibraryName);
    }

    [Fact]
    public async Task EventsSkipped_jumps_directly_to_activity_cards()
    {
        IRfwEventHandler plan = MakePlan();

        var result = await plan.HandleRfwEventAsync(
            "events.skipped",
            new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Rfw);
        Assert.Equal("ino.travel.activities", result.Rfw!.LibraryName);
    }

    [Fact]
    public async Task ActivitySelected_emits_trip_summary_card()
    {
        var plan = MakePlan();

        // Walk the full flow so the summary references all selections.
        await plan.ExecuteAsync(MakeContext("plan a trip to Bali next month"), CancellationToken.None);
        var handler = (IRfwEventHandler)plan;
        await handler.HandleRfwEventAsync("flight.selected",
            new Dictionary<string, string> { ["flightId"] = "FL-001" }, CancellationToken.None);
        await handler.HandleRfwEventAsync("hotel.selected",
            new Dictionary<string, string> { ["hotelId"] = "H-001" }, CancellationToken.None);
        await handler.HandleRfwEventAsync("event.selected",
            new Dictionary<string, string> { ["eventId"] = "EV-001" }, CancellationToken.None);

        var result = await handler.HandleRfwEventAsync(
            "activity.selected",
            new Dictionary<string, string> { ["activityId"] = "AC-001" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Rfw);
        Assert.Equal("ino.travel.summary", result.Rfw!.LibraryName);
        var data = System.Text.Encoding.UTF8.GetString(result.Rfw.DataPayload);
        Assert.Contains("Bali", data);
        Assert.Contains("Singapore Airlines", data); // FL-001's airline
    }

    [Fact]
    public async Task Unknown_event_returns_friendly_message_without_throwing()
    {
        IRfwEventHandler plan = MakePlan();

        var result = await plan.HandleRfwEventAsync(
            "totally.unknown.event",
            new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Rfw);
    }
}
