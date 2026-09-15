# Grok planning session: composition

Independent planning input, not the final implementation contract. See ../PLAN.md for reconciled decisions. Generated from bounded source excerpts after the first read-only CLI runs ended without usable results.

# DigitalBrain v2 plan: registered declarative behaviors

DigitalBrain already has the durable actor substrate. v2 adds a **saved, validated capability graph** on top of it. It does not add a second runtime, descriptor system, or host.

| Layer | Keep as-is | Add (thin) |
|---|---|---|
| Durability | `Neuron` / `Neuron<TState>`, journals, reminders, `Announce` outbox, `AppliedBy` | Behavior snapshot + desired lifecycle |
| Graph | `Fire` / `Connect` / `Deliver`, untyped signal bodies | Optional synapse **owner lease**; no new edge types |
| Typed calls | `DescriptorTable`, `INeuronInvoker`, `ArgumentContract`, `CommandExecution` | Action processor that **only** calls these |
| Composition today | `RecipeNeuron` + CompositionSteps (dirty work) | Treat as a **registered capability**; reuse mapping helpers |
| Agents | `BrainTools` on Session/Agent | Compose via `describe`/`call` on `IBehavior`; **no** new agent tools for graph execution |

NewProgrammingModel’s in-memory host, descriptors, and actor loop stay out. What transfers is the **product shape**: compose registered capabilities, reject bad edges before start, filters/maps as nodes, AI emits structured decisions, ownership is per capability, resume from persisted definition.

---

## Constraints (non-negotiable)

1. **Compose registered capabilities only.** A saved definition names capability ids + config. No generated C#, no ad-hoc grain types, no agent-invented processors.
2. **AI decision neurons do not act.** They output a schema-checked decision payload and `Announce` it. They do not receive `BrainTools` (`fire`/`connect`/`call`/`cancel`).
3. **Incompatible edges fail before start.** Runtime still validates inbound payloads and AI outputs; it does not “discover” contract bugs after activation.
4. **Filters and maps are neurons**, never edge metadata or implicit transforms.
5. **Ownership is per capability registration** (`Owned` vs `Shared`). Start/stop may create/destroy only owned processors and only disconnect **leases this behavior created**.
6. **Persist definition + desired lifecycle; resume by reconciling the same identities.** Retry with stable `CommandId` / `SignalId` / resource ids. Non-idempotent unknown outcomes **pause** that node; do not mint a new id.
7. **Preserve RecipeNeuron, NeuronInvoker, CompositionSteps, and their tests.** Do not convert historical recipes/signals. New examples use behaviors; old `Instruct` recipes keep working.
8. **No new framework.** No NewPM import, no production DB migration, no provider account setup as part of this work. Simulated providers only in tests.
9. **Keep aliases, storage keys, descriptor methods, conversational tools, modules, Flutter routes.**

---

## Current graph vs NewPM (what actually differs)

**DigitalBrain now:** neurons exist when named; a synapse is `(from, to, signalType)` with JSON bodies; the agent freely `connect`/`fire`/`call`; `RecipeNeuron` interprets one standing JSON recipe (`on` / `ask` / `call` / `read` / token substitute) inside a single grain.

**NewPM-style behavior:** a versioned definition of **roles** bound to **registered capabilities**, ports with schemas, directed connections, owned vs shared resources, start/stop as lifecycle, operators as nodes.

**Mapping (smallest):** a behavior is one durable `Neuron<BehaviorState>`. Its definition is data. Start materializes owned grains at deterministic ids, leases synapses on the existing graph, and configures processors **before** opening upstream delivery. Shared nodes keep their original `NeuronId` and config. Execution is ordinary `Deliver` → reaction → `Announce`/`INeuronInvoker`. Stop removes only this behavior’s leases.

RecipeNeuron is **not** replaced. It is capability `recipe` (and CompositionSteps remain the standing multi-step form). A behavior may include a recipe node; existing recipe tests stay on `GrainType("recipe")`.

---

## Smallest module surface

Four types, one catalog, one validator, one grain. Processors are ordinary neurons.

```
CapabilityCatalog          // module registration, not a runtime
BehaviorDefinition         // serializable graph (roles, edges, contracts, revision)
BehaviorValidator          // pure: schema, ownership, config, cycles of required edges
IBehavior / BehaviorNeuron // save / validate / start / stop / read  (commands)
FilterNeuron, MapNeuron, ActionNeuron, DecisionNeuron  // owned processors
Synapse.Owner              // optional lease; Disconnect enforces it
```

