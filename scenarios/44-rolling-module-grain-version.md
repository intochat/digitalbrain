# Scenario 44: Rolling upgrade of a module grain version

## User intent
Ops upgrades the CRM module from v1 to v2 grain interface while the silo keeps serving. In-flight CRM asks must complete; new activations use v2; journals remain readable; owners should see a brief “module upgrading” only if degraded, not a brain outage.

## Trigger
Rolling silo deploy / grain versioning rollout of CrmActivity grain implementation.

## Imagined modules
- CRM module v1/v2
- Catalog / hosting
- Dependent behaviors (meeting notes, nightly batch)
- Health/Ui status
- Compatibility shims if needed

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| CrmActivity / default | Versioned grain implementation |
| MeetingNotes / … | Caller of CRM asks |
| Catalog / host | Binds interfaces |
| UpgradeWatcher / ops | Hears ModuleVersionChanged |
| UiStatus / shell | Optional banner |
| JournalStore | Durable across versions |

## Synapse choreography
1. Pre-upgrade: MeetingNotes Ask `AppendCrmNote` → v1 handler answers.
2. Deploy starts: Orleans grain versioning / placement moves new activations to v2 code.
3. In-flight v1 turns finish on existing activations; answers still deliver via outbox.
4. Catalog Emits `ModuleVersionChanged(CrmActivity, v1→v2)` broadcast for ops.
5. New AppendCrmNote asks hit v2; if payload schema evolved, v2 accepts versioned bodies or emits `SchemaRejected` clearly.
6. Nightly batch mid-flight resumes from journal after node bounce without duplicate notes (idempotency keys stable across versions).
7. UpgradeWatcher clears degraded flag when health asks succeed.

## Orleans / Core surface exercised
Grain versioning; placement; DurableGrain journal compatibility; outbox; grain call filters; serialized turns during upgrade; module catalog; cluster membership (implicit).

## Rich experience
Ops topology view: module version badges; owner-facing only if CRM section degraded in brief; journal viewer still shows historical AppendCrmNote facts.

## Failure / adversarial cases
- Breaking serialization → DeliveryFailed / SchemaRejected, heal path (scenario 35), not corrupt journal.
- Two versions both answering same Ask kind in one catalog → ambiguous answerer boot failure.
- Sticky old activation forever → placement/rebalancing policies.
- Journal replay into v2 handler with v1-only fields → explicit migration or ignore-unknown rules tested.

## Capability claim
DigitalBrain can roll module versions under live fact traffic with journals and outbox as the compatibility backbone—unlike restarting a single chatbot process and losing mid-flight tool state.
