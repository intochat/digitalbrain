# Scenario 05: Install a C# behavior that programs neurons/synapses

## User intent
The owner wants to author and install a C# behavior that listens for `EmailReceived` from a VIP domain and auto-creates a task plus a chat nudge — written against `INeuron<T>` / synapse types, compiled, activated, and proven live.

## Trigger
Shell Behavior Studio: save script + Activate; or chat "install behavior VIP-email-to-task".

## Imagined modules
- Behaviors (author, compile, load, activate, deactivate)
- BehaviorHost (isolated worker process boundary)
- Chat (nudge surface)
- Tasks (create task)
- Gmail (source facts)
- Module catalog / composition
- Shell UI (Monaco editor, activation status)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| behaviorstudio/owner | Edit buffer, compile asks, publish packages |
| behaviorhost/worker | Load assembly; host dynamic neuron kinds |
| behaviorcatalog/owner | Track installed behaviors and bindings |
| gmail/owner-inbox | Emit `EmailReceived` |
| tasks/owner | Answer `TaskCreateAsked` |
| chat/owner-desk | Show `BehaviorNudge` |
| shell/primary | Studio scene + status badges |

## Synapse choreography
1. Owner edits C# implementing e.g. `sealed class VipEmailToTask : Neuron, INeuron<EmailReceived>` with Handle that `Ask`s task create and `Emit`s nudge.
2. Studio **directs** `BehaviorCompileAsked` → compiler service neuron → `BehaviorCompileAnswered` (diagnostics|assemblyHash).
3. On success, owner activates → **directs** `BehaviorActivateAsked` → `behaviorcatalog/owner`.
4. Catalog **directs** `BehaviorLoadAsked` → `behaviorhost/worker`; host answers `BehaviorLoaded` (kind name minted, handlers declared).
5. Catalog **broadcasts** `BehaviorActivated` (behaviorId, listens=[EmailReceived], asks=[TaskCreateAsked], emits=[BehaviorNudge]).
6. Core module catalog refresh: new neuron kind participates in pub-sub for `EmailReceived`.
7. Live proof: VIP `EmailReceived` **broadcast** → behavior neuron hears → **directs** `TaskCreateAsked` → tasks → `TaskCreated`; behavior **broadcasts** `BehaviorNudge`; chat renders card.
8. Journals on behavior neuron show reception + emissions with Cause chain from email.

## Orleans / Core surface exercised
Module catalog hot registration; grain versioning/kind minting for dynamic types; placement of behavior grains; DurableGrain journals for dynamic neurons; grain call filters (owner + sandbox); serialized turns; outbox durability; process isolation via behavior host (not Core types beyond neuron delivery); boot-time answerer cardinality still enforced for any `Synapse<TReply>` the script claims to answer.

## Rich experience
Monaco editor with diagnostics underline; "Activate" / "Deactivate"; live test button injects synthetic `EmailReceived` under owner; topology view shows new edges; chat toast on first live fire.

## Failure / adversarial cases
- Script answers a question type already answered → activation must fail boot/catalog check, not dual-answer in production.
- Script awaits another neuron call style that re-enters self → hang; host must respect serialized turns / no neuron-await-neuron.
- Malicious script tries to emit as another NeuronId → Core mints Source metadata; cannot forge.
- Compile succeeds but Load fails → journal `BehaviorActivationFailed`; UI not "active".
- Deactivate mid-flight: in-flight asks still complete or cancel with journaled terminal fact; no silent orphan tasks without correlation.

## Capability claim
Users program the OS by shipping real `INeuron` handlers over synapses — behaviors are not prompt snippets; they are durable participants in the same nervous system as first-party modules.
