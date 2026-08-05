# Scenario 34: “Replay last Tuesday morning” time-travel journal read

## User intent
The owner asks: “Replay last Tuesday morning—what did my brain do between 8 and 11?” They want a causal reconstruction across chat, email automations, and tasks—not a vague summary, but navigable journal sequences with correlation ids.

## Trigger
Chat/introspection: `UserMessaged` intent “replay/time-travel”, or shell History scrubber selecting a time range.

## Imagined modules
- Introspection / JournalQuery
- Chat transcript index
- Cross-neuron journal reader (MCP/northbound)
- UiTimeline projector
- Memory (optional semantic “what mattered”)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| Chat / recall | User question |
| Introspection / default | Answers JournalRangeQuery |
| JournalIndex / owner | Aggregates sequences by time (read model) |
| UiTimeline / shell | Renders swimlanes per neuron |
| Assistant / recall | Narrates without inventing facts |

## Synapse choreography
1. UserMessaged → Assistant Asks `JournalRangeQuery(Tue 08:00–11:00)`.
2. JournalIndex answers with ordered `JournalSlice` (neuron id, sequence, fact kinds, correlation, sources)—causal facts only.
3. Assistant may Ask secondary `ExplainCorrelation(id)` for one thread (e.g., email → task).
4. Emits `UiSurface(TimelineSwimlane)` broadcast; directed `AssistantResponded` with citations to sequences.
5. Owner taps a node → `FocusJournalEntry` → detail pane with raw fact type names (still no prompts/secrets).
6. Optional “branch what-if” is **not** mutating history; only new forward facts.

## Orleans / Core surface exercised
DurableGrain journals as source of truth; watchers/observers or read APIs over journal storage; request context owner scoping; grain call filters denying foreign journals; no transactions required; placement of index grains.

## Rich experience
Multi-lane timeline (Chat, Gmail behavior, Tasks); correlation highlight; filters by fact kind; export audit pack; scrubber linked to clock.

## Failure / adversarial cases
- Assistant hallucinating events not in JournalSlice → UI must prefer journal projection; tests compare narrative anchors to slice.
- Clock skew across silos → store UTC; display owner TZ.
- Massive range OOM → paging synapses JournalPage; never unbounded load in one turn.
- Leaking another owner’s slice → identity checks on every query.

## Capability claim
DigitalBrain can time-travel an owner’s real causal nervous system via journals—something chat logs alone cannot reconstruct across modules.
