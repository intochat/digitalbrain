# Travel Domain — First App Installed on ino

**Date:** 2026-04-11
**Status:** Approved
**Scope:** TripRadar absorption into ino as the first out-of-box domain experience, Rive persona, RFW server-driven UI

## Vision

ino is an AI-native operating system. TripRadar is the first application installed on it — like Safari on macOS. The travel domain ships out-of-box as a polished experience that proves the neuron/synapse architecture is useful in real life. Users install ino, authenticate via Google, and immediately have an AI travel assistant that searches flights, tracks prices, discovers places, and remembers preferences — all through the neuron/synapse primitives.

The persona is THE feature. It's a real-time neural activity monitor rendered as a living entity. Every morph, pulse, and color shift maps to something actually happening in the architecture. Nobody has done this because nobody has the architecture underneath to drive it.

## Project Structure

```
E:\ino\
├── iaw\                          # ino kernel (unchanged)
├── ino.flutter\                  # Flutter client
├── domains\
│   └── travel\
│       ├── Ino.Travel\           # Class library — neurons + Aspire wiring
│       │   ├── Neurons\          # Travel neuron grain implementations
│       │   ├── Hosting\          # AddTravelDomain() / WithTravelDomain() extensions
│       │   ├── UI\               # RFW template builders (C# → RFW bytes)
│       │   └── Ino.Travel.csproj # References TripRadar projects
│       └── TripRadar\            # Existing TripRadar source (moved from root)
│           └── src\
│               ├── TripRadar.Server.Domain\
│               ├── TripRadar.Server.Application\
│               ├── TripRadar.Server.Infrastructure\
│               ├── TripRadar.Server.Db\
│               ├── TripRadar.Server.Comms.Core\
│               ├── TripRadar.Server.API.Contracts\
│               ├── TripRadar.Server.API\
│               └── ... (all projects stay intact, nothing deleted)
```

**TripRadar source stays completely intact.** No files deleted, no billing code removed, no projects pruned. The `Ino.Travel` class library references the projects it needs. Unused projects (Bot, MiniApp, WebUI, Jobs.API, standalone Aspire) remain in the codebase but are not wired into ino's AppHost.

## Neurons

Hybrid granularity — one fat neuron for user identity plumbing, six focused neurons for travel intelligence.

### UserNeuron (infrastructure, L3)

| Interface | `IUser` |
|---|---|
| Wraps | Google OAuth login, user profile, preferences |
| Tools | `Authenticate`, `GetProfile`, `UpdatePreferences` |
| Notes | Delegates to TripRadar's `GoogleLoginCommandHandler`, `UpdateUserProfileCommandHandler`, etc. via MediatR. Slim — no billing, no tiers, no token tracking. |

### FlightSearchNeuron (travel intelligence)

| Interface | `IFlightSearch` |
|---|---|
| Wraps | `GetFlightsQueryHandler`, `GetFlightPriceCalendarQueryHandler`, `GetFlightNearbyPricesQueryHandler`, `GetFlightExploreQueryHandler` |
| Tools | `SearchFlights`, `GetPriceCalendar`, `ExploreDestinations`, `GetNearbyPrices` |
| RFW output | `FlightCard`, `PriceCalendar` |

### HotelSearchNeuron (travel intelligence)

| Interface | `IHotelSearch` |
|---|---|
| Wraps | `GetHotelsQueryHandler` |
| Tools | `SearchHotels` |
| RFW output | `HotelCard` |

### PlaceDiscoveryNeuron (travel intelligence)

| Interface | `IPlaceDiscovery` |
|---|---|
| Wraps | `GetLocalPlacesQueryHandler`, `GetEventsQueryHandler` |
| Tools | `FindPlaces`, `FindEvents` |
| RFW output | `PlaceCard` |

### PriceTrackerNeuron (travel intelligence)