Do **not** add: a composition DSL compiler, a new grain base besides `Neuron<TState>`, a parallel invoker, edge-time mapping, or agent tools that execute the graph.

---

## Contracts (practical, serializable)

**Capability (registered at module load, like `ModuleManifest`):**

- `CapabilityId` (stable string)
- `GrainType` (existing Orleans grain type, e.g. `recipe`, `filter`, `map`, `action`, `decision`, plus module types already on the silo)
- `Lifetime`: `Owned` | `Shared`
- `ConfigSchema` (JSON Schema)
- `Inputs[]` / `Outputs[]`: `{ Port, SignalType, PayloadSchema }`
- `AllowsManualConnect`: default true for Shared, false for Owned processors
- Optional `Effect`: `Idempotent` | `Uncertain` (Action/external ingress)

**Behavior definition:**

- `BehaviorId`, `Revision` (monotonic; save of a running definition assigns a new revision; start uses that revision in owned ids)
- `Nodes[]`: `{ Role, CapabilityId, Config, ResourceId? }`
  - `Owned`: `ResourceId` omitted; runtime assigns `behavior:{id}:r{revision}:{role}`
  - `Shared`: `ResourceId` required; must already exist; config must be empty or byte-equal to live config (never overwrite)
- `Edges[]`: `{ FromRole, FromPort, ToRole, ToPort }`
- Resource identity ≠ role. The same shared neuron may appear in many behaviors; owned processors are 1:1 with `(behavior, revision, role)`.

**Compatibility (Validate, also re-run at Start):**

1. Every `CapabilityId` is in the catalog.
2. Config validates against `ConfigSchema`.
3. Edge ports exist; `SignalType` equal.
4. **Payload schema:** from-output is compatible with to-input (draft-07 subset: type, required, additionalProperties, enum, format, `$ref` local only). Incompatible → reject, no resources touched.
5. Owned roles do not point at an existing foreign resource id.
6. Two behaviors cannot both claim `Owned` on the same resource id.
7. Decision outputs must declare a payload schema; Action methods must resolve via existing `INeuronInvoker` + `ArgumentContract`.

**Runtime message validation:** same schemas on `Deliver` into filter/map/action/decision; Decision model JSON must match output schema before `Announce`. Failure: do not announce downstream; record pause reason on the processor snapshot.

---

## Ownership and synapses

Extend `Synapse` with optional `Owner` (`BehaviorId` + `Revision` + `Role`). Kernel `Connect` without owner = **manual** (today’s behavior). Behavior start uses an owned connect path.

- `Disconnect` of a leased synapse by anyone except that owner → reject.
- Stop deletes only this behavior’s leases. Manual and other-behaviors’ synapses stay.
- Shared sources: lease **outbound** edges this behavior added; never `Disconnect` edges it did not create; never change shared neuron config/state.
- RecipeNeuron synapses created outside a behavior remain manual.

This is the only kernel graph change. Do not change `Fire`/`Deliver` admission, journals, or command phases.

---

## Lifecycle (reuse commands + snapshot, no new durability)

`BehaviorNeuron : Neuron<BehaviorState>, IBehavior`

Commands (go through `CommandExecution` as every other mutating method):

| Command | Effect |
|---|---|
| `Save(definition)` | Persist desired definition; if Running, does **not** hot-swap owned ids (next Start uses new revision) |
| `Validate()` | Catalog + schema + ownership; no remote effects |
| `Start(commandId)` | Validate → persist `Desired=Running` → configure owned processors → **then** open upstream leases |
| `Stop(commandId)` | Persist `Desired=Stopped` → remove this behavior’s leases → owned processors reject new work |
| `Read()` | Definition, revision, lifecycle, resource ids, leases, per-node pause reasons |

Rules:

- Validate before any create/connect.
- Persist desired lifecycle **before** remote effects (crash-safe because `CommandPhase.Attempted` + snapshot).
- Recovery (`OnActivate` / first reaction): reconcile desired vs actual using the **same** resource ids. Missing owned grain → recreate. Extra owned lease from this revision → remove. Never invent new ids.
- Start/Stop retries use the **same** `CommandId`. `CommandExecution` already replays Completed / throws on Attempted-unknown.
- Stopped owned processors: `ReceiveAsync` returns without announcing (or reject admission if you already have a typed rejection); they keep journals.
- Uncertain action (`Effect=Uncertain`) whose command is `Attempted` with no terminal record: **pause node**, surface `CommandOutcomeUnknownException` on Read; operator/assistant retries **same** command id or explicitly resolves. Do not auto-retry with a new id.

