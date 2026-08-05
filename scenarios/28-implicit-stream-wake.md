# Scenario 28: Implicit stream subscription wakes neuron on first event

## User intent
The owner installs a “Slack reaction → gratitude note” behavior. Until the first matching Slack event arrives, the gratitude neuron may be dormant (deactivated grain). The first event must wake it, deliver the fact, and journal both reception and any follow-on emits without a manual “start” click.

## Trigger
External Slack webhook / stream producer publishes `SlackReactionAdded` into an implicit stream tied to owner context; no prior grain activation.

## Imagined modules
- SlackAdapter (stream producer)
- GratitudeNotes behavior (consumer neuron)
- Memory (store note)
- UiProjector (toast “Noted thanks to @x”)
- Stream topology helpers in Kernel/Core hosting

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| SlackIngress / workspace | Maps webhook → stream event / Email-like fact |
| GratitudeNotes / default | Dormant until first SlackReactionAdded |
| Memory / life | Hears NoteCaptured |
| UiProjector / shell | Hears UiSurface |
| StreamBinder / host | Declares implicit subscription for kind↔stream |

## Synapse choreography
1. Catalog activation registers GratitudeNotes as `INeuron<SlackReactionAdded>` and binds an implicit stream consumer for that fact kind + context.
2. Grain is inactive; no timer, no chat.
3. SlackIngress publishes the first `SlackReactionAdded` onto the implicit stream (broadcast semantics for all declaring kinds in context).
4. Orleans activates GratitudeNotes; Core delivers the synapse as a normal turn: journal fact + handler.
5. Handler Emits `NoteCaptured` (broadcast) and `UiSurface(Toast)`.
6. Memory and UiProjector activate/hear as usual; causal chain links stream message id → journal sequence.
7. Subsequent reactions hit the warm grain with the same subscription; deactivation after idle still re-wakes on next event.

## Orleans / Core surface exercised
Implicit streams; grain activation on stream message; DurableGrain journals survive deactivation; pub-sub / catalog binding for “who hears this kind”; placement of newly woken grains; delivery filters on stream ingress.

## Rich experience
First reaction shows a toast even after days of silence; Behavior Studio “last woken” timestamp; journal starts at sequence 0/1 for that grain with provenance from stream, not chat.

## Failure / adversarial cases
- Poison message wakes grain then throws → poison must not infinite-reactivate loop; DeliveryFailed / dead-letter policy.
- Two contexts share stream id → cross-owner leak; stream identity must include owner/context.
- Handler re-enters Ask that circles back to same neuron in one turn → reentrancy deadlock; design must emit and continue on next turn.
- At-least-once stream redelivery → handler must be idempotent against journaled Slack event ids.

## Capability claim
DigitalBrain treats “asleep until the world speaks” as a first-class nervous-system pattern via streams and journals, not a long-polling bot process the user must keep warm.