| Interface | `IPriceTracker` |
|---|---|
| Wraps | `CreateScheduledFlightQueryHandler`, `CreateScheduledHotelQueryHandler`, Kafka consumer → `PriceDeltaCalculator` |
| Tools | `TrackFlight`, `TrackHotel`, `GetTrackedPrices`, `StopTracking` |
| RFW output | `PriceDropCard`, `TrackedFlightBadge` |
| Scheduling | Uses Orleans `ScheduleRecurringJob` instead of Hangfire |

### TripVaultNeuron (travel intelligence)

| Interface | `ITripVault` |
|---|---|
| Wraps | `CreateTripVaultCommandHandler`, `UpdateTripVaultCommandHandler`, `DeleteTripVaultCommandHandler`, `RemoveTripItemCommandHandler` |
| Tools | `SaveTrip`, `GetSavedTrips`, `RemoveTrip` |
| RFW output | `TripSummary` |

### TravelRecommenderNeuron (travel intelligence, NEW logic)

| Interface | `ITravelRecommender` |
|---|---|
| Wraps | No existing handler — LLM-powered composition |
| Tools | `RecommendDestinations`, `PlanTrip`, `SuggestAlternatives` |
| RFW output | `DestinationCard`, `TripSummary` |
| Notes | Uses other travel neurons as tools. The LLM reasons about what to search, composes results, synthesizes recommendations. This is the "brain" of the travel experience. |

### Neuron implementation pattern

```csharp
public interface IFlightSearch : IAgent
{
    static string AgentDisplayName => "Flight Search";
    static string AgentDescription => "Searches flights via SerpApi, price calendars, explore destinations";
    static string[] AgentCapabilities => ["flights", "search", "travel", "prices"];
    static string AgentInstructions => "You are ino's flight search specialist...";
    static string[] AgentRoutingExamples => [
        "find flights to Bali",
        "cheapest flights NYC to LAX next Friday",
        "show me the price calendar for Tokyo in June"
    ];

    Task<FlightSearchResult> SearchFlights(string from, string to,
        DateOnly departure, DateOnly? returnDate, CancellationToken ct);
    Task<PriceCalendarResult> GetPriceCalendar(string from, string to,
        int monthsAhead, CancellationToken ct);
    Task<FlightExploreResult> ExploreDestinations(string from,
        string region, CancellationToken ct);
}

public class FlightSearchNeuron(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt54Nano>] IChatClient chatClient,
    IMediator mediator)
    : Agent<IFlightSearch>(durableState, chatClient), IFlightSearch
{
    // Interface methods auto-discovered as tools
    // Each delegates to existing MediatR handlers
    // Results returned as RFW bytes via FlightCardTemplate.Build()
}
```

### Synapse flow example

```
User: "find cheap flights to Bali"
  → ino routes to TravelRecommenderNeuron
  → TravelRecommender fires synapse to FlightSearchNeuron
    (verb: "search_flights", args: {to: "Bali"})
  → FlightSearch calls MediatR GetFlightsQueryHandler → SerpApi
  → FlightSearch fires synapse back
    (verb: "flight_results", decay: 100, payload: results)
  → TravelRecommender fires synapse to PriceTrackerNeuron
    (verb: "track_flight", args: {route, price})
  → TravelRecommender composes RFW response (FlightCards + text)
  → Timeline captures all synapse fires
  → Persona shifts: thinking → searching → acting → presenting → celebrating
```

## Persona — The Feature

The persona is a real-time projection of neural activity rendered as a living entity via Rive animation. It is not decoration — it directly reflects what the architecture is doing.

### Rive state machine

One `.riv` file, one artboard, one state machine. Inputs driven by gRPC `StreamPersonaState` + `StreamEvents`:

| Input | Type | Source |
|---|---|---|
| `emotion` | enum (10 states) | `PersonaState.emotion` |
| `energy` | float 0-1 | `PersonaState.energy` |
| `confidence` | float 0-1 | `PersonaState.confidence` |
| `neuronCount` | int | Active neuron count from event stream |
| `synapseRate` | float | Synapses/sec from event stream |
| `domainColor` | color | Domain affinity (travel = warm tones) |

### The 10 persona states