Deterministic Action `CommandId`: hash of `(behaviorId, revision, role, causation SignalId, method alias)` so Deliver retries hit `CommandDedup`.

---

## Processors (owned grains, `Neuron<TState>`)

Reuse `SaveAsync` + `Announce`. No custom persistence.

1. **FilterNeuron** — config: registered predicate id or JSON-schema assertion on the inbound body. Match → announce same signal type/body to outputs. Miss → drop (still journaled via Deliver).
2. **MapNeuron** — config: JSON template + tokens. **Extract** `RecipeNeuron.Substitute` / `Field` / `$signalId` into a shared helper used by both Map and Recipe. Do not fork mapping semantics. `$commandId` in recipes stays a **new** guid per recipe call (existing behavior); ActionNeuron uses deterministic ids (above). Document that difference; do not silently change RecipeNeuron’s `$commandId`.
3. **ActionNeuron** — config: `{ neuron, interface, method, argsTemplate }`. Map inbound → args → `INeuronInvoker.InvokeAsync`. Unknown/Failed → pause. No LLM, no BrainTools.
4. **DecisionNeuron** — config: `{ prompt, outputSchema, model settings already used by AI module }`. Input signal + prompt → model → **JSON only** → schema validate → `Announce` `Decision` (or configured output port). **No tool loop.** Invalid JSON → pause, no announce.

**RecipeNeuron:** register as capability `recipe` (`Lifetime=Owned` or allow Shared on an existing recipe grain). `Instruct` body remains the recipe document (CompositionSteps stay the structured form of that document if that is what the dirty branch already uses). Behavior start may `call`/signal `Instruct` to configure it **only if** the node is Owned by this behavior; Shared recipe nodes are bind-by-id only.

Do not implement Twitter/provider in the same slice as the composition kernel unless the integration planner already has a receipt grain that is just another **Shared** source capability (`postId` as stable identity). If added: simulated provider in tests; real ingress stays existing authenticated routes.

---

## Assistant surface (no new tool vocabulary)

Keep the seven BrainTools. Expose behavior via **existing** `describe`/`call`:

- `IBehavior` methods get descriptors like every other module grain.
- Add **catalog read** as a read-only method on a well-known grain or on `IBehavior` (`ListCapabilities`) so the agent can compose **only** what is registered.
- Guidance: agent saves a definition, `Validate`, `Start`. It does **not** `connect` owned processors by hand and does **not** give DecisionNeuron tools.

AgentNeuron remains the conversational actor **outside** running behaviors. Inside a behavior, reasoning is DecisionNeuron.

---

## File-by-file (stay inside this set)

| File | Change |
|---|---|
| `src/Kernel/DigitalBrain.Contracts/Behaviors/BehaviorDefinition.cs` | **New.** Records: definition, nodes, edges, ports, schemas, revision. |
| `src/Kernel/DigitalBrain.Contracts/Behaviors/IBehavior.cs` | **New.** Save/Validate/Start/Stop/Read + list capabilities. Command types with `CommandId`. |
| `src/Kernel/DigitalBrain.Contracts/Behaviors/CapabilityDescriptor.cs` | **New.** Registration: lifetime, schemas, effect class. |
| `src/Kernel/DigitalBrain.Contracts/Synapses/Synapse.cs` (or current synapse record) | Optional `Owner`. Serializer alias **additive** field only; old synapses remain ownerless. |
| `src/Kernel/DigitalBrain/Behaviors/CapabilityCatalog.cs` | **New.** Filled from module manifest / `DigitalBrainRuntime.Add`. |
| `src/Kernel/DigitalBrain/Behaviors/BehaviorValidator.cs` | **New.** Pure compatibility + ownership checks. No grains. |
| `src/Kernel/DigitalBrain/Behaviors/BehaviorNeuron.cs` | **New.** `Neuron<BehaviorState>`; lifecycle reconcile; lease connect/disconnect. |
| `src/Kernel/DigitalBrain/Behaviors/Processors/*.cs` | **New.** Filter, Map, Action, Decision — each a small `Neuron<TState>`. |
| `src/Kernel/DigitalBrain/Neuron/RecipeNeuron.cs` | **Keep.** Extract mapping helper; register as capability; do not change Instruct/`$commandId` semantics. |
| CompositionSteps (uncommitted, same branch) | **Keep and wire** as the typed/structured recipe document (capability config or Instruct body). No discard, no silent migration of stored recipes. |
| `src/Kernel/DigitalBrain/Descriptors/NeuronInvoker.cs` | **Unchanged** except ActionNeuron consumes it. No second invoker. |
| Neuron Connect/Disconnect implementation (the grain that backs `INeuron`) | Honor synapse owner on disconnect; add owned-connect used only by BehaviorNeuron. |
| `src/Modules/AI/...` Decision path | DecisionNeuron uses existing AI client **without** `BrainTools.For(...)`. Do not teach AgentNeuron to run behaviors via new tools. |
| `src/Kernel/DigitalBrain/Commands/CommandExecution.cs` | **No redesign.** Action/Start/Stop rely on existing Attempted/replay/unknown. |
| `src/Testing/DigitalBrain.Testing/BrainSimulation.cs` | Use existing file-backed restart. No NewPM test host. |

