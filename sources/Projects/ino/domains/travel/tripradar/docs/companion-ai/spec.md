# Companion AI v1 — Technical Specification

## Vision

Companion AI is a proactive, preference-aware travel assistant powered by [IAW](https://github.com/InteractiveAgents/IAW) (Interactive Agents for the Web) — an Orleans-based multi-agent orchestration framework. It doesn't just respond to queries; it anticipates needs, monitors conditions, and delivers personalized recommendations by cross-referencing multiple data sources against learned user preferences.

### What the User Experiences

- **Natural conversation** — "Find me cheap flights to Barcelona next month" in Telegram or MiniApp, not forms
- **Proactive alerts** — "Temperature dropping to 12°C tomorrow in Barcelona, bring a hoodie"
- **Deep recommendations** — Restaurants ranked by aggregating TripAdvisor + Yelp + OpenTable reviews, filtered by user's food preferences
- **Smart price intelligence** — "This price is in the bottom 10% for this route historically, book now"
- **Vision-powered tools** — Photo of a foreign menu → translated with prices in your currency. Photo of a receipt → expenses extracted and added to trip budget
- **Trip awareness** — Knows where you are, what's planned, what's done, and adjusts suggestions accordingly
- **Learns over time** — Remembers you prefer window seats, morning flights, Italian food, and 4-star hotels

### Two Interfaces, One Brain

IAW's Orleans architecture means agents are stateful grains accessible from any client:

- **Telegram** — Primary conversational interface. Most natural for chat, proactive alerts, quick photo sends
- **MiniApp Companion page** — Secondary interface. Better for visual content (itineraries, budget charts, maps)

Both connect to the same Orleans silo. Same agents, same state, same conversation history.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Aspire AppHost                         │
│                                                             │
│  ┌──────────┐  ┌──────────────┐  ┌───────────────────────┐  │
│  │ Telegram  │  │   MiniApp    │  │   TripRadar.API       │  │
│  │   Bot     │  │  (Blazor)    │  │   (REST + GraphQL)    │  │
│  │ (client)  │  │  (client)    │  │   (existing, no chg)  │  │
│  └─────┬─────┘  └──────┬───────┘  └──────────┬────────────┘  │
│        │               │                      │               │
│        └───────┬───────┘                      │               │
│                ▼                               │               │
│  ┌──────────────────────────────┐              │               │
│  │   TripRadar.Agents.Host     │◄─────────────┘               │
│  │   (Orleans Silo)            │                              │
│  │                             │                              │
│  │  ┌─ Search/                 │  ┌──────────┐ ┌──────────┐  │
│  │  │  FlightSearchAgent       │  │ Postgres │ │  Redis   │  │
│  │  │  HotelSearchAgent        │  └──────────┘ └──────────┘  │
│  │  │  PlaceDiscoveryAgent     │                              │
│  │  │  EventDiscoveryAgent     │  ┌──────────┐ ┌──────────┐  │
│  │  ├─ Proactive/              │  │  Kafka   │ │  Qdrant  │  │
│  │  │  WeatherMonitorAgent     │  └──────────┘ └──────────┘  │
│  │  │  PriceWatchAgent         │                              │
│  │  │  TripTimelineAgent       │  ┌──────────────────────┐   │
│  │  ├─ Planning/               │  │   LLM Providers      │   │
│  │  │  ItineraryPlannerAgent   │  │  Anthropic / OpenAI  │   │
│  │  │  BudgetAgent             │  │  Ollama / Google /   │   │
│  │  │  CurrencyAgent           │  │  GitHub Models       │   │
│  │  ├─ Personal/               │  └──────────────────────┘   │
│  │  │  UserPreferenceAgent     │                              │
│  │  │  TripContextAgent        │                              │
│  │  └─ (IAW base agents)       │                              │
│  │     ThreadAgent, Shell,     │                              │
│  │     Memory, Approver, etc.  │                              │
│  └──────────────────────────────┘                              │
└─────────────────────────────────────────────────────────────┘
```

### Project Structure

```
src/
  TripRadar.Agents/                    # Travel domain agents (NuGet: IAW.Core, IAW.Agents)
  │
  ├─ Search/
  │  ├─ FlightSearchAgent.cs
  │  ├─ IFlightSearch.cs               # Grain interface
  │  ├─ HotelSearchAgent.cs
  │  ├─ IHotelSearch.cs
  │  ├─ PlaceDiscoveryAgent.cs
  │  ├─ IPlaceDiscovery.cs
  │  ├─ EventDiscoveryAgent.cs
  │  ├─ IEventDiscovery.cs
  │  └─ Tools/
  │     ├─ FlightSearchTools.cs        # Calls TripRadar API
  │     ├─ HotelSearchTools.cs
  │     ├─ ReviewAggregationTools.cs   # TripAdvisor + Yelp + OpenTable
  │     └─ MapsTools.cs
  │
  ├─ Proactive/
  │  ├─ WeatherMonitorAgent.cs
  │  ├─ IWeatherMonitor.cs
  │  ├─ WeatherAlertEvent.cs           # Co-located with domain
  │  ├─ PriceWatchAgent.cs
  │  ├─ IPriceWatch.cs
  │  ├─ PriceDropEvent.cs
  │  ├─ TripTimelineAgent.cs
  │  ├─ ITripTimeline.cs
  │  ├─ TripReminderEvent.cs
  │  └─ Tools/
  │     ├─ WeatherTools.cs
  │     └─ PriceAnalysisTools.cs
  │
  ├─ Planning/
  │  ├─ ItineraryPlannerAgent.cs
  │  ├─ IItineraryPlanner.cs
  │  ├─ BudgetAgent.cs
  │  ├─ IBudget.cs
  │  ├─ CurrencyAgent.cs
  │  ├─ ICurrency.cs
  │  └─ Tools/
  │     ├─ BudgetTools.cs
  │     ├─ VisionTools.cs              # Receipt/menu image processing
  │     └─ CurrencyTools.cs
  │
  └─ Personal/
     ├─ UserPreferenceAgent.cs
     ├─ IUserPreference.cs
     ├─ TripContextAgent.cs
     ├─ ITripContext.cs
     └─ Tools/
        ├─ PreferenceTools.cs
        └─ TripStateTools.cs

  TripRadar.Agents.Host/               # Orleans silo process
  │  Program.cs
  │  TripRadar.Agents.Host.csproj

  TripRadar.Bot/                        # Existing — becomes Orleans client
  │  Agents/                            # NEW folder
  │  │  TelegramAgentRouter.cs          # Telegram message → IThread dispatch
  │  │  CompanionNotificationService.cs # Agent events → Telegram messages
```

### Aspire Wiring

```csharp
// In AppHost.cs — additions alongside existing infrastructure

// Existing
var tripRadar = builder.AddTripRadar();

// IAW agent cluster — LLM provider fully configurable
var iaw = builder.AddIAW("iaw")
    .WithLLM<AnthropicModels.Claude45Haiku>().AsFast()
    .WithLLM<AnthropicModels.Sonnet46>().AsBalanced()
    .WithLLM<AnthropicModels.Opus46>().AsReasoning()
    .WithEmbedding<OpenAIModels.TextEmbedding3Small>()
    .WithVectorDb(v => v.WithDataVolume())
    .WithWorkspace(builder.ExecutionContext.IsRunMode
        ? "/app" : Path.GetFullPath("../.."));

// Agent silo
var agentHost = builder.AddProject<Projects.TripRadar_Agents_Host>("agent-host")
    .WithReference(iaw)
    .WithReference(tripRadar.Api)
    .WithReference(tripRadar.Redis)
    .WithReference(tripRadar.Kafka);

// Bot as Orleans client
var bot = builder.AddProject<Projects.TripRadar_Bot>("bot")
    .WithReference(iaw.AsClient())
    .WithReference(tripRadar.Api)
    .WithReference(tripRadar.Kafka)
    .WaitFor(agentHost);
```

### Data Flow — Agents ↔ Existing Server

Agents do NOT access the database directly. They call the existing TripRadar.Server.API:

```
User (Telegram) → Bot → IThread (Orleans) → FlightSearchAgent
  → FlightSearchTools.SearchFlights()
    → HTTP GET /api/v1/search/flights (existing API)
    → SerpApi (existing provider)
  ← structured results
  → LLM formats conversational response
← Telegram message to user
```

This means zero changes to the server domain model, database, or API for the initial phases.

---

## Agent Specifications

### Search & Discovery

#### FlightSearchAgent
- **LLM tier:** Fast
- **Grain interface:** `IFlightSearch`
- **Tools:** `FlightSearchTools` (calls `/api/v1/search/flights`, `/flights/explore`, `/flights/price-calendar`, `/flights/nearby-prices`)
- **Capabilities:** Natural language → structured flight search, result summarization, alternative suggestions (flexible dates, nearby airports), preference-aware filtering
- **Durable state:** Recent searches per user, preferred routes
- **Dependencies:** UserPreferenceAgent (query preferences before search)

#### HotelSearchAgent
- **LLM tier:** Fast
- **Grain interface:** `IHotelSearch`
- **Tools:** `HotelSearchTools` (calls hotel search API + TripAdvisor + Google Maps for location context)
- **Capabilities:** NL hotel search, cross-reference reviews, preference-weighted ranking
- **Durable state:** Recent hotel searches, preferred amenities
- **Dependencies:** UserPreferenceAgent, TripContextAgent (destination context)

#### PlaceDiscoveryAgent
- **LLM tier:** Balanced
- **Grain interface:** `IPlaceDiscovery`
- **Tools:** `ReviewAggregationTools` (TripAdvisor search + place details, Yelp search + place + reviews, OpenTable reviews, Google Maps + local places), `MapsTools` (directions, distance)
- **Capabilities:** Multi-source review aggregation, sentiment analysis, preference-weighted ranking with reasoning ("rated 4.5 on TripAdvisor, locals recommend the seafood, matches your outdoor dining preference")
- **Durable state:** Visited places, ratings given, cuisine preferences refined
- **Dependencies:** UserPreferenceAgent, TripContextAgent

#### EventDiscoveryAgent
- **LLM tier:** Fast
- **Grain interface:** `IEventDiscovery`
- **Tools:** `EventSearchTools` (calls Google Events API via SerpApi), YouTube search for event previews
- **Capabilities:** Destination + date range event search, interest-based filtering, proactive notifications
- **Durable state:** Event interests, past event attendance
- **Dependencies:** UserPreferenceAgent, TripContextAgent

### Proactive & Monitoring

#### WeatherMonitorAgent
- **LLM tier:** Fast
- **Grain interface:** `IWeatherMonitor`
- **Tools:** `WeatherTools` (external weather API — OpenWeatherMap or WeatherAPI)
- **Scheduling:** Orleans reminders, checks every 6 hours for active trip destinations
- **Events:** Publishes `WeatherAlertEvent` to Orleans stream
- **Capabilities:** Forecast monitoring, clothing recommendations, itinerary adjustment suggestions ("rain Thursday, swap outdoor plans to Friday?")
- **Durable state:** Monitored destinations, alert thresholds, last alert sent (avoid spam)
- **Dependencies:** TripContextAgent (active trip + itinerary)

#### PriceWatchAgent
- **LLM tier:** Balanced
- **Grain interface:** `IPriceWatch`
- **Tools:** `PriceAnalysisTools` (historical price data from Redis/API, current tracking state)
- **Input:** Kafka `flight-events` topic → Orleans stream bridge
- **Events:** Publishes `PriceDropEvent` to Orleans stream
- **Capabilities:** AI-powered price analysis ("bottom 10% historically"), timing advice ("prices usually drop 3 weeks before departure for this route"), grouped daily summaries
- **Durable state:** Price history per tracked route, alert preferences
- **Migration:** Gradually replaces existing `FlightPriceConsumer` in Bot

#### TripTimelineAgent
- **LLM tier:** Fast
- **Grain interface:** `ITripTimeline`
- **Scheduling:** Orleans reminders for each upcoming event
- **Events:** Publishes `TripReminderEvent` to Orleans stream
- **Capabilities:** Countdown reminders (5 days, 1 day, 3 hours), check-in window alerts, document reminders (passport validity, visa), post-trip feedback prompts
- **Durable state:** Trip timeline, reminder schedule, sent reminders
- **Dependencies:** TripContextAgent

### Planning & Budget

#### ItineraryPlannerAgent
- **LLM tier:** Reasoning
- **Grain interface:** `IItineraryPlanner`
- **Tools:** Delegates to PlaceDiscoveryAgent, EventDiscoveryAgent; uses `MapsTools` for proximity/routing
- **Capabilities:** Day-by-day itinerary generation, optimized by location proximity and opening hours, respects user energy preferences ("relaxed morning, busy afternoon"), iterative refinement ("swap day 2 and 3", "add more food spots")
- **Durable state:** Active itinerary per trip, revision history
- **Dependencies:** PlaceDiscoveryAgent, EventDiscoveryAgent, UserPreferenceAgent, TripContextAgent, BudgetAgent (stay within budget)

#### BudgetAgent
- **LLM tier:** Balanced
- **Grain interface:** `IBudget`
- **Tools:** `BudgetTools` (CRUD expenses, budget totals, category breakdown), `VisionTools` (receipt/bill image → extracted line items + amounts)
- **Capabilities:** Receipt photo scanning, automatic expense categorization (food, transport, accommodation, activities, shopping), running totals with warnings ("70% of food budget spent on day 2 of 5"), end-of-trip summary
- **Durable state:** Trip budget, all expenses with categories, daily totals
- **Dependencies:** CurrencyAgent (convert foreign expenses), TripContextAgent

#### CurrencyAgent
- **LLM tier:** Fast
- **Grain interface:** `ICurrency`
- **Tools:** `CurrencyTools` (exchange rate API), `VisionTools` (menu/sign photo → text extraction)
- **Capabilities:** Real-time currency conversion, foreign menu/sign photo → translated text + converted prices ("this dish is 450 CZK ≈ €18"), knows user's home currency
- **Durable state:** Cached exchange rates, user's preferred currencies
- **Dependencies:** UserPreferenceAgent (home currency, language)

### Personal & Preference

#### UserPreferenceAgent
- **LLM tier:** Balanced
- **Grain interface:** `IUserPreference`
- **Tools:** `PreferenceTools` (read/write structured preferences to durable state)
- **Capabilities:** Explicit preferences ("I don't like spicy food"), implicit learning (always books economy → infer budget preference), periodic confirmation ("I noticed you always pick Italian restaurants, should I prioritize those?")
- **Durable state:** Structured preference profile:
  - Travel: preferred airlines, seat type, cabin class, layover tolerance, time-of-day
  - Food: cuisine types, dietary restrictions, price range, dining style
  - Accommodation: star rating, amenities, location priority, budget range
  - Activities: energy level, interests, accessibility needs
  - Budget: default daily budget, spending patterns
  - Personal: home currency, languages, nationality (for visa checks)
- **Dependencies:** None (leaf agent, queried by all others)

#### TripContextAgent
- **LLM tier:** Fast
- **Grain interface:** `ITripContext`
- **Tools:** `TripStateTools` (read/write active trip state, query TripRadar API for bookings/trackings)
- **Capabilities:** Maintains awareness of current trip (destination, dates, day X of Y), active bookings, planned itinerary, completed activities. Provides context to all other agents so they skip redundant questions.
- **Durable state:** Active trip state, trip history
- **Dependencies:** None (leaf agent, queried by all others)

---

## Phasing

### Phase 1 — IAW Readiness (IAW repo)
Unblock TripRadar consumption of IAW packages. See `iaw-issues.md` for full issue list.

### Phase 2 — TripRadar Foundation (TripRadar repo)
Add IAW packages, create project scaffold, wire Orleans into Aspire, connect Bot as client. See `tripradar-issues.md` for full issue list.

### Phase 3 — Core Agents (TripRadar repo)
FlightSearchAgent, HotelSearchAgent, PlaceDiscoveryAgent, UserPreferenceAgent. Telegram conversational interface. MiniApp Companion page.

### Phase 4 — Vision & Budget Agents (TripRadar repo)
BudgetAgent, CurrencyAgent with vision/OCR. WeatherMonitorAgent, PriceWatchAgent, TripTimelineAgent.

### Phase 5 — Deep Intelligence (TripRadar repo)
ItineraryPlannerAgent, EventDiscoveryAgent, TripContextAgent. Full proactive recommendation engine.

---

## Dependencies Map

See `dependencies.md` for the full issue dependency graph showing which issues block which across both repos.