| State | Shape | Motion | Color | When |
|---|---|---|---|---|
| `sleeping` | Compact sphere | Slow breath (scale 0.98↔1.02) | Deep indigo | Idle timeout |
| `waking` | Sphere unfolds petals | Opening bloom, 2s | Indigo → purple | First interaction |
| `idle` | Soft amoeba | Floating drift, sine waves | Soft teal | Waiting |
| `listening` | Leans toward input | Subtle pull toward bottom | Warm white | User typing |
| `thinking` | Rotating internal core | Slow rotation, inner particles | Amber/orange | LLM reasoning |
| `searching` | Radar/scanning sweep | Radial pulse outward | Blue-white | External API calls |
| `acting` | Multiple tendrils extend | Tips glow on synapse fire | Electric blue | Multiple neurons active |
| `presenting` | Opens up, concave | Settles, clears space for content | Soft green | Results ready |
| `celebrating` | Burst, particles scatter/reform | Firework expansion, gentle return | Bright green/gold | Success |
| `evolving` | Fractal growth, new geometry | New vertices appear and integrate | Purple/violet | L1 neuron created |

### Transitions

No jump-cuts. Rive state machine blends:

- `idle → listening`: 300ms, tilt toward input
- `listening → thinking`: 200ms, tilt converts to rotation
- `thinking → searching`: 400ms, rotation becomes outward pulse
- `searching → acting`: instant, tendrils sprout per neuron
- `acting → presenting`: 600ms, tendrils retract, shape opens
- `presenting → celebrating`: 300ms, brief expansion
- `celebrating → idle`: 1.5s, slow settle

### Architecture-driven, not scripted

The animations respond to real event counts and rates:

```dart
void _onTimelineEvent(TimelineEvent event) {
  _activeNeurons.add(event.source);
  _synapseCount++;
  emit(state.copyWith(
    neuronCount: _activeNeurons.length,
    synapseRate: _synapseCount / _elapsed.inSeconds,
    energy: min(1.0, _synapseRate / 5.0),
  ));
}
```

If a search fires 2 synapses, gentle pulse. If a trip plan fires 15 synapses across 6 neurons, storm of activity with tendrils everywhere. Visual complexity = computational complexity.

### Persona sizing

| Chat state | Size | Position |
|---|---|---|
| Empty (just launched) | 250px, centered | Center of screen |
| First message sent | 250px → 120px | Slides to top |
| Results rendering | 80px | Top-left, minimal |
| Idle in conversation | 120px | Top center |
| Celebrating | 120px → 160px → 120px | Brief expansion in place |

## RFW Component Library — Server-Driven Travel UI

Neurons compose pre-designed RFW templates with data and push to Flutter. The server controls the UI, Flutter is the rendering surface.

### Components

| Component | Neuron source | Visual |
|---|---|---|
| `FlightCard` | FlightSearchNeuron | Airline, route, times, duration, stops, price badge. Expandable: fare class, baggage, layovers |
| `HotelCard` | HotelSearchNeuron | Hero image, name, stars, location, price/night, amenity chips. Expandable: rooms, reviews |
| `PlaceCard` | PlaceDiscoveryNeuron | Photo, name, category, rating, reviews, distance. Expandable: description, hours |
| `PriceDropCard` | PriceTrackerNeuron | Old price strikethrough → new price, savings, trend sparkline, "Book" CTA |
| `PriceCalendar` | FlightSearchNeuron | Month grid, cells colored by price (green↔red), cheapest highlighted |
| `DestinationCard` | TravelRecommenderNeuron | Photo, weather icon + temp, ballpark price, "Explore" button |
| `TripSummary` | TravelRecommenderNeuron | Itinerary: flight + hotel + places by day, total cost, "Save" button |
| `TrackedFlightBadge` | PriceTrackerNeuron | Compact: route, price, trend arrow, next check |
| `SkeletonCard` | All neurons | Shimmer placeholder, crossfades to real card |

### Server-side template architecture

