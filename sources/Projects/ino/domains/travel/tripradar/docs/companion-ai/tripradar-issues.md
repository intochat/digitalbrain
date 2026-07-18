# TripRadar Issues — Companion AI v1

All issues below go in the `RoseXTechnology/TripRadar` repository.

---

## GitHub Setup

### Milestone
- **Name:** `Companion v1`
- **Description:** IAW-powered AI travel companion — proactive, preference-aware, vision-capable

### Labels to Create
- `companion ai` — Color: `#7C3AED` — Description: "AI companion features powered by IAW"
- `area: agents` — Color: `#F59E0B` — Description: "TripRadar.Agents project — travel domain agents"

### GitHub Project
- **Name:** `Companion AI`
- **Description:** "All work related to IAW integration and AI travel companion"
- **Visibility:** Private
- Add all Companion v1 milestone issues to this project

---

## Phase 2 — Foundation

### TR-01: Add IAW NuGet packages to solution

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** InteractiveAgents/IAW — NuGet publishing (IAW-01)

**Description:**
Add IAW NuGet packages to TripRadar solution via central package management.

**Tasks:**
- Add to `Directory.Packages.props`:
  - `IAW.Core`
  - `IAW.Agents`
  - `Aspire.Hosting.IAW`
  - `Aspire.IAW.Client`
  - `IAW.Testing`
- Verify `dotnet restore` succeeds
- Verify `dotnet build` succeeds with IAW references

### TR-02: Create TripRadar.Agents project scaffold

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-01

**Description:**
Create the `src/TripRadar.Agents/` project that will contain all travel domain agents.

**Tasks:**
- Create `TripRadar.Agents.csproj` targeting net11.0
- Reference `IAW.Core` and `IAW.Agents` NuGet packages
- Create folder structure:
  ```
  Search/
  Proactive/
  Planning/
  Personal/
  ```
- Add project to `TripRadar.slnx`
- Verify build succeeds

### TR-03: Create TripRadar.Agents.Host Orleans silo project

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-02

**Description:**
Create the Orleans silo host that runs all agents (IAW base + TripRadar travel agents).

**Tasks:**
- Create `src/TripRadar.Agents.Host/` project
- Reference `TripRadar.Agents`, `IAW.Agents`, `Aspire.IAW.Client`
- `Program.cs` registers Orleans silo via `AddIAW()` from `Aspire.IAW.Client`
- Configure grain storage (in-memory for dev, persistent for prod)
- Configure Orleans Streams and Reminders
- Add health checks
- Add project to `TripRadar.slnx`

### TR-04: Wire IAW into Aspire AppHost

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-03

**Description:**
Integrate IAW's Orleans cluster into the existing Aspire orchestration alongside all current infrastructure.

**Tasks:**
- In `AppHost.cs`:
  - Add `builder.AddIAW("iaw")` with LLM provider configuration
  - Configure LLM tiers: `.WithLLM<T>().AsFast()` / `.AsBalanced()` / `.AsReasoning()`
  - Add `.WithEmbedding<T>()` for vector search
  - Add `.WithVectorDb()` for Qdrant
  - Add Aspire parameters for LLM API keys (Anthropic, OpenAI)
- Add `TripRadar.Agents.Host` as Aspire project:
  - `.WithReference(iaw)` for Orleans + LLM config
  - `.WithReference(tripRadar.Api)` for server API access
  - `.WithReference(tripRadar.Redis)`
  - `.WithReference(tripRadar.Kafka)`
- Verify all services start via `mcp__aspire__list_resources`
- Verify agent-host appears in Aspire dashboard with healthy status

### TR-05: Connect Bot as Orleans client

**Labels:** `companion ai`, `area: telegram bot`
**Milestone:** `Companion v1`
**Blocked by:** TR-04

**Description:**
Make TripRadar.Bot an Orleans client so it can dispatch Telegram messages to IAW agents.

