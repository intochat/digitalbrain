# Scenario 35: Self-heal: DeliveryFailed triggers alternate route

## User intent
A behavior posts meeting summaries to Slack primary channel; when Slack is down or the neuron is mis-addressed, the owner still wants the summary in email and a red banner—without manual reruns. Self-heal should be a listener on Core delivery outcomes, not a hidden try/catch in one mega-agent.

## Trigger
Normal flow Emits `PostSlackSummary`; delivery exhausts retries → Core journals `DeliveryFailed` on sender; heal behavior hears it.

## Imagined modules
- MeetingSummary behavior
- SlackPoster neuron
- EmailFallback neuron
- Alert UI
- Connectivity / health optional

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| Summarizer / meeting-9 | Emits summary facts |
| SlackPoster / primary | Intended receiver |
| HealRouter / meeting-9 | Hears DeliveryFailed; chooses alternate |
| EmailFallback / default | Sends mail summary |
| UiProjector / shell | Failure + healed banners |
| Catalog / host | May refuse bad Connect |

## Synapse choreography
1. Summarizer Emits directed synapse to SlackPoster / or Connect-based route for `SummaryReady`.
2. Transport fails (receiver down, serialization, timeout) → after attempts, sender journals `DeliveryFailed(Fact, Receiver, Reason, Attempts)`.
3. HealRouter implements `INeuron<DeliveryFailed>`: if reason/receiver match policy, Emit `SummaryReadyEmail` / Ask EmailFallback, and `UiSurface(DegradedPath)`.
4. EmailFallback succeeds → `EmailDispatched`; HealRouter Emits `RouteHealed`.
5. Optional: Schedule later `RetrySlack` via `Schedule` fact; on `ScheduleFailed`, escalate.
6. If Connect was wrong, earlier `ConnectionRefused` may short-circuit to heal without retries.
7. Audit overhears both failed and healed paths under one correlation.

## Orleans / Core surface exercised
Outbox durability; DeliveryFailed as listenable Core synapse; Schedule/Unschedule; grain call filters; DurableGrain journals; Connect/Disconnect topology; reminders for delayed retry.

## Rich experience
Banner: “Slack unreachable—sent email instead”; actions Retry Slack / Open journal; timeline shows failed edge in red, healed edge in amber.

## Failure / adversarial cases
- HealRouter fails too → cascading DeliveryFailed must not infinite loop; cap with Attempts and dead-letter fact.
- Heal posts email and later Slack both succeed → duplicate customer noise; journal gate `AlreadyDeliveredExternally`.
- Malicious DeliveryFailed injection → only Core emits DeliveryFailed; modules cannot forge it as transport truth.
- Reentrancy: heal handler must not Ask the same Summarizer in-turn if Summarizer awaits deliver.

## Capability claim
DigitalBrain makes failure a first-class synapse other neurons can heal from—unlike chatbots where tool errors die inside one opaque call stack.