```
domains/travel/Ino.Travel/UI/
├── FlightCardTemplate.cs     # FlightResult → RFW bytes
├── HotelCardTemplate.cs
├── PlaceCardTemplate.cs
├── PriceDropCardTemplate.cs
├── PriceCalendarTemplate.cs
├── DestinationCardTemplate.cs
├── TripSummaryTemplate.cs
└── SkeletonTemplate.cs
```

Each template: pure function, domain data in, RFW bytes out. Widget tree structure defined once, data changes per invocation.

### Rendering flow

```
Server (C#):                          Client (Dart):

Neuron calls MediatR handler          InoBloc receives ChatResponse
  → gets domain result                  → detects rfw_content bytes
  → Template.Build(result)              → passes to ino_runtime
  → RFW bytes in ChatResponse           → renders pre-designed cards
```

### Progressive chat rendering

```
t=0s    User sends message
t=0.1s  SkeletonCard × 3 (instant shimmer placeholders)
t=2-4s  FlightCard × 3 (crossfade replaces skeletons)
t=4s    Text: "The $420 direct flight looks best..."
t=4.5s  TrackedFlightBadge (after "track it")
```

### RFW callbacks

Cards are interactive. "Explore" on DestinationCard, "Book" on PriceDropCard → `HandleCallback(callbackId, value)` → routes to originating neuron → returns new RFW or text. The `HandleCallback` RPC already exists on `IAgent`.

### Proto extension

`ChatResponse` currently has `string reply`. Extend with RFW support:

```protobuf
message ChatResponse {
  string reply = 1;              // text content (existing)
  bytes rfw_description = 2;     // RFW widget tree (new)
  bytes rfw_data = 3;            // RFW data payload (new)
  string content_type = 4;       // "text", "rfw", "mixed" (new)
}
```

When `content_type` is `"rfw"`, the client renders via `ino_runtime`. When `"mixed"`, the client renders both text and RFW cards. When `"text"` or empty, plain text as before.

### Flutter client changes

1. Chat screen: detect RFW bytes in `ChatResponse`, render through `ino_runtime` instead of hardcoded cards
2. New RFW libraries: register `ino.pricedrop`, `ino.calendar`, `ino.destination`, `ino.trip` alongside existing 4
3. Skeleton shimmer: Flutter-side component (needs animation, not RFW)
4. Progressive message handling: chat bubbles as stream, each text or RFW

## Aspire Wiring

### AppHost topology

```csharp
// iaw/Aspire/AppHost.cs

var postgres = builder.AddPostgres("postgres").WithPgAdmin().WithDataVolume();
var travelDb = postgres.AddDatabase("travel");
var redis = builder.AddRedis("redis");
var kafka = builder.AddKafka("kafka").WithKafkaUI();

var travel = builder.AddTravelDomain(travelDb, redis, kafka);

var iaw = builder.AddIAW("iaw")
    .WithLLM<OpenAIModels.Gpt54Nano>().AsFast()
    .WithLLM<OpenAIModels.Gpt54Mini>().AsBalanced()
    .WithLLM<OpenAIModels.Gpt54>().AsReasoning()
    .WithEmbedding<OpenAIModels.TextEmbedding3Small>()
    .WithTravelDomain(travel);

var telegram = builder.AddTelegram("telegram")
    .WithReference(iaw)
    .WithReference(travelDb)
    .WithNgrokTunnel();
```

### AddTravelDomain()

Registers Aspire parameters for secrets (SerpApi key, Google OAuth), adds EF migrations runner project, returns `TravelDomainResources` handle.

### WithTravelDomain()

Adds `Ino.Travel` assembly to silo grain scanning, registers TripRadar DI (DbContext, MediatR, SerpApi provider), wires connection strings, registers Kafka consumer. Travel neurons auto-discovered by `AgentRegistrationStartupTask`.

### Resource map

| Resource | Type | Purpose |
|---|---|---|
| `postgres` / `travel` | PostgreSQL | TripRadar's full schema (users, flights, hotels, reference data) |
| `redis` | Redis | Session cache |
| `kafka` | Kafka | Price alert events |
| `travel-migrations` | Console | EF Core migrations on startup |
| `assistant` | Orleans silo | All neurons (core + travel) |
| `telegram` | ASP.NET | Bot + gRPC + Flutter web + Kafka consumer |
| `mcp` | MCP server | Claude Code integration |
| `ino-flutter` | Flutter Windows | Desktop client (explicit start) |