**Tasks:**
- Add `Aspire.IAW.Client` reference to `TripRadar.Bot`
- Register Orleans client via `AddIAWClient()` in Bot's `Program.cs`
- In AppHost: update bot project with `.WithReference(iaw.AsClient())` and `.WaitFor(agentHost)`
- Create `src/TripRadar.Bot/Agents/` folder
- Create `TelegramAgentRouter.cs`:
  - Receives Telegram text messages from webhook
  - Gets `IThread` grain for user (`telegram:{telegramUserId}`)
  - Sends message to thread, receives response stream
  - Sends response back via `TelegramBotService`
- Create `CompanionNotificationService.cs`:
  - Subscribes to Orleans stream events (`PriceDropEvent`, `WeatherAlertEvent`, `TripReminderEvent`)
  - Formats and sends Telegram messages to users
- Verify end-to-end: Telegram message → Bot → IThread → response → Telegram

### TR-06: Add Qdrant to Aspire infrastructure

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`

**Description:**
Add Qdrant vector database to Aspire AppHost for RAG context (user preferences, trip history, destination knowledge).

**Tasks:**
- Add Qdrant via IAW's `.WithVectorDb(v => v.WithDataVolume())`
- Verify Qdrant starts and is accessible from agent-host
- Verify Qdrant appears in Aspire dashboard

---

## Phase 3 — Core Agents

### TR-07: Implement UserPreferenceAgent

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-04

**Description:**
Leaf agent that stores and learns user preferences. All other agents query this before making recommendations.

**Agent spec:**
- **LLM tier:** Balanced
- **Grain interface:** `IUserPreference`
- **Durable state:** Structured preference profile
  - Travel: preferred airlines, seat type, cabin class, layover tolerance, time-of-day
  - Food: cuisine types, dietary restrictions, price range, dining style
  - Accommodation: star rating, amenities, location priority, budget range
  - Activities: energy level, interests, accessibility needs
  - Budget: default daily budget, spending patterns
  - Personal: home currency, languages, nationality
- **Tools:** `PreferenceTools` — read/write preferences, query preference by category
- **Capabilities:**
  - Store explicit preferences ("I don't like spicy food")
  - Learn from implicit signals (search patterns, booking history)
  - Periodic confirmation ("I noticed you always pick Italian restaurants, prioritize those?")

### TR-08: Implement FlightSearchAgent

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-04, TR-07

**Description:**
Natural language flight search that calls existing TripRadar API endpoints.

**Agent spec:**
- **LLM tier:** Fast
- **Grain interface:** `IFlightSearch`
- **Tools:** `FlightSearchTools`
  - `SearchFlights()` — calls `POST /api/v1/search/flights`
  - `ExploreFlights()` — calls explore endpoint
  - `GetPriceCalendar()` — calls price calendar endpoint
  - `GetNearbyPrices()` — calls nearby prices endpoint
  - `SearchAirports()` — calls `GET /api/v1/search/airports`
- **Capabilities:**
  - Parse "cheap flights to Barcelona next month for 2" → structured query
  - Apply user preferences (airlines, cabin, time) from UserPreferenceAgent
  - Summarize results conversationally with highlights
  - Suggest alternatives: flexible dates, nearby airports, different cabin
- **Durable state:** Recent searches, preferred routes

### TR-09: Implement HotelSearchAgent

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-04, TR-07

**Description:**
Natural language hotel search with cross-source review analysis.

**Agent spec:**
- **LLM tier:** Fast
- **Grain interface:** `IHotelSearch`
- **Tools:** `HotelSearchTools`
  - `SearchHotels()` — calls hotel search API
  - `GetTripAdvisorReviews()` — calls TripAdvisor endpoints (already in SerpApi)
  - `GetGoogleMapsInfo()` — calls Google Maps for location context
- **Capabilities:**
  - NL hotel search with preference-aware filtering
  - Cross-reference reviews from multiple sources
  - Location-aware recommendations ("close to your planned activities")
- **Durable state:** Recent searches, preferred amenities
- **Dependencies:** UserPreferenceAgent, TripContextAgent

### TR-10: Implement PlaceDiscoveryAgent

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-04, TR-07

**Description:**
Multi-source place recommendations — the core "deep analysis" agent.

**Agent spec:**
- **LLM tier:** Balanced
- **Grain interface:** `IPlaceDiscovery`
- **Tools:** `ReviewAggregationTools`
  - `SearchTripAdvisor()` + `GetTripAdvisorPlace()`
  - `SearchYelp()` + `GetYelpPlace()` + `GetYelpReviews()`
  - `GetOpenTableReviews()`
  - `SearchGoogleLocal()` + `GetGoogleMaps()`
  - `GetMapsDirections()` — for distance/travel time
- **Capabilities:**
  - Aggregate reviews across TripAdvisor, Yelp, OpenTable, Google Maps
  - Sentiment analysis across review sources
  - Preference-weighted ranking with reasoning
  - Example output: "Rated 4.5 on TripAdvisor (320 reviews), locals recommend the seafood. Matches your preference for outdoor dining. 12 min walk from your hotel."
- **Durable state:** Visited places, user ratings, refined cuisine preferences
- **Dependencies:** UserPreferenceAgent, TripContextAgent

### TR-11: Build Telegram conversational interface for agents

**Labels:** `companion ai`, `area: telegram bot`
**Milestone:** `Companion v1`
**Blocked by:** TR-05, TR-08

**Description:**
Wire Telegram messages through to IAW agents for natural conversation.

**Tasks:**
- Extend `TelegramAgentRouter` to handle:
  - Text messages → IThread → agent routing
  - Photo messages → forward to appropriate vision agent
  - Voice messages → transcription (if IAW Whisper configured) → text → agents
  - Location sharing → update TripContextAgent
- Handle streaming responses: send typing indicator, stream partial responses as message edits
- Add Telegram commands:
  - `/companion` — start/resume companion conversation
  - `/preferences` — view/edit preferences
  - `/trip` — view active trip context
  - `/budget` — view budget summary
- Preserve conversation context across messages via IThread grain state

### TR-12: Build MiniApp Companion page

**Labels:** `companion ai`, `area: miniapp`
**Milestone:** `Companion v1`
**Blocked by:** TR-05, TR-08

**Description:**
Replace the "coming soon" Companion page with a working chat interface connected to IAW agents.

**Tasks:**
- Redesign `Companion.razor` as a chat interface
- Connect to Bot backend via API (Bot proxies to Orleans)
- Message input with text + photo attachment support
- Streaming response display
- Quick action buttons: "Search flights", "Find restaurants", "Check weather"
- Show active trip context card at top
- Budget summary widget
- Link to full Telegram for richer conversation

---

## Phase 4 — Vision & Budget Agents

### TR-13: Implement CurrencyAgent with vision

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-04, IAW-05 (vision tier support)

**Description:**
Currency conversion and foreign menu/sign translation via vision.

**Agent spec:**
- **LLM tier:** Fast (text), Vision (images)
- **Grain interface:** `ICurrency`
- **Tools:**
  - `CurrencyTools` — exchange rate API integration, conversion calculations
  - `VisionTools` — image → text extraction for menus, signs, price tags
- **Capabilities:**
  - Real-time currency conversion: "how much is 450 CZK in euros?"
  - Photo of foreign menu → extracted items with translated names + converted prices
  - Photo of price tag/sign → translated + converted
  - Knows user's home currency from UserPreferenceAgent
- **Durable state:** Cached exchange rates, user's preferred currencies

### TR-14: Implement BudgetAgent with receipt scanning

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-04, TR-13 (currency conversion), IAW-05 (vision tier support)

**Description:**
Trip expense tracking with receipt photo scanning.

**Agent spec:**
- **LLM tier:** Balanced (analysis), Vision (receipt scanning)
- **Grain interface:** `IBudget`
- **Tools:**
  - `BudgetTools` — CRUD expenses, budget totals, category breakdown, daily/trip summaries
  - `VisionTools` — receipt/bill photo → extracted line items + amounts
- **Capabilities:**
  - Set trip budget: "my Barcelona budget is €1500 for 5 days"
  - Receipt scanning: send photo → items extracted, categorized, added to budget
  - Screenshot of purchase → amount extracted and logged
  - Automatic categorization: food, transport, accommodation, activities, shopping
  - Running totals with warnings: "70% of food budget spent on day 2 of 5"
  - Daily and end-of-trip summaries
- **Durable state:** Trip budget, all expenses with categories, daily totals
- **Dependencies:** CurrencyAgent (convert foreign expenses), TripContextAgent

### TR-15: Implement WeatherMonitorAgent

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-04, IAW-08 (proactive agent pattern docs)

**Description:**
Proactive weather monitoring with clothing and itinerary recommendations.

**Agent spec:**
- **LLM tier:** Fast
- **Grain interface:** `IWeatherMonitor`
- **Tools:** `WeatherTools` — external weather API (OpenWeatherMap or WeatherAPI)
- **Scheduling:** Orleans reminders, checks every 6 hours for active trip destinations
- **Events:** Publishes `WeatherAlertEvent` to Orleans stream → Telegram
- **Capabilities:**
  - "Temperature dropping to 12°C tomorrow, bring a hoodie"
  - "Rain expected Thursday, consider swapping outdoor plans to Friday"
  - Packing recommendations based on forecast
- **Durable state:** Monitored destinations, alert thresholds, last alert time (avoid spam)
- **Dependencies:** TripContextAgent (active trip + itinerary)

### TR-16: Implement PriceWatchAgent with Kafka bridge

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-04, IAW-06 (Kafka bridge)

**Description:**
AI-powered price monitoring that replaces the current `FlightPriceConsumer`.

**Agent spec:**
- **LLM tier:** Balanced
- **Grain interface:** `IPriceWatch`
- **Tools:** `PriceAnalysisTools` — historical price data, trend analysis, tracking state
- **Input:** Kafka `flight-events` → Orleans stream via Kafka bridge adapter
- **Events:** Publishes `PriceDropEvent` → Telegram
- **Capabilities:**
  - "This price is in the bottom 10% for this route historically"
  - "Prices for this route usually drop 3 weeks before departure"
  - Grouped daily summaries instead of alert spam
  - Timing advice: "book now" vs "wait for further drop"
- **Durable state:** Price history per route, alert preferences, sent alerts
- **Migration path:** Runs alongside existing `FlightPriceConsumer`, then replaces it

### TR-17: Implement TripTimelineAgent

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-04, IAW-08 (proactive agent pattern docs)

**Description:**
Proactive reminders and countdown notifications for trips.

**Agent spec:**
- **LLM tier:** Fast
- **Grain interface:** `ITripTimeline`
- **Scheduling:** Orleans reminders per upcoming event
- **Events:** Publishes `TripReminderEvent` → Telegram
- **Capabilities:**
  - Countdown: "Your Barcelona trip is in 5 days"
  - Check-in: "Online check-in opens in 2 hours for your Vueling flight"
  - Documents: "Make sure your passport is valid for 6+ months"
  - Post-trip: "How was Barcelona? Rate your experience"
- **Durable state:** Trip timeline, reminder schedule, sent reminders
- **Dependencies:** TripContextAgent

### TR-18: Wire MiniApp Money features to BudgetAgent and CurrencyAgent

**Labels:** `companion ai`, `area: miniapp`
**Milestone:** `Companion v1`
**Blocked by:** TR-13, TR-14

**Description:**
Replace the "coming soon" Money section in the MiniApp Home page with working features.

**Tasks:**
- **Budget tracking page:** Trip budget dashboard, expense list by category, daily spend chart, add expense form, photo capture for receipts
- **Currency conversion page:** Quick converter with user's home currency as default, photo capture for foreign menus/signs, translated + converted results display
- Both pages connect to Bot backend which proxies to Orleans agents
- Remove "COMING SOON" badges from Money section on Home page

---

## Phase 5 — Deep Intelligence

### TR-19: Implement TripContextAgent

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-04

> **Note:** Although listed in Phase 5, this agent can be implemented as early as Phase 3 since it's a leaf agent with no agent dependencies. Multiple Phase 4 agents (WeatherMonitor, TripTimeline, ItineraryPlanner) benefit from trip context. Prioritize accordingly.

**Description:**
Maintains real-time awareness of the user's current trip state.

**Agent spec:**
- **LLM tier:** Fast
- **Grain interface:** `ITripContext`
- **Tools:** `TripStateTools` — read/write trip state, query TripRadar API for bookings and scheduled executions
- **Capabilities:**
  - Tracks: destination, dates, day X of Y, active bookings, planned itinerary, completed activities
  - Provides context to all other agents (no redundant questions)
  - Updates as trip progresses
  - Location awareness when user shares location via Telegram
- **Durable state:** Active trip state, trip history

### TR-20: Implement EventDiscoveryAgent

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-04, TR-07

**Description:**
Discover events at the user's destination during their travel dates.

**Agent spec:**
- **LLM tier:** Fast
- **Grain interface:** `IEventDiscovery`
- **Tools:**
  - `EventSearchTools` — calls Google Events API via existing SerpApi integration
  - `YouTubeSearchTools` — find event preview videos
- **Capabilities:**
  - "There's a jazz festival in Barcelona during your trip"
  - Interest-based filtering from UserPreferenceAgent
  - Proactive notifications when new events are discovered
- **Durable state:** Event interests, past attendance
- **Dependencies:** UserPreferenceAgent, TripContextAgent

### TR-21: Implement ItineraryPlannerAgent

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-10, TR-20, TR-07

**Description:**
Day-by-day trip planning using data from all other agents.

**Agent spec:**
- **LLM tier:** Reasoning
- **Grain interface:** `IItineraryPlanner`
- **Tools:** Delegates to PlaceDiscoveryAgent, EventDiscoveryAgent; uses `MapsTools` for proximity optimization
- **Capabilities:**
  - Generate day-by-day itinerary optimized by location proximity and opening hours
  - Respect user energy preferences ("relaxed morning, busy afternoon")
  - Account for travel time between spots (Google Maps Directions)
  - Iterative refinement: "swap day 2 and 3", "add more food spots", "make it more relaxed"
  - Budget-aware: stay within daily budget from BudgetAgent
- **Durable state:** Active itinerary per trip, revision history
- **Dependencies:** PlaceDiscoveryAgent, EventDiscoveryAgent, UserPreferenceAgent, TripContextAgent, BudgetAgent

### TR-22: Implement proactive recommendation engine

**Labels:** `companion ai`, `area: agents`
**Milestone:** `Companion v1`
**Blocked by:** TR-07, TR-10, TR-19

**Description:**
Cross-agent proactive recommendations that make the companion feel alive.

**Tasks:**
- Extend UserPreferenceAgent with implicit learning (search patterns → inferred preferences)
- WeatherMonitorAgent suggests itinerary swaps based on forecast changes
- PriceWatchAgent sends grouped daily intelligence reports
- PlaceDiscoveryAgent proactively suggests places based on location + time of day + preferences
- TripTimelineAgent sends contextual reminders (not just countdown, but "check-in opens at 3pm, you have a restaurant at 7pm")
- All proactive messages route through `CompanionNotificationService` in Bot with rate limiting (no spam)

### TR-23: Per-user token accounting for Companion AI

**Labels:** `companion ai`, `area: agents`, `area: server`
**Milestone:** `Companion v1`
**Blocked by:** TR-04, IAW-09 (token usage tracking)

**Description:**
Map IAW's token usage tracking to TripRadar's existing `UsageEvent` domain model and tier limits.

**Tasks:**
- Bridge IAW's `ITokenBudget` to TripRadar's token consumption system
- Agent LLM calls create `UsageEvent` entries with `ServiceType = CompanionAI`
- Respect tier limits: Free tier gets limited companion interactions, Essential/Advanced get more
- Budget exceeded → friendly message: "You've used your companion quota for this month. Upgrade for more."
- Dashboard in MiniApp showing companion token usage
