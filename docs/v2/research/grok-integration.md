# Grok planning session: integration

Independent planning input, not the final implementation contract. See ../PLAN.md for reconciled decisions. Generated from bounded source excerpts after the first read-only CLI runs ended without usable results.

# DigitalBrain v2 behavior composition — implementation plan

This plan is grounded in the eight inspected sources of truth. `docs/v2/PLAN.md` is the **product contract**, not runtime evidence. Runtime truth is the grain/command/tool/descriptor code. Do not import NewPM’s in-memory host. Do not add five new assistant tools. Do not touch Flutter HTTP routes except to keep them working.

---

## Sources of truth vs stale docs

| Artifact | Role |
|---|---|
| `src/Modules/AI/AI/BrainTools.cs` | Conversational assistant surface: exactly seven tools (`fire`, `connect`, `disconnect`, `read`, `cancel`, `describe`, `call`). This is what Flutter/MCP already bind. |
| `src/Kernel/DigitalBrain/Descriptors/NeuronInvoker.cs` | Generic typed-method registry. `describe`/`call` already discover and invoke any grain interface registered in `DescriptorTable`. **This is the only assistant seam for behavior commands.** |
| `src/Kernel/DigitalBrain.Contracts/Neurons/INeuron.cs` | Kernel verbs. Synapses are `Connect`/`Disconnect`; they have **no owner**. Ownership must be a layer above, not a kernel change. |
| `src/Kernel/DigitalBrain/Neuron/NeuronOfState.cs` | Snapshot + announcement outbox. `SaveAsync`/`Announce` are **reaction-only**. Commands must stage desired state, then a reaction performs remote effects. |
| `src/Kernel/DigitalBrain/Commands/CommandExecution.cs` | Durable, hashed, retryable commands with Attempted → Completed/Failed/Rejected. Start/stop **are commands**. Effects after persist. |
| `src/Kernel/DigitalBrain/Neuron/RecipeNeuron.cs` | Existing composition. Keep as-is. Do not convert historical `Instruct` bodies into behaviors. |
| `src/Testing/DigitalBrain.Testing/BrainSimulation.cs` | File-backed silo + `RestartSiloAsync`. Recovery tests go here. |
| `docs/v2/PLAN.md` | Agreed product rules. Treat checklist as work remaining, not implemented fact. |

**Do not treat as truth:** NewPM in-memory Orleans host, any doc that implies new MCP tools named `save_behavior` / `start_behavior`, any claim of live Twitter credentials, any silent Recipe→Behavior migration.

