# Travel Domain — First App on ino: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Absorb TripRadar into ino as the first installed application — travel neurons wrapping existing MediatR handlers, Rive persona driven by real neural activity, RFW server-driven UI with pre-designed travel cards, full E2E from Google auth to flight search to price tracking.

**Architecture:** TripRadar moves to `domains/travel/TripRadar/`. A new `Ino.Travel` class library defines 7 neurons (User, FlightSearch, HotelSearch, PlaceDiscovery, PriceTracker, TripVault, TravelRecommender) that inject `IMediator` and delegate to existing handlers. Aspire wires Postgres/Redis/Kafka. The proto gets RFW fields. The Flutter chat screen renders server-pushed RFW cards via the existing `ino_runtime`. The Rive persona replaces CustomPaint with a state machine driven by real timeline events.

**Tech Stack:** .NET 11, Orleans 9, MediatR 14, EF Core 11, SerpApi, Aspire 13, Flutter 3.11, gRPC, RFW 1.1, Rive 0.14, BLoC 9

**Spec:** `docs/superpowers/specs/2026-04-11-travel-domain-first-app-design.md`

---

## Task 1: Repo Restructure — Move TripRadar into domains/travel/

**Files:**
- Move: `TripRadar/` → `domains/travel/TripRadar/`
- Delete: `TripRadar/.git/` (absorbed into ino's git)
- Modify: `ino.slnx` (add travel projects to solution)

- [ ] **Step 1: Remove TripRadar's standalone git**

```bash
rm -rf TripRadar/.git
```

- [ ] **Step 2: Move TripRadar into domains/travel/**

```bash
mkdir -p domains/travel
mv TripRadar domains/travel/TripRadar
```

- [ ] **Step 3: Verify TripRadar builds standalone**

```bash
dotnet build domains/travel/TripRadar/src/TripRadar.Server.Application/TripRadar.Server.Application.csproj
dotnet build domains/travel/TripRadar/src/TripRadar.Server.Infrastructure/TripRadar.Server.Infrastructure.csproj
```

Expected: both succeed (they have no dependency on ino)

- [ ] **Step 4: Add travel projects to ino.slnx**

Add the 7 kept TripRadar projects to the ino solution under a `domains/travel` solution folder. The projects to include:

```xml
<!-- In ino.slnx, add inside a solution folder -->
<Folder Name="/domains/travel/">
  <Project Path="domains/travel/TripRadar/src/TripRadar.Server.Domain/TripRadar.Server.Domain.csproj" />
  <Project Path="domains/travel/TripRadar/src/TripRadar.Server.Application/TripRadar.Server.Application.csproj" />
  <Project Path="domains/travel/TripRadar/src/TripRadar.Server.Infrastructure/TripRadar.Server.Infrastructure.csproj" />
  <Project Path="domains/travel/TripRadar/src/TripRadar.Server.Db/TripRadar.Server.Db.csproj" />
  <Project Path="domains/travel/TripRadar/src/TripRadar.Server.Comms.Core/TripRadar.Server.Comms.Core.csproj" />
  <Project Path="domains/travel/TripRadar/src/TripRadar.Server.API.Contracts/TripRadar.Server.API.Contracts.csproj" />
  <Project Path="domains/travel/TripRadar/src/TripRadar.Server.API/TripRadar.Server.API.csproj" />
</Folder>
```

- [ ] **Step 5: Verify ino solution builds**

```bash
dotnet build ino.slnx
```

Expected: all existing ino projects + TripRadar projects compile. Fix any path issues from the move.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: move TripRadar into domains/travel/ as first installed app"
```

---

## Task 2: Create Ino.Travel Class Library

**Files:**
- Create: `domains/travel/Ino.Travel/Ino.Travel.csproj`
- Create: `domains/travel/Ino.Travel/TravelDomainRegistration.cs`
- Modify: `ino.slnx` (add Ino.Travel project)

- [ ] **Step 1: Create the project file**

Create `domains/travel/Ino.Travel/Ino.Travel.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Ino.Travel</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\iaw\Core\Core.csproj" />
    <ProjectReference Include="..\TripRadar\src\TripRadar.Server.Application\TripRadar.Server.Application.csproj" />
    <ProjectReference Include="..\TripRadar\src\TripRadar.Server.Infrastructure\TripRadar.Server.Infrastructure.csproj" />
    <ProjectReference Include="..\TripRadar\src\TripRadar.Server.Domain\TripRadar.Server.Domain.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create DI registration entry point**

Create `domains/travel/Ino.Travel/TravelDomainRegistration.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TripRadar.Server.Application.Extensions;

namespace Ino.Travel;

public static class TravelDomainRegistration
{
    public static IServiceCollection AddTravelDomain(this IServiceCollection services)
    {
        services.ConfigureApplicationLayer();
        return services;
    }
}
```

This calls TripRadar's existing `ConfigureApplicationLayer()` which registers MediatR, validators, orchestrators, and all application services.

- [ ] **Step 3: Add to solution and verify build**

Add `domains/travel/Ino.Travel/Ino.Travel.csproj` to `ino.slnx` under the `domains/travel` folder.

```bash
dotnet build domains/travel/Ino.Travel/Ino.Travel.csproj
```

Expected: compiles with references to both ino Core and TripRadar projects.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(travel): create Ino.Travel class library referencing TripRadar"
```

---

## Task 3: Travel Neuron Interfaces

**Files:**
- Create: `domains/travel/Ino.Travel/Neurons/IFlightSearch.cs`
- Create: `domains/travel/Ino.Travel/Neurons/IHotelSearch.cs`
- Create: `domains/travel/Ino.Travel/Neurons/IPlaceDiscovery.cs`
- Create: `domains/travel/Ino.Travel/Neurons/IPriceTracker.cs`
- Create: `domains/travel/Ino.Travel/Neurons/ITripVault.cs`
- Create: `domains/travel/Ino.Travel/Neurons/ITravelRecommender.cs`
- Create: `domains/travel/Ino.Travel/Neurons/IUser.cs`

Each interface follows the pattern from `iaw/Core/Contracts/IAgent.cs` — extends `IAgent`, defines static virtual metadata, declares tool methods with `[Description]` attributes.

- [ ] **Step 1: Create IFlightSearch interface**

Create `domains/travel/Ino.Travel/Neurons/IFlightSearch.cs`:

```csharp
using System.ComponentModel;
using Core.Contracts;

namespace Ino.Travel.Neurons;

public interface IFlightSearch : IAgent
{
    static string IAgent.AgentDisplayName => "Flight Search";
    static string IAgent.AgentDescription =>
        "Searches flights via SerpApi — round trips, one-way, multi-city. Price calendars, nearby airport prices, destination exploration.";
    static string[] IAgent.AgentCapabilities => ["flights", "search", "travel", "prices", "airlines"];
    static string IAgent.AgentInstructions =>
        """
        You are ino's flight search neuron. You find flights, compare prices, and help users discover destinations.
        When a user asks about flights, use your tools to search SerpApi. Present results clearly with airline,
        times, duration, stops, and price. Suggest price calendars when dates are flexible. Offer nearby airport
        alternatives when the primary route is expensive.
        """;
    static string[] IAgent.AgentRoutingExamples =>
    [
        "find flights to Bali",
        "cheapest flights NYC to LAX next Friday",
        "show me the price calendar for Tokyo in June",
        "flights from JFK to London round trip",
        "explore destinations from San Francisco"
    ];

    [Description("Search flights between airports on specific dates")]
    Task<string> SearchFlights(
        [Description("Departure airport code or city name")] string from,
        [Description("Destination airport code or city name")] string to,
        [Description("Departure date (YYYY-MM-DD)")] string departureDate,
        [Description("Return date for round trip (YYYY-MM-DD), omit for one-way")] string? returnDate,
        CancellationToken ct);

    [Description("Get a price calendar showing flight costs across dates")]
    Task<string> GetPriceCalendar(
        [Description("Departure airport code")] string from,
        [Description("Destination airport code")] string to,
        [Description("Number of months ahead to check")] int monthsAhead,
        CancellationToken ct);

    [Description("Explore flight destinations from a departure city")]
    Task<string> ExploreDestinations(
        [Description("Departure airport code")] string from,
        CancellationToken ct);
}
```

- [ ] **Step 2: Create IHotelSearch interface**

Create `domains/travel/Ino.Travel/Neurons/IHotelSearch.cs`:

```csharp
using System.ComponentModel;
using Core.Contracts;

namespace Ino.Travel.Neurons;

public interface IHotelSearch : IAgent
{
    static string IAgent.AgentDisplayName => "Hotel Search";
    static string IAgent.AgentDescription =>
        "Searches hotels at any destination — pricing, star ratings, amenities, reviews.";
    static string[] IAgent.AgentCapabilities => ["hotels", "search", "travel", "accommodation"];
    static string IAgent.AgentInstructions =>
        """
        You are ino's hotel search neuron. You find hotels at any destination with pricing, ratings, and amenities.
        Present results with name, star rating, price per night, location, and top amenities.
        """;
    static string[] IAgent.AgentRoutingExamples =>
    [
        "find hotels in Bali",
        "cheap hotels in Tokyo for July",
        "5-star hotels near Times Square"
    ];

    [Description("Search hotels at a destination for given dates")]
    Task<string> SearchHotels(
        [Description("Destination city or area")] string location,
        [Description("Check-in date (YYYY-MM-DD)")] string checkIn,
        [Description("Check-out date (YYYY-MM-DD)")] string checkOut,
        CancellationToken ct);
}
```

- [ ] **Step 3: Create IPlaceDiscovery interface**

Create `domains/travel/Ino.Travel/Neurons/IPlaceDiscovery.cs`:

```csharp
using System.ComponentModel;
using Core.Contracts;

namespace Ino.Travel.Neurons;

public interface IPlaceDiscovery : IAgent
{
    static string IAgent.AgentDisplayName => "Place Discovery";
    static string IAgent.AgentDescription =>
        "Discovers local places, restaurants, attractions, and events at any destination.";
    static string[] IAgent.AgentCapabilities => ["places", "restaurants", "attractions", "events", "travel"];
    static string IAgent.AgentInstructions =>
        """
        You are ino's place discovery neuron. You find interesting places, restaurants, attractions, and events
        at any location. Present results with name, type, rating, and why it's worth visiting.
        """;
    static string[] IAgent.AgentRoutingExamples =>
    [
        "things to do in Bali",
        "best restaurants in Tokyo",
        "events in NYC this weekend",
        "attractions near my hotel in Lisbon"
    ];

    [Description("Find local places and attractions at a destination")]
    Task<string> FindPlaces(
        [Description("Location to search")] string location,
        [Description("Type of place: restaurant, attraction, shopping, nightlife")] string? type,
        CancellationToken ct);

    [Description("Find events happening at a destination")]
    Task<string> FindEvents(
        [Description("Location to search")] string location,
        [Description("Date range start (YYYY-MM-DD)")] string? fromDate,
        CancellationToken ct);
}
```

- [ ] **Step 4: Create IPriceTracker interface**

Create `domains/travel/Ino.Travel/Neurons/IPriceTracker.cs`:

```csharp
using System.ComponentModel;
using Core.Contracts;

namespace Ino.Travel.Neurons;

public interface IPriceTracker : IAgent
{
    static string IAgent.AgentDisplayName => "Price Tracker";
    static string IAgent.AgentDescription =>
        "Monitors flight and hotel prices over time, alerts on price drops, tracks trends.";
    static string[] IAgent.AgentCapabilities => ["price-tracking", "alerts", "monitoring", "travel"];
    static string IAgent.AgentInstructions =>
        """
        You are ino's price tracker neuron. You monitor flights and hotels for price changes. When a user
        asks you to track a price, set up a recurring check. Alert immediately on significant drops.
        Report trends when asked about tracked items.
        """;
    static string[] IAgent.AgentRoutingExamples =>
    [
        "track the price of flights to Bali",
        "alert me if that flight drops below $400",
        "what are my tracked prices",
        "stop tracking the London flight"
    ];

    [Description("Start tracking a flight route for price changes")]
    Task<string> TrackFlight(
        [Description("Departure airport code")] string from,
        [Description("Destination airport code")] string to,
        [Description("Departure date (YYYY-MM-DD)")] string departureDate,
        [Description("Return date (YYYY-MM-DD)")] string? returnDate,
        [Description("Current known price to use as baseline")] decimal? currentPrice,
        CancellationToken ct);

    [Description("List all currently tracked flights and hotels")]
    Task<string> GetTrackedPrices(CancellationToken ct);

    [Description("Stop tracking a specific flight or hotel")]
    Task<string> StopTracking(
        [Description("Tracking ID to cancel")] string trackingId,
        CancellationToken ct);
}
```

- [ ] **Step 5: Create ITripVault interface**

Create `domains/travel/Ino.Travel/Neurons/ITripVault.cs`:

```csharp
using System.ComponentModel;
using Core.Contracts;

namespace Ino.Travel.Neurons;

public interface ITripVault : IAgent
{
    static string IAgent.AgentDisplayName => "Trip Vault";
    static string IAgent.AgentDescription =>
        "Saves and manages trip plans, flight/hotel bookmarks, and search history.";
    static string[] IAgent.AgentCapabilities => ["saved-trips", "bookmarks", "history", "travel"];
    static string IAgent.AgentInstructions =>
        """
        You are ino's trip vault neuron. You save trips, manage bookmarks for flights and hotels,
        and recall search history. Help users organize their travel plans.
        """;
    static string[] IAgent.AgentRoutingExamples =>
    [
        "save this trip to my vault",
        "show my saved trips",
        "what flights did I search last week"
    ];

    [Description("Save a trip plan to the user's vault")]
    Task<string> SaveTrip(
        [Description("Name for this saved trip")] string name,
        [Description("Trip details as JSON")] string tripData,
        CancellationToken ct);

    [Description("List all saved trips")]
    Task<string> GetSavedTrips(CancellationToken ct);

    [Description("Remove a saved trip")]
    Task<string> RemoveTrip(
        [Description("Trip vault ID to remove")] string vaultId,
        CancellationToken ct);
}
```

- [ ] **Step 6: Create ITravelRecommender interface**

Create `domains/travel/Ino.Travel/Neurons/ITravelRecommender.cs`:

```csharp
using System.ComponentModel;
using Core.Contracts;

namespace Ino.Travel.Neurons;

public interface ITravelRecommender : IAgent
{
    static string IAgent.AgentDisplayName => "Travel Recommender";
    static string IAgent.AgentDescription =>
        "AI-powered travel planning — recommends destinations, composes itineraries, synthesizes flight/hotel/place results into actionable plans.";
    static string[] IAgent.AgentCapabilities => ["recommendations", "planning", "itineraries", "travel"];
    static string IAgent.AgentInstructions =>
        """
        You are ino's travel recommender — the brain of the travel experience. You compose results from
        FlightSearch, HotelSearch, and PlaceDiscovery neurons into coherent travel recommendations.
        When a user has a vague request ("somewhere warm in July"), explore destinations, check flight
        prices, and present curated options. When they pick a destination, build a full plan with
        flights, hotels, and things to do. Always offer to track prices. Be proactive — suggest
        alternatives, flag deals, remember preferences from past conversations.
        """;
    static string[] IAgent.AgentRoutingExamples =>
    [
        "I want to go somewhere warm in July",
        "plan a trip to Bali",
        "suggest weekend getaways from NYC",
        "what's the cheapest beach destination right now",
        "help me plan my vacation"
    ];
}
```

Note: TravelRecommenderNeuron has no custom tool methods — it uses other travel neurons' interfaces as tools via the agent routing system. Its LLM calls FlightSearch, HotelSearch, PlaceDiscovery tools directly.

- [ ] **Step 7: Create IUser interface**

Create `domains/travel/Ino.Travel/Neurons/IUser.cs`:

```csharp
using System.ComponentModel;
using Core.Contracts;

namespace Ino.Travel.Neurons;

public interface IUser : IAgent
{
    static string IAgent.AgentDisplayName => "User";
    static string IAgent.AgentDescription =>
        "User authentication and profile management — Google auth, preferences, settings.";
    static string[] IAgent.AgentCapabilities => ["auth", "profile", "preferences", "user"];
    static string IAgent.AgentInstructions =>
        """
        You are ino's user management neuron. You handle authentication (Google OAuth),
        profile updates, and preference management. Keep user interactions minimal — auth
        should be seamless, preferences learned from behavior when possible.
        """;
    static string[] IAgent.AgentRoutingExamples =>
    [
        "sign in with Google",
        "update my preferences",
        "change my timezone"
    ];

    [Description("Authenticate a user via Google OAuth token")]
    Task<string> Authenticate(
        [Description("Google OAuth ID token")] string googleIdToken,
        CancellationToken ct);

    [Description("Get the current user's profile")]
    Task<string> GetProfile(CancellationToken ct);

    [Description("Update user preferences")]
    Task<string> UpdatePreferences(
        [Description("Preferences as JSON")] string preferences,
        CancellationToken ct);
}
```

- [ ] **Step 8: Verify build**

```bash
dotnet build domains/travel/Ino.Travel/Ino.Travel.csproj
```

Expected: all 7 interfaces compile, referencing `IAgent` from Core.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(travel): define 7 travel neuron interfaces (IFlightSearch, IHotelSearch, IPlaceDiscovery, IPriceTracker, ITripVault, ITravelRecommender, IUser)"
```

---

## Task 4: FlightSearchNeuron — First Neuron Implementation

This is the pattern-setting neuron. Get this right, and the other 5 follow the same shape.

**Files:**
- Create: `domains/travel/Ino.Travel/Neurons/FlightSearchNeuron.cs`

- [ ] **Step 1: Create FlightSearchNeuron**

Create `domains/travel/Ino.Travel/Neurons/FlightSearchNeuron.cs`:

```csharp
using System.Text.Json;
using IAW.Core;
using IAW.Core.AI;
using MediatR;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlights;
using TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlightPriceCalendar;
using TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlightExplore;

namespace Ino.Travel.Neurons;

public class FlightSearchNeuron(
    [AgentState] AgentDurableState durableState,
    [Llm<OpenAIModels.Gpt54Nano>] IChatClient chatClient,
    IMediator mediator,
    ILogger<FlightSearchNeuron> logger)
    : Agent<IFlightSearch>(durableState, chatClient), IFlightSearch
{
    public async Task<string> SearchFlights(string from, string to, string departureDate,
        string? returnDate, CancellationToken ct)
    {
        logger.LogInformation("Searching flights {From} → {To} on {Date}", from, to, departureDate);

        var request = new GetFlightRequestDTO
        {
            FlightSearch = new FlightSearchDTO
            {
                DepartureId = from,
                ArrivalId = to
            },
            AdvancedOptions = new FlightAdvancedOptionsDTO()
        };

        var query = new GetFlightsQuery(request, "ino-user");
        var result = await mediator.Send(query, ct);

        if (result.IsFailure)
            return $"Flight search failed: {result.Error.Message}";

        return JsonSerializer.Serialize(new
        {
            type = "flight_results",
            flights = result.Value
        });
    }

    public async Task<string> GetPriceCalendar(string from, string to, int monthsAhead,
        CancellationToken ct)
    {
        logger.LogInformation("Price calendar {From} → {To}, {Months} months", from, to, monthsAhead);

        var query = new GetFlightPriceCalendarQuery(
            new GetFlightPriceCalendarRequestDTO
            {
                DepartureId = from,
                ArrivalId = to
            }, "ino-user");
        var result = await mediator.Send(query, ct);

        if (result.IsFailure)
            return $"Price calendar failed: {result.Error.Message}";

        return JsonSerializer.Serialize(new
        {
            type = "price_calendar",
            calendar = result.Value
        });
    }

    public async Task<string> ExploreDestinations(string from, CancellationToken ct)
    {
        logger.LogInformation("Exploring destinations from {From}", from);

        var query = new GetFlightExploreQuery(
            new GetFlightExploreRequestDTO { DepartureId = from }, "ino-user");
        var result = await mediator.Send(query, ct);

        if (result.IsFailure)
            return $"Destination explore failed: {result.Error.Message}";

        return JsonSerializer.Serialize(new
        {
            type = "destination_results",
            destinations = result.Value
        });
    }
}
```

**Important:** The exact DTO types (`GetFlightRequestDTO`, `FlightSearchDTO`, `FlightAdvancedOptionsDTO`) and query constructors need to match TripRadar's existing types exactly. Read `TripRadar.Server.Application/DTO/Requests/` and `UseCases/SearchEngine/Flights/Queries/` to verify constructor signatures before writing. The code above is the structural pattern — adapt the DTO construction to match the actual types found.

- [ ] **Step 2: Verify build**

```bash
dotnet build domains/travel/Ino.Travel/Ino.Travel.csproj
```

Fix any DTO type mismatches. The neuron must compile against TripRadar's actual request/response types.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(travel): implement FlightSearchNeuron wrapping MediatR handlers"
```

---

## Task 5: Remaining Travel Neurons

**Files:**
- Create: `domains/travel/Ino.Travel/Neurons/HotelSearchNeuron.cs`
- Create: `domains/travel/Ino.Travel/Neurons/PlaceDiscoveryNeuron.cs`
- Create: `domains/travel/Ino.Travel/Neurons/PriceTrackerNeuron.cs`
- Create: `domains/travel/Ino.Travel/Neurons/TripVaultNeuron.cs`
- Create: `domains/travel/Ino.Travel/Neurons/UserNeuron.cs`
- Create: `domains/travel/Ino.Travel/Neurons/TravelRecommenderNeuron.cs`

Each follows the FlightSearchNeuron pattern: primary constructor with `[AgentState]`, `[Llm<>]`, `IMediator`, implements its interface, delegates to MediatR handlers.

- [ ] **Step 1: Create HotelSearchNeuron**

Create `domains/travel/Ino.Travel/Neurons/HotelSearchNeuron.cs`:

```csharp
using System.Text.Json;
using IAW.Core;
using IAW.Core.AI;
using MediatR;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.UseCases.SearchEngine.Hotels.Queries.GetHotels;

namespace Ino.Travel.Neurons;

public class HotelSearchNeuron(
    [AgentState] AgentDurableState durableState,
    [Llm<OpenAIModels.Gpt54Nano>] IChatClient chatClient,
    IMediator mediator,
    ILogger<HotelSearchNeuron> logger)
    : Agent<IHotelSearch>(durableState, chatClient), IHotelSearch
{
    public async Task<string> SearchHotels(string location, string checkIn, string checkOut,
        CancellationToken ct)
    {
        logger.LogInformation("Searching hotels in {Location}", location);

        var query = new GetHotelsQuery(
            new TripRadar.Server.Application.DTO.Requests.GetHotelRequestDTO
            {
                Location = location
            }, "ino-user");
        var result = await mediator.Send(query, ct);

        if (result.IsFailure)
            return $"Hotel search failed: {result.Error.Message}";

        return JsonSerializer.Serialize(new
        {
            type = "hotel_results",
            hotels = result.Value
        });
    }
}
```

**Adapt DTO construction to match actual TripRadar types found in `UseCases/SearchEngine/Hotels/`.**

- [ ] **Step 2: Create PlaceDiscoveryNeuron**

Create `domains/travel/Ino.Travel/Neurons/PlaceDiscoveryNeuron.cs` — same pattern, wraps `GetLocalPlacesQueryHandler` and `GetEventsQueryHandler`. Read `UseCases/SearchEngine/LocalPlaces/` and `UseCases/SearchEngine/Events/` for exact query types.

- [ ] **Step 3: Create PriceTrackerNeuron**

Create `domains/travel/Ino.Travel/Neurons/PriceTrackerNeuron.cs`:

```csharp
using System.Text.Json;
using IAW.Core;
using IAW.Core.AI;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Ino.Travel.Neurons;

public class PriceTrackerNeuron(
    [AgentState] AgentDurableState durableState,
    [Llm<OpenAIModels.Gpt54Nano>] IChatClient chatClient,
    IMediator mediator,
    ILogger<PriceTrackerNeuron> logger)
    : Agent<IPriceTracker>(durableState, chatClient), IPriceTracker
{
    public async Task<string> TrackFlight(string from, string to, string departureDate,
        string? returnDate, decimal? currentPrice, CancellationToken ct)
    {
        logger.LogInformation("Tracking {From} → {To} on {Date}", from, to, departureDate);

        // set up Orleans recurring job for price checks
        var trackingId = $"track-{from}-{to}-{departureDate}";
        await ScheduleRecurringJob(
            trackingId,
            TimeSpan.FromHours(6),
            $"check_price {from} {to} {departureDate} {currentPrice}",
            ct);

        return JsonSerializer.Serialize(new
        {
            trackingId,
            message = $"Tracking {from} → {to}. I'll check every 6 hours and alert you on price drops.",
            currentPrice
        });
    }

    public async Task<string> GetTrackedPrices(CancellationToken ct)
    {
        var jobs = await ListJobs(ct);
        return JsonSerializer.Serialize(new
        {
            tracked = jobs.Select(j => new { j.Name, j.LastRun, j.Interval })
        });
    }

    public async Task<string> StopTracking(string trackingId, CancellationToken ct)
    {
        await CancelJob(trackingId, ct);
        return $"Stopped tracking {trackingId}";
    }
}
```

- [ ] **Step 4: Create TripVaultNeuron**

Create `domains/travel/Ino.Travel/Neurons/TripVaultNeuron.cs` — wraps `CreateTripVaultCommandHandler`, `DeleteTripVaultCommandHandler`. Read `UseCases/TripVault/` for exact command types.

- [ ] **Step 5: Create UserNeuron**

Create `domains/travel/Ino.Travel/Neurons/UserNeuron.cs` — wraps `GoogleLoginCommandHandler`, `UpdateUserProfileCommandHandler`. Read `UseCases/Authentication/` and `UseCases/Users/` for exact types.

- [ ] **Step 6: Create TravelRecommenderNeuron**

Create `domains/travel/Ino.Travel/Neurons/TravelRecommenderNeuron.cs`:

```csharp
using IAW.Core;
using IAW.Core.AI;
using Microsoft.Extensions.Logging;

namespace Ino.Travel.Neurons;

public class TravelRecommenderNeuron(
    [AgentState] AgentDurableState durableState,
    [Llm<OpenAIModels.Gpt54Mini>] IChatClient chatClient,
    ILogger<TravelRecommenderNeuron> logger)
    : Agent<ITravelRecommender>(durableState, chatClient), ITravelRecommender
{
    protected override IEnumerable<AITool> DefineTools()
    {
        // expose other travel neurons as tools for composition
        var flightSearch = GrainFactory.GetGrain<IFlightSearch>("travel");
        var hotelSearch = GrainFactory.GetGrain<IHotelSearch>("travel");
        var placeDiscovery = GrainFactory.GetGrain<IPlaceDiscovery>("travel");
        var priceTracker = GrainFactory.GetGrain<IPriceTracker>("travel");
        var tripVault = GrainFactory.GetGrain<ITripVault>("travel");

        var tools = new List<AITool>();
        RegisterToolMethods(tools, flightSearch);
        RegisterToolMethods(tools, hotelSearch);
        RegisterToolMethods(tools, placeDiscovery);
        RegisterToolMethods(tools, priceTracker);
        RegisterToolMethods(tools, tripVault);
        return tools;
    }
}
```

**Important:** The exact mechanism for exposing other grains as tools depends on how `Agent.Tools.cs` handles remote grain method discovery. Read `iaw/Core/Agents/Agent.Tools.cs` to verify `RegisterToolMethods` works with grain references. If it doesn't, create wrapper methods that delegate to the grain calls and expose those as tools via `AIFunctionFactory.Create()`.

- [ ] **Step 7: Verify full build**

```bash
dotnet build domains/travel/Ino.Travel/Ino.Travel.csproj
```

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(travel): implement all 7 travel neurons (HotelSearch, PlaceDiscovery, PriceTracker, TripVault, User, TravelRecommender)"
```

---

## Task 6: Aspire Wiring

**Files:**
- Create: `domains/travel/Ino.Travel/Hosting/TravelDomainExtensions.cs`
- Create: `domains/travel/Ino.Travel/Hosting/TravelDomainResources.cs`
- Modify: `iaw/Aspire/AppHost.cs`
- Modify: `iaw/Aspire/Aspire.csproj` (add project reference to Ino.Travel)
- Modify: `iaw/Agents.Host/Agents.Host.csproj` (add project reference to Ino.Travel for grain scanning)

- [ ] **Step 1: Create TravelDomainResources record**

Create `domains/travel/Ino.Travel/Hosting/TravelDomainResources.cs`:

```csharp
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Ino.Travel.Hosting;

public record TravelDomainResources(
    IResourceBuilder<PostgresDatabaseResource> Database,
    IResourceBuilder<RedisResource> Redis,
    IResourceBuilder<KafkaServerResource> Kafka,
    IResourceBuilder<ProjectResource> Migrations);
```

- [ ] **Step 2: Create TravelDomainExtensions**

Create `domains/travel/Ino.Travel/Hosting/TravelDomainExtensions.cs`:

```csharp
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Ino.Travel.Hosting;

public static class TravelDomainExtensions
{
    public static TravelDomainResources AddTravelDomain(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresDatabaseResource> database,
        IResourceBuilder<RedisResource> redis,
        IResourceBuilder<KafkaServerResource> kafka)
    {
        var serpApiKey = builder.AddParameter("SerpApiKey", secret: true);
        var googleClientId = builder.AddParameter("GoogleClientId", secret: true);
        var googleClientSecret = builder.AddParameter("GoogleClientSecret", secret: true);
        var jwtSecret = builder.AddParameter("JwtSecret", secret: true);
        var encryptionKey = builder.AddParameter("EncryptionKey", secret: true);

        var migrations = builder.AddProject<Projects.TripRadar_Server_Db>("travel-migrations")
            .WithReference(database)
            .WaitFor(database);

        return new TravelDomainResources(database, redis, kafka, migrations);
    }

    public static IResourceBuilder<T> WithTravelDomain<T>(
        this IResourceBuilder<T> builder,
        TravelDomainResources travel)
        where T : IResourceWithEnvironment, IResourceWithEndpoints, IResourceWithWaitSupport
    {
        return builder
            .WithReference(travel.Database)
            .WithReference(travel.Redis)
            .WithReference(travel.Kafka)
            .WaitFor(travel.Migrations);
    }
}
```

**Important:** The Aspire project reference to `TripRadar.Server.Db` needs the correct `Projects.TripRadar_Server_Db` type. Verify the generated type name matches after adding the project reference. Also verify what parameters TripRadar's Infrastructure layer expects (check `TripRadar.Server.Infrastructure/Extensions/` for `IOptions<SerpApiSettings>` binding).

- [ ] **Step 3: Add project references**

Add to `iaw/Aspire/Aspire.csproj`:
```xml
<ProjectReference Include="..\..\domains\travel\Ino.Travel\Ino.Travel.csproj" />
<ProjectReference Include="..\..\domains\travel\TripRadar\src\TripRadar.Server.Db\TripRadar.Server.Db.csproj" />
```

Add to `iaw/Agents.Host/Agents.Host.csproj`:
```xml
<ProjectReference Include="..\..\domains\travel\Ino.Travel\Ino.Travel.csproj" />
```

- [ ] **Step 4: Wire into AppHost**

Modify `iaw/Aspire/AppHost.cs` — add travel domain wiring after the existing IAW setup:

```csharp
// after existing var iaw = builder.AddIAW(...) block:

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var travelDb = postgres.AddDatabase("travel");
var redis = builder.AddRedis("redis")
    .WithRedisInsight()
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
var kafka = builder.AddKafka("kafka")
    .WithKafkaUI()
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var travel = builder.AddTravelDomain(travelDb, redis, kafka);

// modify the assistant project to include travel domain
// (find the existing AddProject<Projects.Agents_Host>("assistant") line and add .WithTravelDomain(travel))
```

- [ ] **Step 5: Register travel DI in the silo host**

Modify `iaw/Agents.Host/Program.cs` (or wherever the silo DI is configured) to call `services.AddTravelDomain()` which registers MediatR, TripRadar's `TripRadarDbContext`, SerpApi provider, and all application services.

Read `TripRadar.Server.Infrastructure/Extensions/ServiceCollectionExtensions.cs` to see what `ConfigureInfrastructureLayer` registers (DbContext, SerpApi, Google auth, etc.) and call it alongside `ConfigureApplicationLayer`.

- [ ] **Step 6: Verify build**

```bash
dotnet build ino.slnx
```

Expected: full solution compiles including travel domain.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(travel): wire travel domain into Aspire AppHost with Postgres, Redis, Kafka"
```

---

## Task 7: Proto Extension — RFW Fields in ChatResponse

**Files:**
- Modify: `ino.flutter/protos/ino.proto`
- Modify: `iaw/Telegram/Protos/ino.proto` (must stay in sync)
- Regenerate: `ino.flutter/lib/grpc/generated/` (Dart protobuf codegen)

- [ ] **Step 1: Extend ChatResponse in proto**

In both `ino.flutter/protos/ino.proto` and `iaw/Telegram/Protos/ino.proto`, modify the `ChatResponse` message:

```protobuf
message ChatResponse {
  string reply = 1;
  string neuron_id = 2;
  bytes rfw_description = 3;
  bytes rfw_data = 4;
  string content_type = 5;  // "text", "rfw", "mixed"
}
```

- [ ] **Step 2: Regenerate Dart protobuf**

```bash
cd ino.flutter
dart run grpc:protoc_plugin --proto_path=protos --dart_out=lib/grpc/generated protos/ino.proto
```

Or use the project's existing protobuf generation command (check `ino.flutter/buf.yaml` or build scripts).

- [ ] **Step 3: Rebuild C# gRPC**

```bash
dotnet build iaw/Telegram/Telegram.csproj
```

The `.proto` in `iaw/Telegram/Protos/` should auto-generate via `Grpc.Tools` during build.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(proto): add rfw_description, rfw_data, content_type to ChatResponse"
```

---

## Task 8: InoService Travel Integration

**Files:**
- Modify: `iaw/Telegram/Services/InoService.cs`

The `Chat` RPC currently dispatches via `InoCommandDispatcher`. Modify it to route travel-related requests to the `TravelRecommenderNeuron` and return RFW content when available.

- [ ] **Step 1: Read the current InoService.Chat implementation**

Read `iaw/Telegram/Services/InoService.cs` to understand the current `Chat` method. It likely calls `InoCommandDispatcher.ExecuteScriptToStringAsync` or similar.

- [ ] **Step 2: Add travel neuron routing**

Modify the `Chat` method to use `AgentRegistry` for routing. When the registry routes to a travel neuron, the response should be returned with `ContentType = "rfw"` and the RFW bytes populated. For the initial implementation, route through `TravelRecommenderNeuron` which handles composition:

```csharp
public override async Task<ChatResponse> Chat(ChatRequest request, ServerCallContext context)
{
    var ct = context.CancellationToken;

    // route via the travel recommender for travel-related queries
    var recommender = _clusterClient.GetGrain<ITravelRecommender>("travel");
    var reply = await recommender.GetResponse(request.Message, ct);

    // check if reply contains structured results (JSON with type field)
    // if so, build RFW bytes from the template
    var response = new ChatResponse { Reply = reply, ContentType = "text" };

    // TODO: in a follow-up step, parse reply JSON and build RFW bytes
    // using templates from Ino.Travel.UI

    return response;
}
```

**Note:** The exact integration depends on how the current routing works. Read the existing `Chat` method, `InoCommandDispatcher`, and the agent routing mechanism to determine the cleanest integration point. The key is: travel queries reach `TravelRecommenderNeuron`, which calls other travel neurons, and the response flows back through gRPC.

- [ ] **Step 3: Verify build**

```bash
dotnet build iaw/Telegram/Telegram.csproj
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(travel): route chat to travel neurons via InoService"
```

---

## Task 9: RFW Templates (Server-Side C#)

**Files:**
- Create: `domains/travel/Ino.Travel/UI/FlightCardTemplate.cs`
- Create: `domains/travel/Ino.Travel/UI/HotelCardTemplate.cs`
- Create: `domains/travel/Ino.Travel/UI/PlaceCardTemplate.cs`
- Create: `domains/travel/Ino.Travel/UI/DestinationCardTemplate.cs`

RFW templates are C# functions that produce the two `bytes` fields: `rfw_description` (widget tree as RFW text format) and `rfw_data` (data values as JSON → RFW data format).

- [ ] **Step 1: Research RFW wire format**

Before writing templates, verify the RFW binary format via Context7. Look up `package:rfw` — specifically how `decodeLibraryBlob` and `decodeDataBlob` work on the Dart side, and what format the server needs to produce. The RFW text format can be encoded to binary via `encodeLibraryBlob` on the Dart side, but the server needs to produce equivalent bytes.

**Key question:** Does RFW have a canonical binary encoding, or should the server send the RFW text format as UTF-8 bytes and let the Dart client parse it? Check the `RemoteWidget` and `DynamicContent` classes.

- [ ] **Step 2: Create FlightCardTemplate**

Create `domains/travel/Ino.Travel/UI/FlightCardTemplate.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace Ino.Travel.UI;

public static class FlightCardTemplate
{
    public static (byte[] Description, byte[] Data) Build(JsonElement flight)
    {
        // RFW text format for the widget tree
        // This defines the layout — the Dart client's ino.flights library renders it
        var rfwText = """
            import ino.flights;
            widget root = FlightCard(
              airline: data.airline,
              from: data.from,
              to: data.to,
              price: data.price,
              departureTime: data.departureTime,
              arrivalTime: data.arrivalTime,
              duration: data.duration,
              stops: data.stops
            );
            """;

        // Data payload — values from the search result
        var data = JsonSerializer.Serialize(new
        {
            airline = flight.GetProperty("airline").GetString() ?? "Unknown",
            from = flight.GetProperty("departure_airport").GetString() ?? "",
            to = flight.GetProperty("arrival_airport").GetString() ?? "",
            price = flight.GetProperty("price").GetDecimal(),
            departureTime = flight.GetProperty("departure_time").GetString() ?? "",
            arrivalTime = flight.GetProperty("arrival_time").GetString() ?? "",
            duration = flight.GetProperty("duration").GetString() ?? "",
            stops = flight.GetProperty("stops").GetInt32()
        });

        return (Encoding.UTF8.GetBytes(rfwText), Encoding.UTF8.GetBytes(data));
    }

    public static (byte[] Description, byte[] Data) BuildList(JsonElement flights)
    {
        // build a Column of FlightCards
        var sb = new StringBuilder();
        sb.AppendLine("import ino.flights;");
        sb.AppendLine("widget root = Column(children: [");
        for (var i = 0; i < flights.GetArrayLength(); i++)
        {
            sb.AppendLine($"  FlightCard(");
            sb.AppendLine($"    airline: data.flights[{i}].airline,");
            sb.AppendLine($"    from: data.flights[{i}].from,");
            sb.AppendLine($"    to: data.flights[{i}].to,");
            sb.AppendLine($"    price: data.flights[{i}].price,");
            sb.AppendLine($"    departureTime: data.flights[{i}].departureTime,");
            sb.AppendLine($"    arrivalTime: data.flights[{i}].arrivalTime,");
            sb.AppendLine($"    duration: data.flights[{i}].duration,");
            sb.AppendLine($"    stops: data.flights[{i}].stops");
            sb.AppendLine($"  ),");
        }
        sb.AppendLine("]);");

        var dataList = new List<object>();
        for (var i = 0; i < flights.GetArrayLength(); i++)
        {
            var f = flights[i];
            dataList.Add(new
            {
                airline = f.GetProperty("airline").GetString() ?? "Unknown",
                from = f.GetProperty("departure_airport").GetString() ?? "",
                to = f.GetProperty("arrival_airport").GetString() ?? "",
                price = f.GetProperty("price").GetDecimal(),
                departureTime = f.GetProperty("departure_time").GetString() ?? "",
                arrivalTime = f.GetProperty("arrival_time").GetString() ?? "",
                duration = f.GetProperty("duration").GetString() ?? "",
                stops = f.GetProperty("stops").GetInt32()
            });
        }

        var data = JsonSerializer.Serialize(new { flights = dataList });
        return (Encoding.UTF8.GetBytes(sb.ToString()), Encoding.UTF8.GetBytes(data));
    }
}
```

**Important:** The exact JSON property names (`airline`, `departure_airport`, `price`, etc.) depend on what SerpApi returns and how `GetFlightResponseDTO` is structured. Read the actual DTO to get the right property names. The template above shows the structural pattern.

- [ ] **Step 3: Create HotelCardTemplate, PlaceCardTemplate, DestinationCardTemplate**

Follow the same pattern — each template produces RFW text + data bytes from the domain DTO. Adapt property names per DTO structure.

- [ ] **Step 4: Wire templates into neuron responses**

Update `FlightSearchNeuron.SearchFlights` to call `FlightCardTemplate.BuildList()` and return the bytes alongside the text reply. This requires the neuron to return structured data that `InoService` can split into `ChatResponse.Reply` + `ChatResponse.RfwDescription` + `ChatResponse.RfwData`.

One approach: have the neuron return a JSON envelope `{ "text": "...", "rfw_description": "<base64>", "rfw_data": "<base64>" }` and have `InoService` parse it.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(travel): RFW templates for flight, hotel, place, destination cards"
```

---

## Task 10: Flutter — Activate RFW in Chat Screen

**Files:**
- Modify: `ino.flutter/lib/screens/home/home_screen.dart`
- Modify: `ino.flutter/lib/state/ino_bloc.dart`
- Modify: `ino.flutter/lib/ui/ino_runtime.dart`

- [ ] **Step 1: Update ChatMessage model to carry RFW bytes**

In `ino.flutter/lib/state/ino_bloc.dart`, extend `ChatMessage`:

```dart
class ChatMessage {
  const ChatMessage({
    required this.text,
    required this.isUser,
    this.resultType,
    this.resultData,
    this.rfwDescription,
    this.rfwData,
    this.contentType,
  });
  final String text;
  final bool isUser;
  final String? resultType;
  final List<Map<String, dynamic>>? resultData;
  final List<int>? rfwDescription;
  final List<int>? rfwData;
  final String? contentType;

  bool get hasRfw => rfwDescription != null && rfwData != null;
}
```

- [ ] **Step 2: Update InoBloc._onMessageReceived to extract RFW**

In the `_onMessageReceived` handler, after receiving the `ChatResponse`, extract the new fields:

```dart
void _onMessageReceived(_MessageReceived event, Emitter<InoBlocState> emit) {
  final reply = event.reply;
  final rfwDesc = event.rfwDescription;
  final rfwData = event.rfwData;
  final contentType = event.contentType;

  // existing JSON parsing for backwards compatibility...
  String? resultType;
  List<Map<String, dynamic>>? resultData;
  // ... existing parsing logic ...

  final message = ChatMessage(
    text: reply,
    isUser: false,
    resultType: resultType,
    resultData: resultData,
    rfwDescription: rfwDesc,
    rfwData: rfwData,
    contentType: contentType,
  );

  emit(state.copyWith(
    messages: [...state.messages, message],
    isLoading: false,
  ));
}
```

Update `_MessageReceived` event to carry the new fields from `ChatResponse`.

- [ ] **Step 3: Add RFW rendering to home_screen.dart**

In `_ChatBubble.build()`, add RFW rendering when the message has RFW content:

```dart
@override
Widget build(BuildContext context) {
  if (message.hasRfw) {
    return _RfwContent(
      rfwDescription: message.rfwDescription!,
      rfwData: message.rfwData!,
    );
  }
  // ... existing text + result card rendering ...
}
```

Create the `_RfwContent` widget:

```dart
class _RfwContent extends StatelessWidget {
  const _RfwContent({required this.rfwDescription, required this.rfwData});
  final List<int> rfwDescription;
  final List<int> rfwData;

  @override
  Widget build(BuildContext context) {
    final runtime = createInoRuntime();
    // parse RFW text format from bytes
    final descText = utf8.decode(rfwDescription);
    final dataText = utf8.decode(rfwData);

    // use RemoteWidget to render
    // exact API depends on RFW version — verify via Context7
    return RemoteWidget(
      runtime: runtime,
      widget: const FullyQualifiedWidgetName(
        LibraryName(<String>['main']),
        'root',
      ),
      data: DynamicContent()..update('data', jsonDecode(dataText)),
    );
  }
}
```

**Important:** The exact RFW rendering API (`RemoteWidget`, `DynamicContent`, `parseLibraryFile`) must be verified against the `rfw: ^1.1.3` package docs via Context7. The code above shows the intent — adapt to the actual API.

- [ ] **Step 4: Flutter build and test**

```bash
cd ino.flutter
flutter build web
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(flutter): activate RFW rendering in chat screen for server-driven travel UI"
```

---

## Task 11: Rive Persona

**Files:**
- Create: `ino.flutter/assets/rive/persona.riv` (created in Rive Editor — manual step)
- Modify: `ino.flutter/lib/persona/persona_widget.dart`
- Modify: `ino.flutter/lib/persona/persona_state.dart`
- Modify: `ino.flutter/pubspec.yaml` (add rive asset)

- [ ] **Step 1: Add Rive asset to pubspec.yaml**

In `ino.flutter/pubspec.yaml`, add to the flutter assets section:

```yaml
flutter:
  assets:
    - assets/rive/persona.riv
```

- [ ] **Step 2: Create placeholder Rive file**

Until the real `.riv` is designed in Rive Editor, create a code-based Rive approach. For now, enhance the existing `CustomPaint` persona to respond to the new states. The full Rive state machine will be created in the Rive Editor as a manual design step.

Update `ino.flutter/lib/persona/persona_state.dart` to add the new states:

```dart
enum PersonaEmotion {
  sleeping,
  waking,
  idle,
  listening,
  thinking,
  searching,   // NEW — external API calls
  acting,      // EXISTING
  presenting,  // NEW — results ready, making space
  responding,  // EXISTING (keep for backwards compat)
  celebrating, // EXISTING
  confused,    // EXISTING
  evolving,    // EXISTING
}

class PersonaStateModel {
  const PersonaStateModel({
    this.emotion = PersonaEmotion.sleeping,
    this.energy = 0.0,
    this.confidence = 0.0,
    this.neuronCount = 0,
    this.synapseRate = 0.0,
    this.domainAffinity = const {},
  });

  final PersonaEmotion emotion;
  final double energy;
  final double confidence;
  final int neuronCount;        // NEW
  final double synapseRate;     // NEW
  final Map<String, double> domainAffinity;

  PersonaStateModel copyWith({...}) => ...;
}
```

- [ ] **Step 3: Enhance CustomPaint persona with new states**

Update `ino.flutter/lib/persona/persona_widget.dart` to render the new states (searching = radar pulse, presenting = concave opening, etc.) using the existing `CustomPaint` approach. Add:

- `searching` state: expanding ring pattern, outward radial pulse
- `presenting` state: shape opens concave, settles downward
- `neuronCount` driving visible orbiting dots around the persona
- `synapseRate` modulating the pulse frequency

```dart
// In _PersonaPainter.paint():
// Add orbiting neuron dots
for (var i = 0; i < neuronCount; i++) {
  final angle = (2 * pi * i / neuronCount) + phase;
  final orbitRadius = baseRadius * 1.5;
  final dotCenter = Offset(
    center.dx + orbitRadius * cos(angle),
    center.dy + orbitRadius * sin(angle),
  );
  canvas.drawCircle(dotCenter, 3, Paint()..color = color.withOpacity(0.6));
}

// Searching state: concentric expanding rings
if (emotion == PersonaEmotion.searching) {
  for (var ring = 0; ring < 3; ring++) {
    final ringRadius = baseRadius * (1.2 + ring * 0.3 + animationValue * 0.5);
    final ringOpacity = (1.0 - (animationValue + ring * 0.3).clamp(0, 1)) * 0.3;
    canvas.drawCircle(
      center,
      ringRadius,
      Paint()
        ..color = color.withOpacity(ringOpacity)
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.5,
    );
  }
}
```

- [ ] **Step 4: Wire persona to real timeline events**

Update `ino.flutter/lib/state/persona_bloc.dart` to subscribe to timeline events and derive `neuronCount` and `synapseRate`:

```dart
// In PersonaBloc, add timeline event tracking
final Set<String> _activeNeurons = {};
int _synapseCount = 0;
DateTime _windowStart = DateTime.now();

void _onTimelineEvent(TimelineEvent event) {
  _activeNeurons.add(event.source);
  _synapseCount++;

  final elapsed = DateTime.now().difference(_windowStart).inSeconds;
  final rate = elapsed > 0 ? _synapseCount / elapsed : 0.0;

  add(PersonaUpdated(state.copyWith(
    neuronCount: _activeNeurons.length,
    synapseRate: rate,
    energy: (rate / 5.0).clamp(0.0, 1.0),
  )));

  // reset window every 30 seconds
  if (elapsed > 30) {
    _activeNeurons.clear();
    _synapseCount = 0;
    _windowStart = DateTime.now();
  }
}
```

Subscribe the persona bloc to the timeline stream in `main.dart` or wire through the existing `TimelineBloc`.

- [ ] **Step 5: Implement persona sizing based on chat state**

In `home_screen.dart`, make persona size respond to message count:

```dart
final hasMessages = state.messages.isNotEmpty;
final personaSize = hasMessages ? 120.0 : 250.0;

AnimatedContainer(
  duration: const Duration(milliseconds: 600),
  curve: Curves.easeOutCubic,
  height: personaSize,
  width: personaSize,
  child: PersonaWidget(size: personaSize),
)
```

- [ ] **Step 6: Flutter build**

```bash
cd ino.flutter
flutter build web
```

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(flutter): enhanced persona with searching/presenting states, neuron dots, timeline-driven animation"
```

---

## Task 12: Progressive Chat Rendering

**Files:**
- Modify: `ino.flutter/lib/screens/home/home_screen.dart`
- Create: `ino.flutter/lib/ui/components/skeleton_card.dart`

- [ ] **Step 1: Create SkeletonCard widget**

Create `ino.flutter/lib/ui/components/skeleton_card.dart`:

```dart
import 'package:flutter/material.dart';

class SkeletonCard extends StatefulWidget {
  const SkeletonCard({super.key});

  @override
  State<SkeletonCard> createState() => _SkeletonCardState();
}

class _SkeletonCardState extends State<SkeletonCard>
    with SingleTickerProviderStateMixin {
  late final AnimationController _shimmer;

  @override
  void initState() {
    super.initState();
    _shimmer = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1500),
    )..repeat();
  }

  @override
  void dispose() {
    _shimmer.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _shimmer,
      builder: (context, child) {
        return Container(
          margin: const EdgeInsets.symmetric(vertical: 4, horizontal: 16),
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(12),
            gradient: LinearGradient(
              begin: Alignment(-1.0 + 2.0 * _shimmer.value, 0),
              end: Alignment(-1.0 + 2.0 * _shimmer.value + 1.0, 0),
              colors: [
                Colors.white.withOpacity(0.05),
                Colors.white.withOpacity(0.1),
                Colors.white.withOpacity(0.05),
              ],
            ),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _shimmerBar(width: 120, height: 14),
              const SizedBox(height: 12),
              _shimmerBar(width: 200, height: 10),
              const SizedBox(height: 8),
              _shimmerBar(width: 160, height: 10),
              const SizedBox(height: 12),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  _shimmerBar(width: 80, height: 20),
                  _shimmerBar(width: 60, height: 20),
                ],
              ),
            ],
          ),
        );
      },
    );
  }

  Widget _shimmerBar({required double width, required double height}) {
    return Container(
      width: width,
      height: height,
      decoration: BoxDecoration(
        color: Colors.white.withOpacity(0.08),
        borderRadius: BorderRadius.circular(4),
      ),
    );
  }
}
```

- [ ] **Step 2: Show skeletons while loading**

In `home_screen.dart`, when `state.isLoading` is true after sending a message, show skeleton cards:

```dart
// In the message list builder, after the last message:
if (state.isLoading) ...[
  const SkeletonCard(),
  const SkeletonCard(),
  const SkeletonCard(),
],
```

- [ ] **Step 3: Animate skeleton → real card transition**

Use `AnimatedSwitcher` to crossfade from skeleton to real content when the response arrives:

```dart
AnimatedSwitcher(
  duration: const Duration(milliseconds: 400),
  child: state.isLoading
    ? Column(
        key: const ValueKey('skeleton'),
        children: List.generate(3, (_) => const SkeletonCard()),
      )
    : const SizedBox.shrink(key: ValueKey('loaded')),
)
```

- [ ] **Step 4: Flutter build**

```bash
cd ino.flutter
flutter build web
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(flutter): skeleton shimmer cards with crossfade transition during search"
```

---

## Task 13: BDD Tests — Gherkin Per Neuron

**Files:**
- Create: `domains/travel/Ino.Travel.Tests/Ino.Travel.Tests.csproj`
- Create: `domains/travel/Ino.Travel.Tests/Features/FlightSearch.feature`
- Create: `domains/travel/Ino.Travel.Tests/Features/HotelSearch.feature`
- Create: `domains/travel/Ino.Travel.Tests/Features/PlaceDiscovery.feature`
- Create: `domains/travel/Ino.Travel.Tests/Features/PriceTracker.feature`
- Create: `domains/travel/Ino.Travel.Tests/Steps/FlightSearchSteps.cs`
- Create: `domains/travel/Ino.Travel.Tests/Steps/HotelSearchSteps.cs`
- Create: `domains/travel/Ino.Travel.Tests/Steps/PlaceDiscoverySteps.cs`
- Create: `domains/travel/Ino.Travel.Tests/Steps/PriceTrackerSteps.cs`
- Create: `domains/travel/Ino.Travel.Tests/Scenarios/FlightSearchScenarioTests.cs`
- Create: `domains/travel/Ino.Travel.Tests/TravelTestFixture.cs`

Per the project convention (feedback_gherkin_for_neurons): `.feature` is the canonical Gherkin contract, step definitions in `Steps/`, xUnit `[Fact]` methods invoke steps in order (Reqnroll xunit.v3 incompatibility workaround).

- [ ] **Step 1: Create test project**

Create `domains/travel/Ino.Travel.Tests/Ino.Travel.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Ino.Travel\Ino.Travel.csproj" />
    <ProjectReference Include="..\..\..\iaw\Testing\Testing.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>

  <ItemGroup>
    <None Update="Features\*.feature" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create TravelTestFixture**

Create `domains/travel/Ino.Travel.Tests/TravelTestFixture.cs` — shared TestCluster setup with travel DI registered:

```csharp
using IAW.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ino.Travel.Tests;

public class TravelTestFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TravelSiloConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Cluster.StopAllSilosAsync();
    }

    private class TravelSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.ConfigureServices(services =>
            {
                services.AddTravelDomain();
                // register mock SerpApi for tests
                // register mock DbContext for tests
            });
        }
    }
}
```

**Important:** Read `iaw/Testing/` to understand the existing `AgentTest<T>` base class and `TestCluster` setup. The fixture should follow the same patterns. Mock the SerpApi provider to avoid real API calls in tests.

- [ ] **Step 3: Create FlightSearch.feature**

Create `domains/travel/Ino.Travel.Tests/Features/FlightSearch.feature`:

```gherkin
Feature: Flight Search Neuron

  Scenario: Search flights returns results
    Given a registered user
    When I ask FlightSearch to search flights from "NYC" to "DPS" on "2026-07-15"
    Then I receive flight results
    And the results contain at least 1 flight

  Scenario: Price calendar returns monthly prices
    Given a registered user
    When I ask FlightSearch for a price calendar from "NYC" to "DPS" for 3 months
    Then I receive a price calendar
    And the calendar contains entries for 3 months

  Scenario: Explore destinations returns options
    Given a registered user
    When I ask FlightSearch to explore destinations from "JFK"
    Then I receive destination suggestions
```

- [ ] **Step 4: Create FlightSearchSteps.cs**

Create `domains/travel/Ino.Travel.Tests/Steps/FlightSearchSteps.cs`:

```csharp
using Ino.Travel.Neurons;

namespace Ino.Travel.Tests.Steps;

public class FlightSearchSteps(TravelTestFixture fixture)
{
    private readonly IFlightSearch _neuron = fixture.Cluster.GrainFactory
        .GetGrain<IFlightSearch>("test-flight-search");
    private string? _result;

    public async Task GivenARegisteredUser()
    {
        // setup test user context if needed
    }

    public async Task WhenISearchFlights(string from, string to, string date)
    {
        _result = await _neuron.SearchFlights(from, to, date, null, CancellationToken.None);
    }

    public async Task WhenIGetPriceCalendar(string from, string to, int months)
    {
        _result = await _neuron.GetPriceCalendar(from, to, months, CancellationToken.None);
    }

    public async Task WhenIExploreDestinations(string from)
    {
        _result = await _neuron.ExploreDestinations(from, CancellationToken.None);
    }

    public void ThenIReceiveFlightResults()
    {
        Assert.NotNull(_result);
        Assert.Contains("flight_results", _result);
    }

    public void ThenResultsContainAtLeast(int count)
    {
        // parse JSON and verify count
        Assert.NotNull(_result);
    }

    public void ThenIReceivePriceCalendar()
    {
        Assert.NotNull(_result);
        Assert.Contains("price_calendar", _result);
    }

    public void ThenIReceiveDestinationSuggestions()
    {
        Assert.NotNull(_result);
        Assert.Contains("destination_results", _result);
    }
}
```

- [ ] **Step 5: Create FlightSearchScenarioTests.cs**

Create `domains/travel/Ino.Travel.Tests/Scenarios/FlightSearchScenarioTests.cs`:

```csharp
using Ino.Travel.Tests.Steps;

namespace Ino.Travel.Tests.Scenarios;

public class FlightSearchScenarioTests(TravelTestFixture fixture)
    : IClassFixture<TravelTestFixture>
{
    [Fact]
    public async Task SearchFlightsReturnsResults()
    {
        var steps = new FlightSearchSteps(fixture);
        await steps.GivenARegisteredUser();
        await steps.WhenISearchFlights("NYC", "DPS", "2026-07-15");
        steps.ThenIReceiveFlightResults();
        steps.ThenResultsContainAtLeast(1);
    }

    [Fact]
    public async Task PriceCalendarReturnsMonthlyPrices()
    {
        var steps = new FlightSearchSteps(fixture);
        await steps.GivenARegisteredUser();
        await steps.WhenIGetPriceCalendar("NYC", "DPS", 3);
        steps.ThenIReceivePriceCalendar();
    }

    [Fact]
    public async Task ExploreDestinationsReturnsOptions()
    {
        var steps = new FlightSearchSteps(fixture);
        await steps.GivenARegisteredUser();
        await steps.WhenIExploreDestinations("JFK");
        steps.ThenIReceiveDestinationSuggestions();
    }
}
```

- [ ] **Step 6: Create HotelSearch.feature + steps + scenarios**

Follow the same pattern for HotelSearchNeuron.

- [ ] **Step 7: Create PlaceDiscovery.feature + steps + scenarios**

Follow the same pattern for PlaceDiscoveryNeuron.

- [ ] **Step 8: Create PriceTracker.feature + steps + scenarios**

Follow the same pattern for PriceTrackerNeuron. Test `TrackFlight`, `GetTrackedPrices`, `StopTracking`.

- [ ] **Step 9: Add test project to solution**

Add `domains/travel/Ino.Travel.Tests/Ino.Travel.Tests.csproj` to `ino.slnx`.

- [ ] **Step 10: Run tests**

```bash
dotnet test domains/travel/Ino.Travel.Tests/Ino.Travel.Tests.csproj -v normal
```

Expected: tests pass with mock SerpApi provider.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "test(travel): BDD tests for FlightSearch, HotelSearch, PlaceDiscovery, PriceTracker neurons"
```

---

## Task 14: E2E Integration Test

**Files:**
- Modify: `test/E2E.Tests/` (if exists) or create new E2E test

- [ ] **Step 1: Build full solution**

```bash
dotnet build ino.slnx
```

Expected: everything compiles — ino core + travel domain + tests.

- [ ] **Step 2: Run all tests**

```bash
dotnet test ino.slnx -v normal
```

Expected: all existing tests pass + new travel neuron tests pass.

- [ ] **Step 3: Start Aspire and verify travel domain resources**

```bash
aspire start
```

Check dashboard at `https://localhost:17280`:
- `assistant` silo: Healthy (travel neurons loaded)
- `travel-migrations`: Completed (EF migrations ran)
- `postgres` / `travel` database: Running
- `redis`: Running
- `kafka`: Running
- `telegram`: Healthy

- [ ] **Step 4: Test via MCP**

Use `mcp__iaw__assistant_chat` to send a travel query:

```
"find flights from NYC to Bali in July"
```

Verify:
- Response contains flight results
- Timeline shows neuron activation events
- Persona state changes (via `StreamPersonaState`)

- [ ] **Step 5: Test Flutter web**

Open `http://localhost:<telegram-port>/` in browser.
- Persona should be visible and animated
- Type a travel query in chat
- Verify skeleton cards appear during search
- Verify results render (either as RFW or fallback hardcoded cards)

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "test(travel): E2E integration verified — travel neurons + persona + Flutter"
```

- [ ] **Step 7: Stop Aspire**

```bash
aspire stop
```