Register new grain types in the existing module/runtime add path (same place `recipe` is registered). Do not touch Flutter routes.

**Out of this slice:** real Twitter credentials, dashboard redesign, replacing AgentNeuron, changing signal type vocabulary.

---

## Numbered implementation steps

1. **Freeze dirty work on `v2`.** RecipeNeuron, NeuronInvoker, CompositionSteps, composition tests remain compiling. Add behavior files beside them; do not rewrite recipe tests to behaviors.

2. **Record baseline.** Run the existing DigitalBrain test executable (Microsoft Testing Platform) and solution build with `CodeGraphRefresh=false`. Save pass/fail. Opt-in external tests: report skipped vs available, do not enable accounts.

3. **Additive contracts only.** `BehaviorDefinition`, `IBehavior` commands, `CapabilityDescriptor`, `SignalContract`. JSON Schema subset documented in one place (validator). Serializer aliases new (`db.v2.behavior.*`); do not reuse or break `db.v3.snapshot`1`` / `db.v3.neuron`.

4. **Catalog.** Register built-ins: `filter`, `map`, `action`, `decision`, `recipe`, plus existing module grains you explicitly opt in as Shared sources (e.g. counters). Module authors register; the agent cannot.

5. **Validator + tests (no silo).** Incompatible types, missing required, additionalProperties, wrong signal type, Shared without resource id, Owned colliding with existing id, Decision without output schema, Action method not on `DescriptorTable`.

6. **Synapse owner field + Connect/Disconnect tests.** Manual connect unchanged. Owned disconnect rejected. Stop removes only owner leases.

7. **`BehaviorNeuron` lifecycle** against `BrainSimulation` file storage: Save → Validate → Start → RestartSilo → still Running with same resource ids → Stop → restart → still Stopped, owned processors reject, shared+manual edges intact. Repeated Start/Stop with **same** command ids (idempotent) and **new** ids after completion (fresh incarnation).

8. **Extract mapping helper** from RecipeNeuron; MapNeuron uses it. RecipeNeuron tests must stay green **without** expectation changes for `$commandId` / `ask` / `call`.

9. **Filter/Map/Action processors.** Action uses invoker + deterministic command id. Duplicate Deliver (`AppliedBy`) does not double-invoke. Uncertain Attempted-after-crash pauses; Read shows unknown; retry same id.

10. **DecisionNeuron.** No tools in the request. Bad JSON / schema miss → pause, no downstream Announce. Good JSON → structured Decision only.

11. **Wire CompositionSteps** as recipe/behavior config (whatever the dirty type already is): a behavior node can point at capability `recipe` with that document. No automatic conversion of historical `Instruct` signals.

12. **Assistant path.** Descriptors on `IBehavior` + `ListCapabilities`. Existing `describe`/`call` only. Do not add an eighth BrainTool unless descriptors cannot list a well-known behavior grain; prefer a named neuron `behavior:{id}`.

13. **Examples (simulated only).** One internal-event graph: shared source → filter → map → action. One decision graph: source → decision → map → action. Optional simulated receipt source with stable post ids **if** the integration surface already exists; otherwise stub a Shared neuron that `Fire`s a `Receipt` signal in tests.

