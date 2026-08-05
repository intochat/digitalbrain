# Scenario 36: Scripting: user writes neuron that reacts to all EmailReceived

## User intent
In Behavior Studio, the owner authors a small behavior: on every `EmailReceived`, if subject contains “Invoice”, extract amount and create a finance task + UI chip. They install it; from then on it participates as a real neuron kind—no deploy pipeline, no engineer.

## Trigger
Behavior Studio save → compile → `BehaviorPackageActivated`; subsequent Gmail webhooks.

## Imagined modules
- Behavior authoring (Monaco) + compiler host
- Behavior runtime (script neuron host)
- GmailAdapter
- Tasks / Finance tags
- UiProjector
- Capability catalog update

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| BehaviorStudio / editor | Emits source saved facts |
| BehaviorCompiler / host | Emits BehaviorPackageCompiled |
| ScriptHost / invoice-catcher | Dynamic neuron kind hearing EmailReceived |
| GmailIngress / inbox | Broadcasts EmailReceived |
| TaskStore / personal | Creates finance tasks |
| UiProjector / shell | Invoice chips |
| PolicyGate / safety | May refuse unsafe APIs in scripts |

## Synapse choreography
1. Owner writes handler shape equivalent to `INeuron<EmailReceived>`; Studio Emits `BehaviorSourceSaved`.
2. Compiler validates sandbox APIs; `BehaviorPackageCompiled` or `BehaviorCompileFailed` (UI).
3. On activate: catalog registers ScriptHost kind; `BehaviorPackageActivated` broadcast.
4. EmailReceived arrives (broadcast); ScriptHost turn: journals, maybe Ask `ParseInvoice`, Emit `FinanceTaskProposed` / on auto policy `TaskCreate`.
5. TaskStore and UI hear as with any module—script is not a side channel.
6. Owner edits script → hot-reload (scenario 26) version N+1.
7. Deactivate removes Connect bindings; in-flight emails complete under old version rules.

## Orleans / Core surface exercised
Module catalog dynamic registration; DurableGrain journals for script neurons; grain call filters sandboxing; serialized turns; outbox; grain versioning if host upgrades; placement of behavior host workers.

## Rich experience
Studio with live “last 5 emails matched” journal preview; chip on mail list; errors as `ScriptHandlerFaulted` cards with stack redacted; test-send sample EmailReceived button.

## Failure / adversarial cases
- Script calls forbidden network → PolicyGate ConnectionRefused / runtime fault journaled.
- Script infinite Emit loop → Core rate/depth guards; DeliveryFailed storm detection.
- Script answers an Ask kind already answered by a module → boot/activate must fail loud (ambiguous answerer).
- Prompt-like eval of email body executing code → sandbox never evals body as code; body is data only.

## Capability claim
DigitalBrain lets owners install new nervous-system listeners as behaviors that journal and compose like shipped modules—not chatbot “custom instructions” that cannot reliably bind to EmailReceived.