**Preserve without reinterpretation:** existing module integrations, signal type aliases, storage keys (`db.v3.snapshot\`1``, default grain storage), descriptor method aliases, conversational tools, Flutter HTTP routes, RecipeNeuron.

---

## Constraints (non-negotiable)

1. **No new AIFunction tools.** Discover/save/validate/start/stop/read are `IBehavior` methods on a `behavior` grain. The assistant uses existing `describe` + `call`.
2. **Conversational `AgentNeuron` keeps tools.** Graph `IAgent` nodes are a different grain (or a tool-less mode) that emit one schema-validated decision signal and never receive `BrainTools`.
3. **Filters/mappers are owned snapshot neurons**, configured by the behavior, not prompt text and not RecipeNeuron.
4. **Kernel `Connect`/`Disconnect` stay dumb.** Ownership is leases in behavior snapshot + a connection-lease neuron or per-source lease table. A behavior may only disconnect edges it leased.
5. **Commands never `SaveAsync`/`Announce`.** `Neuron<TState>` throws if they do. Lifecycle commands persist desired generation; a follow-up reaction/reminder does resource work.
6. **Shared vs owned:** shared sources keep their original `NeuronId` and config. Owned processors get deterministic ids `behavior/{behaviorId}/{revision}/{role}`.
7. **Validate before allocate, configure before open upstream, persist desired lifecycle before remote effects.** Recovery replays the same identities.
8. **No production DB wipe, no live provider config, no NewPM host.** Simulated Twitter only in this change. Live ingress must share the receipt contract.
9. **RecipeNeuron and direct `call` stay.** New examples use saved behaviors; historical state is untouched.
10. **Flutter / HTTP / MCP paths stay.** If a webhook is added, it is an adapter onto the same durable receipt command, not a second pipeline.

---

## Minimal seams (exact wiring)

### A. Assistant → behavior (no new tools)

`BrainTools` already does:

- `describe(neuron)` → `INeuronInvoker.Describe(NeuronId)` → `DescriptorTable.For(grainType)`
- `call(neuron, interface, method, args)` → `InvokeAsync` with command-id injection

**Add:** `[Alias]` interface `IBehavior` on grain type `behavior`, registered in the existing descriptor table the same way other module grains are. Assistant flow:

1. `describe` on `behavior:{name}` lists save/validate/start/stop/read (and any catalog methods).
2. `call` with that interface alias + method alias + JSON args.

**Capability discovery:** do **not** add a `discover` tool. Add a well-known catalog grain (e.g. `capability:catalog`) with a **read-only** `List`/`Get` method. Assistant: `describe` that grain, then `call` `List`. Catalog is filled at module registration time (same `ModuleManifest` path `BrainSimulation` already injects).

Do not bind behavior methods through `AgentNeuron` identity. `BrainTools.For(AgentNeuron, …)` stays session-scoped kernel ops. Behavior grains are ordinary named neurons the session `call`s.

### B. Graph agent vs conversational agent

| | Conversational `AgentNeuron` | Graph decision neuron |
|---|---|---|
| Tools | `BrainTools` seven ops | **none** |
| Input | chat / `Ask` | inbound signal + bound JSON schema |
| Output | free text + tool calls | `Announce` of a schema-validated body (e.g. `Decision`) |
| Persistence | existing agent snapshot/journal | `Neuron<TState>` snapshot of last decision + outbox |
| Invoker | may `call` modules | **must not** hold `INeuronInvoker` for tools; if it needs an action, that is a **separate owned action neuron** downstream |

If current `AgentNeuron` is one class that always builds tools, **do not fork the conversational path**. Add a distinct grain type (e.g. `decision`) that uses the AI client to fill a JSON schema only. Prompt: system instruction “emit JSON matching this schema; you have no tools.” Validate with the node’s output contract before `Announce`. Invalid model output → Failed command / visible unknown outcome, no fire.

### C. Owned processors

New grain types, all `Neuron<TState>`:

- `filter` — config: predicate over inbound JSON; pass-through `Announce` or drop.
- `map` — config: mapping spec (JSON Pointer / declared field map, not arbitrary code); `Announce` mapped body on configured output type.
- `action` — config: target neuron + interface + method; uses **existing** `INeuronInvoker` + **deterministic** `CommandId` derived from `(behaviorId, revision, role, inboundSignalId)` so retries are safe. Unknown/Attempted outcomes stay readable via `read`/`commands`.
- `decision` — tool-free structured IAgent as above.

Factory: behavior start materializes these under deterministic ids. They are **owned**: stop deactivates them (reject new work) and drops **only leased edges**. Snapshots remain (audit), they do not accept new deliveries while stopped.

### D. Ownership / leases (layer above `INeuron`)

`INeuron.Connect`/`Disconnect` have no owner. Behavior snapshot stores:

- node bindings: `role → NeuronId` (shared keeps caller’s id; owned is minted)
- leases: `(from, to, signalType, leaseId, behaviorId, revision)`

Start:

1. Validate definition + contracts.
2. Command persists `Lifecycle = Starting` + planned bindings/leases.
3. Reaction creates owned grains, applies config via their commands, then `Connect` for each edge and records the lease.
4. Persist `Lifecycle = Running`.

Stop:

1. Command persists `Lifecycle = Stopping`.
2. Reaction disconnects **only** rows in this behavior’s lease set; owned processors set `Accepting = false` and reject inbound; shared sources are left connected if another lease or a **manual** (unleased) edge exists.
3. Persist `Lifecycle = Stopped`.

A second behavior connecting the same shared source adds its own lease. Disconnect of a non-leased (manual) edge is refused. Collision: two behaviors claiming ownership of the same owned role id is a validate/start error (ids are deterministic, so this is a definition bug or overlapping names).

### E. Contracts

Serializable definition (new contracts assembly types, Orleans `[GenerateSerializer]` + JSON schema for `call` args):

- `BehaviorDefinition`: id, nodes[], connections[], desired lifecycle
- `BehaviorNode`: role, capabilityId, ownership (Shared|Owned), config JSON, in/out signal contracts (JSON Schema)
- `BehaviorConnection`: fromRole, toRole, signalType
- Resource identity ≠ role: shared Twitter source id stays `twitter:elon` (example); role in this graph is `source`.

Validation (pure, no grains required for the static pass):

- capability exists and allows requested ownership
- config matches capability config schema
- each connection’s signal type/schema is compatible (producer out ⊇ consumer in)
- no unknown roles, no self-edges unless capability says so
- shared nodes must already exist or be allow-listed sources; owned nodes must not collide with an existing **other** owner

Runtime: before `Announce`/action `call`, validate payload against the edge contract. Invalid → do not deliver; record visible failure.

### F. Capability registration

Extend the existing module registration (`ModuleManifest` consumed by `DigitalBrainRuntime.Add` in `BrainSimulation`) with a capability table:

- `CapabilityId`
- grain type for owned instances
- ownership policy (OwnedOnly / SharedOnly / Either)
- config schema, input/output contracts
- whether it is a source (ingress) vs processor

Modules that already register grains keep doing so. New filter/map/action/decision/twitter-sim capabilities register here. Catalog grain reads this table (process-local, reconstructed on silo start — **definitions** live in behavior snapshots, **capability metadata** is code).

### G. Twitter: simulated now, live later, one receipt contract

Do **not** configure real credentials. Add:

1. **Receipt command** on a source grain (shared): `IngestPost(PostId, Author, Text, At, …)` with `CommandId` = hash of provider+postId so duplicates replay.
2. On completed ingest, reaction `Announce`s a `Post` (or contracted type) to leased downstream edges.
3. **Simulated provider**: test/module helper that calls `IngestPost`. No HTTP required for v2 tests.
4. **Durable ingress adapter** (optional HTTP): if an existing webhook/MCP HTTP route convention exists, add one route that maps body → `IngestPost`. If not, **do not invent a Flutter route**. Leave a kernel-side command as the seam; live provider later posts to the same command. Document that live Twitter needs real auth and is out of scope.

Stable post id is the idempotency key. Restart must not re-announce a completed ingest (command replay returns stored result; `AppliedBy` on snapshot neurons skips duplicate reactions).

### H. RecipeNeuron

Leave `RecipeNeuron` and its `Instruct` JSON (`on` / `ask` / `call` / `length` / `read`) unchanged. Tests that compose via recipe stay green. New Elon-post / internal-event examples use `IBehavior`, not recipes.

---

## Proposed types and files (add, don’t reshape)

Keep existing modules and Flutter trees. Add beside them:

**Contracts (new, small)**

- `src/Kernel/DigitalBrain.Contracts/Behaviors/IBehavior.cs` — `[Alias]` interface: `Save`, `Validate`, `Start`, `Stop`, `Read` (all commands except `Read`/`Validate` as appropriate; `Read` `[ReadOnly]`).
- `src/Kernel/DigitalBrain.Contracts/Behaviors/BehaviorDefinition.cs` and related records (nodes, connections, leases, lifecycle).
- `src/Kernel/DigitalBrain.Contracts/Capabilities/CapabilityDescriptor.cs`

**Kernel**

- `src/Kernel/DigitalBrain/Behaviors/BehaviorNeuron.cs` — `[GrainType("behavior")] : Neuron<BehaviorState>`
- `src/Kernel/DigitalBrain/Behaviors/BehaviorContracts.cs` — schema validation
- `src/Kernel/DigitalBrain/Behaviors/ConnectionLeaseGuard.cs` — wrap connect/disconnect with lease checks (do not change `INeuron` signature)
- `src/Kernel/DigitalBrain/Capabilities/CapabilityTable.cs` — filled from module registration

**Owned processors (AI or a new Behaviors module; prefer a dedicated module so AI conversational code stays clean)**

- `src/Modules/Behaviors/` (or under AI only if registration is already there — **prefer new module** to avoid mixing tools with graph agents)
  - `FilterNeuron.cs`, `MapNeuron.cs`, `ActionNeuron.cs`, `DecisionNeuron.cs`
- Decision neuron: AI client **without** `BrainTools.For(...)`

**Twitter sim / ingress**

- `src/Modules/Integrations/` (or existing integration module folder): `TwitterSourceNeuron.cs` + `SimulatedTwitterProvider.cs`
- HTTP adapter **only** next to current webhook entry; same body → `IngestPost`. Do not add Flutter screens.

**Registration**

- Existing `ModuleManifest` / `DigitalBrainRuntime.Add` site: register grain types + capability rows + descriptor interfaces. Same hook `BrainSimulation` already uses.

**Do not edit except as needed for registration:** `BrainTools.cs` (no new tools), `RecipeNeuron.cs`, `INeuron.cs`, `NeuronInvoker.cs` (works as-is if `IBehavior` is in `DescriptorTable`), Flutter routes.

`NeuronInvoker` already rejects a wrong interface with “use one of: …”. Once `IBehavior` is on the `behavior` grain type, `describe`/`call` work with zero tool changes.

---

## Lifecycle on existing substrate

`BehaviorState` snapshot:

- definition + revision
- desired and observed lifecycle (`Stopped` / `Starting` / `Running` / `Stopping` / `Failed`)
- bindings, leases
- last error / unknown-outcome refs

`Start`/`Stop` commands:

1. Capacity/dedup via `CommandExecution`.
2. Validate (Start).
3. Persist desired lifecycle + generation in the command’s staged state **or** schedule a private signal and persist (command body cannot `SaveAsync` — use the pattern this kernel already uses for “command schedules work”: `CommandReaction.ScheduledWork` / Fire from command context if that is the supported path; if commands cannot Fire either, stage fields on the grain’s command-host persist as `CommandExecution` already calls `host.PersistAsync()`, then a reminder/turn-work reaction reads desired vs observed and converges).
4. After persist, `WakeTurnWorkAsync` (already in `CommandExecution`).
5. Reaction: create/configure owned nodes, lease-connect, then `SaveAsync` observed=Running and `Announce` lifecycle events if contracted.

Recovery: silo restart reloads snapshot; if desired ≠ observed, the same turn-work/reminder converges with the **same** owned ids and lease ids. No new identity on retry.

Stopped owned processors: `ReceiveAsync` returns without work (or admission rejects) when `Accepting == false`.

Uncertain action (`CommandPhase.Attempted` / `CommandOutcomeUnknownException`): do not retry with a new CommandId; surface on `Read` / commands journal; require an explicit resolve command later (out of minimal v2 if needed — at minimum **leave visible and do not double-effect**).

---

## Numbered implementation steps

1. **Baseline**  
   Run the existing DigitalBrain test executable (Microsoft Testing Platform) and a solution build with `CodeGraphRefresh=false`. Record failures. Do not “fix” unrelated dirt on `feat/standing-composition` except what this work needs.

2. **Contracts + schemas**  
   Add `BehaviorDefinition` and JSON schemas used both by `call` args and by `Validate`. Conservative: unknown fields fail closed. Unit-test validate: missing capability, schema mismatch, incompatible edge, shared/owned violation.

3. **Capability table**  
   Register filter/map/action/decision + twitter-sim in `ModuleManifest` path. Catalog grain read-only `List`/`Get`. Test: `describe` + `call` on `capability:catalog` returns ids and ownership policy **without** new AI tools.

4. **`IBehavior` grain**  
   `[GrainType("behavior")] Neuron<BehaviorState>` with Save (new revision), Validate, Read. Save persists definition only; does not start. Test: Save → silo restart via `BrainSimulation.RestartSiloAsync` → Read returns same definition.

5. **Lease-aware connect**  
   Implement lease records and a guard used only by behavior start/stop. Test: manual `connect` from conversational tools still works; behavior stop does not remove that edge; second behavior cannot disconnect the first’s lease.

6. **Owned filter + map**  
   Snapshot neurons, config via command, `Announce` from reaction. Test: fire inbound → filtered drop vs pass; map rewrites field; restart does not duplicate announce (`AppliedBy`).

7. **Owned action**  
   `INeuronInvoker` + CommandId `stable(behavior, revision, role, signalId)`. Test: invoke existing module method; retry same inbound does not double-apply; oversized/failed command is readable.

8. **Tool-free decision neuron**  
   AI call with schema, no `BrainTools`. Test: given a stub/fake chat client, output matches schema and is `Announce`d as `Decision`; a tool-shaped model output or extra keys **fails validation** and does not fire. Conversational agent tests still see seven tools.

9. **Start/Stop convergence**  
   Desired lifecycle persisted first; reaction allocates, configs, then opens upstream. Stop closes leased edges only, owned nodes reject. Test: repeated Start/Stop idempotent; crash between persist and remote effect (use existing crash-point style if present, else kill silo mid-start) recovers to the same ids.

10. **Twitter simulated source**  
    Shared source grain + `IngestPost` command keyed by post id. Simulated provider in tests. Test: duplicate post id replays; announce once; restart; leased filter→decision→action path for “react to Elon-like posts” **and** an internal-signal path (plain `fire` into a shared source neuron) without Twitter.

11. **HTTP/UI**  
    Confirm Flutter routes and MCP still bind only the seven tools. If and only if an existing webhook pipeline is the current ingress convention, add a thin adapter to `IngestPost`. Otherwise skip HTTP. No new UI. No credentials.

12. **Assistant guidance**  
    Update agent system prompt / tool descriptions only as text: “compose by `describe`/`call` on `behavior:…` and `capability:catalog`; do not emit code; graph agents have no tools.” Do not add tools.

13. **Regression + focused v2 tests** (below). Update architecture docs **after** tests pass, with evidence and “live Twitter not configured.”

---

## Tests (meaningful, not tautological)

Use `BrainSimulation` with `PersistenceDirectory` + `RestartSiloAsync` for durability. Keep opt-in external tests skipped and report them as unavailable.

| Test | What it proves |
|---|---|
| Catalog `describe`/`call` | Discovery uses existing tools; no new AIFunction. |
| Save/Read across restart | Definition is snapshot-durable. |
| Validate rejects bad contracts | Incompatible signal schemas never start. |
| Ownership collision | Two behaviors cannot own the same minted processor id. |
| Shared source + two behaviors | Independent leases; stop A leaves B’s edge. |
| Manual edge preserved | Conversational `connect` is not a lease; stop does not `Disconnect` it. |
| Filter drop vs pass | Owned filter is a neuron, not prompt logic. |
| Map rewrite | Config-owned mapper; contract validation on outbound. |
| Decision has no tools | Fake client never invoked with `BrainTools`; invalid JSON not announced. |
| Conversational tools unchanged | Agent still exposes exactly the seven ops (existing AI tests). |
| Action CommandId stability | Duplicate delivery / restart does not double-call. |
| Action unknown outcome | Attempted/unknown is readable; no silent retry with new id. |
| Start/Stop repeat | Idempotent; stopped owned node rejects new work. |
| Crash/restart mid-start | Same owned ids and leases; desired lifecycle converges. |
| Duplicate Twitter post id | One announce; command replay. |
| Elon-sim example | sim ingest → filter(author) → decision(schema) → action(`call` existing module). |
| Internal event example | `fire` into shared neuron → same graph pattern, no Twitter. |
| RecipeNeuron unchanged | Existing recipe tests still pass; no auto-migration. |
| Flutter/MCP smoke | Existing HTTP routes still serve; no new required UI. |

Run existing full suite after the focused tests. Build with `CodeGraphRefresh=false`.

---

## What not to do

- Do not add `save_behavior` / `start_behavior` tools next to `fire`/`describe`.
- Do not give graph `IAgent` `INeuronInvoker` “just in case.”
- Do not implement filters as RecipeNeuron `Instruct` JSON.
- Do not change `INeuron.Connect` to take an owner parameter (breaks existing tools and aliases).
- Do not import NewPM host or replace `BrainSimulation`.
- Do not convert stored recipes into behaviors.
- Do not claim Twitter is live or write credentials.
- Do not delete or migrate production storage.
- Do not add Flutter screens or rename existing module/HTTP paths.

---

## Suggested first PR slice

Contracts + capability catalog + `IBehavior` Save/Validate/Read + restart test + catalog via `describe`/`call`. No processors yet. This proves the assistant seam and durability before leases and AI.

Second slice: leases + filter/map + start/stop.  
Third: decision (tool-free) + action + twitter-sim + the two examples + full regression.
