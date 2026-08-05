# Scenario 26: Behavior hot-reload while live emissions continue

## User intent
The owner is mid-conversation and mid-email flood when they edit a live behavior in Behavior Studio: change how “VIP email” is classified and what UI card is projected. They expect the next facts to obey the new rules without restarting the silo, without losing in-flight turns, and without double-applying the same email under both old and new logic.

## Trigger
Behavior Studio save + activate on a running behavior package while chat and Gmail adapters keep emitting.

## Imagined modules
- BehaviorHost / BehaviorCatalog (compile, version, activate neuron kinds)
- GmailAdapter (EmailReceived broadcasts)
- Chat / Assistant (UserMessaged, streaming replies)
- PolicyClassifier (VIP rules as a replaceable behavior)
- UiProjector (cards, banners)
- Outbox / Delivery audit listeners

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| BehaviorActivator / host | Accepts BehaviorPackageActivated; swaps catalog bindings for context |
| PolicyClassifier / owner-default | Hears EmailReceived; emits EmailClassified then UiSurface cards |
| GmailIngress / inbox | External webhook → EmailReceived broadcast in owner context |
| Chat / morning-desk | Ongoing UserMessaged turns; may Ask policy for enrichment |
| UiProjector / shell | Renders UiSurface; never holds classifier logic |
| DeliveryAuditor / silo | Overhears DeliveryFailed / Completed for activation windows |

## Synapse choreography
1. GmailIngress broadcasts `EmailReceived` (broadcast, context = owner session).
2. PolicyClassifier (version N) hears it, journals reception, emits directed `EmailClassified` to interested askers and broadcasts `UiSurface(VipCard)`.
3. Owner saves new script; BehaviorHost emits `BehaviorPackageCompiled` then `BehaviorPackageActivated(version=N+1, kind=PolicyClassifier)` (broadcast to catalog + shell).
4. Core catalog replaces the kind binding for new deliveries; in-flight turns on version N finish under N’s handlers (serialized grain turn).
5. Next `EmailReceived` is delivered only to PolicyClassifier N+1; N is disconnected (`Disconnect` reserved Core path) and may journal `ConnectionRefused` if stale wiring remains.
6. Chat turn already mid-Ask continues with original correlation; answers return as directed `Answer`/`reply` facts; no reentrancy into the same neuron mid-turn.
7. UiProjector hears only the surfaces from the active version; optional `BehaviorVersionSwitched` broadcast updates a “live rules” badge in the shell.

## Orleans / Core surface exercised
Serialized grain turns; grain call filters (incoming/outgoing synapse filters); DurableGrain journals; module catalog hot rebinding; outbox durability so emissions queued during swap still deliver once; Connect/Disconnect topology; request context for owner/context isolation.

## Rich experience
Behavior Studio split pane (source + live journal tail); chat continues streaming; shell shows “Classifier vN+1 active” toast; VIP card layout changes on the *next* email without app restart; timeline scrubber of EmailReceived → EmailClassified under each version.

## Failure / adversarial cases
- Two versions both subscribed → double VIP cards / double side effects: Core must ensure catalog has one answerer per ask kind and one active binding per behavior kind per context.
- Swap mid-handler: reentrancy or partial journal commit must not leave half-old classification durable as if new policy produced it.
- Disconnect of N while outbox still targets N → DeliveryFailed must surface, not silent drop; alternate route or retry must not invent ownership across brains.
- Malicious package activation from another owner’s marketplace install must be refused at catalog scope.

## Capability claim
DigitalBrain can rewire live nervous-system handlers under continuous fact traffic while journals preserve a single causal version timeline per turn—something a process-restart chatbot workflow cannot do.
