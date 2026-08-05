# Scenario 47: Pub-sub: many dashboards bind same broadcast fact kind

## User intent
Multiple open panes—mobile glance, desktop wallboard, chat sidebar widget—all show live “open incidents” counts. When IncidentOpened/Closed facts fire, every bound dashboard updates without the incident module naming consumers.

## Trigger
Ops module Emits `IncidentOpened` / `IncidentClosed` (broadcast); dashboards already listening via catalog declarations or Connect.

## Imagined modules
- Incident management
- Dashboard widgets (several neuron instances)
- Shell multi-pane
- Optional stream bridge to edge SSE

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| IncidentDesk / default | Source of incident facts |
| Dashboard / wall-ops | Large board projection |
| Dashboard / mobile-glance | Compact projection |
| Dashboard / chat-sidebar | Inline widget |
| UiEdge / sse | Pushes to devices |
| AnalyticsOverhear / default | Overhears for metrics |

## Synapse choreography
1. At composition, each Dashboard declares `INeuron<IncidentOpened>`, `INeuron<IncidentClosed>` (or Connect to fact kinds).
2. IncidentDesk Emits `IncidentOpened` broadcast—speaker names nobody.
3. All three dashboards receive, journal, Emit their own `UiSurface` variants (different layouts).
4. UiEdge fans to SSE subscribers per surface id.
5. Adding a fourth dashboard module requires no change to IncidentDesk.
6. AnalyticsOverhear records rates without affecting dashboards.
7. Disconnect of mobile-glance removes only that consumer.

## Orleans / Core surface exercised
Broadcast announce/listen; pub-sub; streams/SSE edge; DurableGrain journals per dashboard instance; Connect/Disconnect; placement of many listeners; catalog discovery of listeners.

## Rich experience
Wallboard full table; mobile red badge; chat sidebar mini-list; simultaneous update; layout chooser per pane.

## Failure / adversarial cases
- Slow dashboard blocks others → delivery isolation; per-receiver outbox/retries; one poison handler doesn’t stall others beyond its grain.
- Missed event while deactivated → rehydration from incident snapshot ask on activate, not only live events.
- Cross-owner incident broadcast → context isolation.
- Feedback loop if dashboard Emits something IncidentDesk treats as open → careful kind separation.

## Capability claim
DigitalBrain’s broadcast nervous system lets many UIs bind the same fact kind without rewriting the producer—unlike a chatbot that has one response channel per reply.
