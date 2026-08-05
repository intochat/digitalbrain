# Scenario 03: "What did I do last week?" journal timeline recall

## User intent
The owner asks in chat what they did last week and expects a structured timeline reconstructed from journaled facts across chat, email actions, calendar, tasks, and behaviors — not a vague LLM guess.

## Trigger
Chat message: `UserMessaged` with text intent "What did I do last week?" (relative time resolved against owner timezone).

## Imagined modules
- Chat (turn, transcript)
- Introspection / JournalQuery (cross-neuron journal scan under owner scope)
- Calendar (events attended/created)
- Gmail (sent/important threads)
- Tasks (completed/attempted)
- TimelineProjector (bucket by day, rank salience)
- Memory (owner timezone, "busy" definitions)
- Shell UI (timeline scene)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| chat/owner-desk | Accept question; present timeline answer |
| journalquery/owner | Answer range queries over owner-visible journals |
| calendar/owner | Contribute event facts for range |
| gmail/owner-inbox | Contribute sent/received salience |
| tasks/owner | Contribute completions |
| timeline/owner | Merge, rank, emit day buckets |
| memory/owner-profile | Timezone, workweek prefs |
| shell/primary | Optional timeline pane |

## Synapse choreography
1. Edge → `chat/owner-desk`: directed ingress `UserMessaged` (text, clientTime).
2. Chat classifies recall intent, **directs** `TimeRangeResolvedAsked` → memory/time module → `TimeRangeResolved` (startUtc, endUtc, tz).
3. Chat **directs** `TimelineBuildAsked` → `timeline/owner` (range, maxItems).
4. Timeline **directs** parallel asks (non-blocking multi-ask pattern):
   - `JournalRangeAsked` → `journalquery/owner`
   - `CalendarRangeAsked` → `calendar/owner`
   - `EmailSalienceRangeAsked` → `gmail/owner-inbox`
   - `TaskCompletionsRangeAsked` → `tasks/owner`
5. Each answers directed `*Answered` with fact refs / summaries (no raw secrets if redacted).
6. Timeline merges on continuations (`Answer<…>` handlers), **broadcasts** `TimelineBuilt` (days[], items[{when, kind, title, synapseRef, neuron}]).
7. Chat hears `TimelineBuilt` (or receives as answer), **directs** `AssistantResponded` with narrative + structured day sections; **broadcasts** `ChatCardRendered` (timeline table).
8. Shell may **broadcast** `SceneOpened` for timeline view bound to same correlation.

## Orleans / Core surface exercised
DurableGrain journals as the recall oracle; request context for owner isolation on journalquery; serialized turns; multi-ask continue-without-await-neuron pattern; grain call filters enforcing owner scope; placement sticky for chat neuron for the whole turn chain; module catalog of query answerers.

## Rich experience
Chat: day-headed sections with bullets and deep-links ("open journal at seq N"). Shell timeline: vertical rail, icons by module, filter chips (email/calendar/tasks/chat). Export action emits `TimelineExportAsked`.

## Failure / adversarial cases
- Hallucinated week: assistant must only narrate items backed by `synapseRef`; missing modules yield explicit gaps, not invented work.
- Cross-owner journal read: journalquery filters must fail closed; adversarial name collision on neuron Name must not escape owner tenancy.
- Huge range: pagination via `TimelinePageToken`; must not load entire silo journals into one turn.
- Clock skew: range resolution uses owner tz + server UTC; ambiguous "last week" journaled as resolved bounds on the ask.
- Partial module outage: timeline still builds with `sourcesFailed[]` visible in UI.

## Capability claim
The product answers autobiographical questions from durable cross-module journals with provenance refs — a chatbot without a journaled nervous system can only invent a week.
