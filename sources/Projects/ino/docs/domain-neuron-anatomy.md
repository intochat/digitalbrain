# Domain, neuron, and synapse anatomy

How ino behavior is shaped now: a **domain** owns neurons and synapses. There is no separate runtime/product unit between them.

## What a domain flow is

A domain flow is a path through neurons connected by synapses that produces a user-visible outcome. From a code perspective it is:

- a graph of neurons and synapses,
- entry synapses routed by Cortex into the owning domain,
- journaled neuron state for multi-hop flows,
- RFW payloads emitted when the UI needs richer affordances than text,
- self-improvement via new neurons and new synapse handlers after the L1 loop matures.

What separates a good domain flow from a chatbot wrapper:

1. **Anticipates dependencies** — Plan Trip needs weather; weather needs dates; dates affect flight prices; flight prices affect hotel budget; hotel location affects activity picks.
2. **Holds context across hops** — the responsible neuron keeps state in its journal and resumes from the same correlation.
3. **Surfaces the next best question, not all questions** — no 20-field form.
4. **Cross-domain by default** — Plan Trip can touch Travel, Weather, Calendar, Places, Payments, and Taxi through synapses.
5. **Reversible and resumable** — every step is represented by durable neuron state and synapse history.
6. **Composable** — `find_flights` can be reused by Plan Trip and by a direct flight-search prompt.

## BDD-first to real-LLM doctrine

The dev-loop story for any non-trivial domain flow is two switches:

| Layer | Test mode | Production |
|---|---|---|
| `IChatClient` | `BddMockChatClient` returns scripted replies / tool calls from `.feature` | xAI Grok via `XaiProviderFactory` or another provider |
| Tool execution | mock corpus / fixture middleware | real implementations such as TripRadar GraphQL or Open-Meteo |

The seam is two interfaces. Both swap independently. A scenario in `.feature` describes one full conversation including tool roundtrips. Flipping `INO_TEST_MODE=false` swaps the chat client; replacing a mock tool with its real implementation swaps the data path. The neurons, synapses, RFW emission, and Flutter widgets do not change.

This is why every new tool gets a tool-shaped surface even when backed by a static corpus today: future-proofing the swap is cheap if we name and shape it correctly now.

## Current pragmatic position

The trip-planning code is mid-evolution. Two paths exist:

- `PlanTripPlan` (`domains/travel/Ino.Domains.Travel/Plans/PlanTripPlan.cs`) is the current six-hop RFW card flow. It is effectively an orchestration neuron but still carries legacy naming and non-journaled state.
- `TripPlannerNeuron` (`domains/travel/Ino.Domains.Travel/Neurons/TripPlannerNeuron.cs`) is the journaled neuron path. It handles slot extraction, fans out through `FindFlightsRequest`, `FindHotelsRequest`, and `FindPlacesRequest`, then composes an itinerary card.

Direction: the six-hop Plan Trip flow belongs in a journaled neuron. Legacy plan names should be retired as the refactor lands. Domains own neurons and synapses; multi-hop state belongs in neurons.

## Plan Trip six-hop scope

Reduced from the conceptual 12 to 6, deliberately. This subset exercises the primitives we need to prove: cross-domain composition, weather correlation, events as anchors, RFW per hop, BDD-driven mocks, and the real-LLM swap pattern.

```text
1. dates_refinement
   tool: weather.get_climatology(destination, month)
   emits: WeatherSummaryCard + DateRangeCard

2. find_flights
   tool: travel.find_flights(origin, destination, dateRange)
   emits: FlightCard list

3. find_hotels
   tool: travel.find_hotels(destination, checkin, checkout)
   emits: HotelCard list

4. find_events
   tool: events.find_events(destination, dateRange)
   emits: EventCard list

5. weather_aware_activities
   tools: weather.get_forecast(location, day) x travel.find_places(near, type)
   emits: DayPlanCard with weather badge + swap button

6. confirmation
   summarises selections and emits the final trip card
```

Each hop exposes a tool-shaped call site so a later LLM-backed neuron can attribute these to `[Description]`-tagged methods and let the agentic loop drive them.

## Verification target

The slice is done when:

1. `dotnet build ino.slnx` is green.
2. The Plan Trip neuron e2e covers the six-hop path, branch path, missing-slot path, and failure surface.
3. `aspire run` with `INO_TEST_MODE=true` boots cleanly.
4. Driving the Bali prompt in the browser walks through all six hops, each emitting its RFW card.
5. Screenshots under `reviews/TripPlanner/` capture each stage.
6. Aspire traces show one `grpc Chat` span branching into the expected neuron and synapse chain with `traceparent` propagated end-to-end.

## What this is not

- A spec. The contract here is intent, not API freeze.
- A migration plan for the full LLM tool-call path.
- A pitch for adding more hops. Twelve hops is the full vision; six is what ships now.