## E2E User Experience

### First launch

1. User installs ino (Flutter desktop or opens web)
2. Onboarding screen → Google auth → `UserNeuron.Authenticate()` → TripRadar's `GoogleLoginCommandHandler` → user created in Postgres
3. Persona wakes: `sleeping → waking → idle`
4. Chat screen, persona centered large: "Hi, I'm ino. I can help you plan travel, track prices, and discover places."

### First conversation: "I want to go somewhere warm in July"

```
t=0s    User types → Persona: listening
t=0.2s  Routes to TravelRecommenderNeuron → Persona: thinking
t=0.5s  TravelRecommender → PlaceDiscoveryNeuron (explore warm destinations)
        Persona: searching, skeleton cards shimmer
t=2s    PlaceDiscovery returns 4 destinations
        TravelRecommender → FlightSearchNeuron × 4 (ballpark pricing)
        Persona: acting (4 tendrils)
t=4s    Results coalesce → Persona: presenting → celebrating
        RFW: 4 DestinationCards with photos, weather, prices
        "Bali is cheapest, Lisbon has the best weather. Explore any of these?"
```

### Deep dive: user taps "Explore" on Bali

```
t=0s    RFW callback → Persona: acting
t=0.5s  Parallel synapses: FlightSearch + HotelSearch + PlaceDiscovery
t=2-4s  Progressive rendering: FlightCards, HotelCards, PlaceCards
        Persona: found → presenting
t=4s    "Bali in July: flights from $420, hotels from $35/night.
        Track prices on that $420 flight?"
```

### Proactive: price drop alert

```
        PriceTrackerNeuron recurring job fires
        → Price dropped $420 → $380
        → Notification (Telegram push + Flutter)
        User opens ino:
        Persona: celebrating
        RFW: PriceDropCard with sparkline, savings, "Book" CTA
```

### Synapse memory via decay

After the session, synapses persist with decay:
- "User interested in warm destinations" (decay: 80)
- "User prefers Bali" (decay: 100)
- "NYC is departure city" (decay: 90, inferred)
- "User tracks prices before booking" (decay: 85, behavioral)

Next session, ino starts from NYC, suggests warm destinations, offers tracking. No explicit preference model — the synapses ARE the memory.

## BDD Tests

One `.feature` per neuron, one scenario per synapse verb. Tests run via `dotnet test` with TestCluster.

```gherkin
# domains/travel/Ino.Travel.Tests/Features/FlightSearch.feature

Feature: Flight Search Neuron

Scenario: Search flights returns results
  Given a registered user
  When I fire a synapse to FlightSearch with verb "search_flights"
    | from | to  | departure  |
    | NYC  | DPS | 2026-07-15 |
  Then I receive a synapse with verb "flight_results"
  And the payload contains at least 1 flight
  And the synapse decay is 100

Scenario: Price calendar returns monthly prices
  Given a registered user
  When I fire a synapse to FlightSearch with verb "get_price_calendar"
    | from | to  | months_ahead |
    | NYC  | DPS | 3            |
  Then I receive a synapse with verb "price_calendar"
  And the payload contains price entries for 3 months
```

## Scope Boundaries

### Build now

- `Ino.Travel` class library with 7 neurons
- Aspire wiring (`AddTravelDomain` + `WithTravelDomain`)
- RFW component library (9 templates)
- RFW activation in Flutter chat screen
- Rive persona (10 states, architecture-driven)
- Progressive chat rendering (skeleton → cards → text)
- Google auth via UserNeuron
- Price tracking with Orleans scheduling
- BDD tests per neuron

### Skip now (code stays, not wired)

- Stripe billing, subscriptions, tiers, token tracking
- Promo codes, usage events, overage billing
- Hangfire jobs
- TripRadar REST API as running service
- TripRadar Bot, MiniApp, WebUI as running services