14. **Regression + evidence.** Full existing suite + focused v2 tests. Build `CodeGraphRefresh=false`. Document: no silent recipe migration; real providers still require existing ingress config (not this PR).

15. **Docs only after evidence.** Update architecture/context: behaviors are saved graphs; agents compose catalog entries; DecisionNeuron has no tools; leases vs manual synapses.

---

## Tests (must exist before calling the slice done)

Use `BrainSimulation` with `PersistenceDirectory` + `RestartSiloAsync` for every durability claim.

**Contracts / start rejection**

- Edge payload incompatible → `Validate` and `Start` fail; no grains created; no synapses added.
- Signal type mismatch → same.
- Unknown `CapabilityId` → fail.
- Shared node missing `ResourceId` or config would overwrite → fail.
- Owned id collision with another behavior or a pre-existing foreign neuron → fail.

**Ownership**

- Behavior A cannot `Disconnect` B’s leased edge or a manual edge.
- Stop A leaves B’s leases and manual `connect` from tests intact.
- Two behaviors can **share** one source; each only removes its own outbound leases.

**Lifecycle / resume**

- Start, kill silo, restart: definition, revision, resource ids, leases, pending announcements resume (`Neuron<TState>` outbox).
- Stop, restart: owned processors do not announce new work; shared source still readable.
- Start/Stop retried with the same `CommandId` after Completed → replay, no second materialize.
- Start crash during Attempted → `CommandOutcomeUnknown`; reconcile does not mint new owned ids.

**Processors**

- Filter drops non-matching; matching delivers once; re-Deliver same `SignalId` is idempotent (`AppliedBy`).
- Map token substitution matches RecipeNeuron for `$read`/`$length`/`$signalId` via shared helper; recipe `$commandId` still unique per recipe invocation.
- Action duplicate event → one `INeuronInvoker` call (command dedup).
- Action uncertain unknown → node paused; no new CommandId.

**Decision**

- Output is JSON matching schema; downstream sees Decision body only.
- Model/tool-shaped output or extra properties (if `additionalProperties: false`) → pause, zero downstream fires.
- DecisionNeuron host must not register `BrainTools`.

**Preserve dirty composition**

- Existing RecipeNeuron / CompositionSteps / NeuronInvoker tests: **same assertions**.
- Historical recipe `Instruct` still interpreted; not rewritten into `BehaviorDefinition`.

**Non-goals to assert**

- No NewPM types in the DigitalBrain silo.
- Solution build does not require CodeGraph refresh.
- Simulated provider tests never call a live account.

---

## Risks and limitations

- **Synapse owner is a kernel protocol change.** Must be additive serialization; old snapshots without `Owner` = manual. If Connect is only `(target, type)`, owned connect needs a kernel-internal path so agents cannot spoof `Owner` through BrainTools. Do not add `owner` to the public `connect` tool.
- **Schema subset will not equal full JSON Schema / NewPM.** Conservative: reject on unknown keywords rather than ignore. Document the subset.
- **Revision vs live owned ids.** Saving a new revision while Running must not relocate live processors until Stop/Start; otherwise in-flight `AppliedBy` and command dedup keys break. First version: Start of a new revision requires Stop (or explicit rolling replace later — **not** this slice).
- **Recipe `$commandId` vs Action deterministic ids.** Changing recipe tokens would break existing tests and retry semantics. Keep both; document.
- **DecisionNeuron still needs the AI module and keys.** Tests use a fake `IChatClient` / existing test double. No live model required for CI.
- **Shared module grains may lack config/schema.** Only catalog entries that declare contracts are composable. Everything else remains agent `call`/`fire` as today.
- **Pause is not human workflow UI.** It is state on the processor + Read. Inspection is `read`/`call` IBehavior.Read.
- **Provider receipts** are a Shared source + stable ids, not part of the composition kernel. Shipping them in the same change increases auth/config risk; keep simulated.

---

## What “done” means

A composing assistant can `call` `Save`/`Validate`/`Start` on an `IBehavior` whose nodes are **only** catalog capabilities; incompatible graphs never start; filter/map/action/decision run as neurons on existing durability; AI nodes emit schema-valid decisions without tools; ownership leases survive restart; RecipeNeuron/CompositionSteps still pass; no NewPM runtime and no generated C# in the product path.
