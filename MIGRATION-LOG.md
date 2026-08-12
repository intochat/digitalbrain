# Migration log — absorbing Cortex into DigitalBrain

Honest running record of the absorption: what was decided, what conflicted, what surprised us.
Newest session last. Branch: `cortex-absorption` (cut from `master` @ `2fd38423`).

---

## Session 1 — 2026-08-11 — reconciliation and sequence revision

No production source changed this session. Deliverable: reconcile the mission prompt against
the actual Orleans source, log the resolved conflicts, and revise the suggested sequence before
executing it (per `prompt.md` §"Suggested sequence").

Evidence base: first-hand reading of the kernel delivery core (`Neuron`, `NeuronTurnCoordinator`,
`NeuronJournal`, `NeuronFeed`, `NeuronOutbox`, `NeuronMessagePipeline`, `NeuronDeliveryMemory`,
`SynapseDelivery`, `DeliveryPolicy`) plus an eleven-area file-and-line survey of every subsystem
the absorption touches.

### 1. Resolved conflicts

Authority order applied: `prompt.md` → Cortex spec/DECISIONS as *semantics* → prior owner
directives → current source as evidence.

**C1 — The documented verification gate does not exist.**
`CLAUDE.md:18-19` prescribes `pwsh scripts/gate.ps1`. There is no `scripts/` directory; it was
deleted in `db529d2f` ("Delete trash") along with `GROK.md`, `UNIFIED-ARCHITECTURE.md`,
`INTERCONNECT-REVIEW.md`. `CLAUDE.md:6-7` still links to two of those deleted files.
*Resolved:* the gate for this refit is `dotnet build DigitalBrain.slnx -warnaserror --nologo`
(what CI actually runs, `.github/workflows/ci.yml:24`) + `flutter analyze lib` per affected Flutter
package + manual product smokes recorded here — exactly what `prompt.md` already specifies.
CLAUDE.md is stale and gets corrected in the documentation slice, not silently worked around.

**C2 — A delete-list item is already deleted.**
`prompt.md` lists "the `ShowTime`/`WantsTimeButton` keyword demo and its transform". A repo-wide
grep over `*.cs` and `*.dart` returns nothing. `CLAUDE.md`'s "W2 is complete" is accurate; the
mission's delete list is stale on this one item. *Resolved:* no work, no deletion.

**C3 — "Tasks module" does not exist.**
The delete list names "Tasks-module worker/attempt machinery". There is no project, folder, or
namespace called Tasks in `src/**`. The referent is unambiguously `src/Modules/Execution`
(~3,364 lines), whose own description string reads "durable execution lifecycle, operation ledger,
and worker attempt coordination". *Resolved:* the item is re-scoped in the revised sequence — and
its verdict changes materially (see §3, step 8).

**C4 — Cortex's hop identity conflicts with DigitalBrain's dedupe. Orleans wins.**
Cortex gives the incoming entry a *fresh* SynapseId whose CausationId points at the causing
outgoing entry (`poc/DECISIONS.md:25-28`). DigitalBrain uses **one** `SynapseDelivery` — one
SynapseId — appended twice: `AppendOutgoing` at the sender (`NeuronMessagePipeline.FireAsync` →
`NeuronTurnCoordinator.StageOutgoing/FlushOutgoing`) and `AppendIncoming` at the receiver
(`NeuronTurnCoordinator.StageInboundCause`). Receiver dedupe (`NeuronDeliveryMemory`, keyed by
`SynapseId`) is what makes at-least-once delivery effectively once, and it *depends* on that
sharing. *Resolved:* keep the Orleans scheme. A hop is the `(SynapseId, direction)` pair; the
corpus records `dir: in|out`. The causal graph is equivalent and the join still works.
This is Orleans-adaptation latitude, exercised deliberately.

**C5 — "Causation-linked two-entry journaling" is already done.**
`prompt.md` step 1 asks for it as new work. It exists: `SynapseDelivery.CausationId = cause?.SynapseId`
where `cause` is the turn's handling delivery (`SynapseDelivery.cs:56-64`), and both journal
appends already happen inside the single `WriteStateAsync` (`NeuronOutbox.CommitAsync`).
*Resolved:* step 1 shrinks. What is actually missing is listed in C6–C8.

**C6 — The audit trail is lossy today; the corpus is genuinely absent.**
`NeuronFeed` compacts at 512 entries / 512 KB, dropping oldest first (`NeuronFeed.cs:10-11,103-113`).
Journals are per-neuron with no owner-level ordering. Episode extraction over "the whole story"
is impossible after ~512 hops on a busy neuron. *Consequence recorded now:* the corpus **cannot be
backfilled** — it starts empty the day it lands. That is an argument for landing it early, which
the revised sequence acts on.

**C7 — Refusal reasons are destroyed in three lines of the outbox.**
`NeuronOutbox.TryDeliverAsync` catches `NeuronAuthorizationException`, calls `Record("refused", …)`
— which only sets OpenTelemetry Activity tags (`NeuronOutbox.cs:230-234, 253-265`) — and returns
`true`, so the entry is dropped as delivered. `Abandon` (depth exceeded / 1000 attempts / 30 min
horizon) does the same. There is **no durable refusal record anywhere** and nothing reaches the
caller. There are 135 `NeuronAuthorizationException` throw sites across 32 files; the richest
messages in the system (`SynapseGraphNeuron.RequireWorkingTransform`'s field-by-field morph
diagnostics) are exactly the ones nobody can see. The assistant's `fire` then reports a 15 s
silence (`SystemTools.cs:28,256-262`). *Resolved:* this is the highest-value, smallest, most
foundational change in the mission and becomes its own first slice.

**C8 — Connections carry no provenance at all.**
`SynapseConnection` is `(ConnectionId, Source, SynapseAlias, Target, Transform?, ExpiresAt?)` —
no created-by, created-at, intent, or correlation. `Connect` has no `Intent` field. Cortex makes
provenance mandatory on every connection, schedule, behavior, assembly and grant (invariant 7).
*Resolved:* provenance is a prerequisite of the concepts that carry it, not a follow-up — see §3.

**C9 — Correlation does not thread a story. This is new work the mission's sequence does not contain.**
Episode extraction groups by root correlation. Today one chat turn breaks into three or four
correlation islands: (a) `NeuronCapabilityCoordinator.BeginRequestAsync` mints a *new* correlation
for every reified inter-neuron grain call and ignores the ambient client-entry correlation that
`NeuronMessagePipeline.ResolveEmissionCorrelation` already honours; (b) `ChatTurnWorker`'s detached
`_ = RunAsync(...)` continuation loses `turn.Handling`; (c) Chat's terminal apply runs on a grain
timer/reminder outside any delivery turn; (d) the assistant's `fire` goes through
`ISessionNeuron.Fire` and polls the session's own correlation, so a tool call is not linked to the
turn that provoked it. A corpus written today would faithfully record an unjoinable story.
*Resolved:* correlation threading lands **with** the corpus, not after it. This is the single most
consequential finding of the survey.

**C10 — Cortex's behavior execution model is incompatible with Orleans as literally specified.**
Cortex runs the subprocess *inside the owning cell's turn* and has the kernel fire on the blocked
cell's behalf (`poc/DECISIONS.md:79-84`). On Orleans: the sender's delivery attempt is cancelled at
45 s (`DeliveryPolicy.DeliveryAttemptTimeout`) while the run bound is 120 s; grains are strictly
non-reentrant (`NeuronConcurrency`), so a blocked behavior grain cannot serve its own run's fires;
and every fire is staged into the firing grain's own turn (`NeuronMessagePipeline.FireAsync` hard-codes
`neuron.Id` as caller). *Resolved:* adopt the *semantics* (run-token identity override, allow-list,
120 s bound, capped run history, build-cache shield, causation across the process boundary) on the
production-proven `ChatTurnWorker` shape — dispatch returns immediately, the run is detached with
its own CTS, and the run record commits in a second short turn. Deviation logged; the guarantee is
honestly weaker (a crash between subprocess exit and record-commit loses the run record) and that
weakening is stated rather than hidden.

**C11 — CLAUDE.md trap #5 is stale.**
`SynapseCapabilityTool.Materialize` no longer exists. Request-vs-fact is now decided by
`CapabilityIndex.Build` (Accepted → request, Facts → fact) and by walking base types for
`RequestSynapse<>` in `SystemTools.ReplyTypeOf` / `ContractSignature.ReplyOf`. *Resolved:* the
*rule* still holds (only request contracts get a reply the model can await); the named mechanism
is gone. CLAUDE.md gets corrected in the documentation slice.

**C12 — `poc/` stays untracked.**
It is a nested git repository. `prompt.md` requires its suite stay untouched as the executable
spec. Committing it into the parent would embed a gitlink or flatten it. *Resolved:* leave
untracked; it is reference material, not product source.

### 2. Deviations from Cortex semantics (deliberate, Orleans-adapted)

| Cortex semantic | Decision | Why |
|---|---|---|
| Fresh SynapseId per journal entry | **Rejected** — keep one id per hop, `dir` distinguishes | Receiver dedupe depends on it (C4) |
| Behavior runs inside the triggering turn | **Adapted** — detached run, record in a second turn | 45 s attempt timeout vs 120 s bound; non-reentrant grains (C10) |
| Reply emissions traverse the graph | **Deferred, likely rejected** | Changes every `RequestSynapse` round trip in the product and lets an owner wire a reply back into its own emitter — the exact loop `NeuronConcurrency`'s no-callback rule exists to prevent. Revisit only with a named product need. |
| Duplicate-wire refusal by (source, alias, target, transform) | **Adapted** — keep upsert-by-ConnectionId, refuse only a *different* id that duplicates an existing live wire | `ChatRoles.ResponderConnectionId` derives a stable SHA-256 id precisely so re-running `connect-chat-responder.cs` is idempotent. Cortex's rule as written breaks that script. |
| "Both endpoints must be live cells" | **Adapted** — validate the grain *type* is known (`ActiveModuleContractTypeMap`), not that an activation exists | Orleans grains are virtual; nothing is ever "not live". Catches typos, which is the actual value. |
| Cascade quiescence counter | **Sequenced late, scope-flagged** | In-process counting is trivial; cluster-wide quiescence is not. Its only real product consumers are behavior runs and the demo walkthrough. See §4 decision D5. |

### 3. Revised sequence

The prompt's six steps become nine slices. Three changes carry real weight; the rest is
decomposition.

**Change 1 — Split step 1, and lead with refusal visibility.**
The prompt bundles "causation-linked two-entry journaling + refusal records + cascade quiescence"
into one step. Journaling already exists (C5); the three remaining pieces have different sizes,
different risks and different prerequisites. More importantly, *every* Cortex semantic being
absorbed adds new refusals — connect-time type checks, required intent, allow-lists, schedule
guards, behavior guards. Landing any of them before refusals are visible makes the system strictly
**harder** to drive than it is today: the model receives a 15 s timeout with no cause and cannot
self-correct. Three independent surveys reached this conclusion from different files.
Refusal visibility is therefore not a by-product of milestone 1 — it is the gate on milestones 2–6.

**Change 2 — Provenance moves before the concepts that carry it.**
The prompt reaches provenance's payoff at step 4 (episode extraction), after schedules, behaviors
and assemblies are built. Cortex makes provenance mandatory on all five record types. Building
schedules and behaviors first means building them without provenance and rewriting them. Provenance
is a small, contained kernel + contract change; it lands before its carriers.

**Change 3 — Split the deletion by *why* it is safe.**
"The great deletion" last is correct for deletions justified by *supersession* — you cannot prove
Execution's machinery is superseded until behaviors exist. It is wrong for deletions justified by
*proven absence of consumers*, which have no absorption dependency at all and which shrink the
"what must keep working" surface for every later slice. The llama/convene stack (~20 files, zero
non-llama consumers) and the Memory/Qdrant vertical (zero consumers, and non-functional — no
`IEmbeddingGenerator` is registered anywhere, so every `memory.*` request refuses) are dead *today*.
They move early.

| # | Slice | Was | Gate |
|---|---|---|---|
| 1 | **Refusal visibility** — durable refusal record + refusal reaches the caller with the fix path | part of 1 | build; `fire` at a refusing contract returns the reason, not a timeout |
| 2 | **Dead-code deletion I** — llama/convene stack, Memory/Qdrant vertical, proven-dead seams, stale docs → CLAUDE.md | part of 6 | build; chat turn + find_capabilities still answer |
| 3 | **Correlation threading** — one story per turn across reification, detached worker, timer apply, tool calls | *absent from the prompt* | build; one chat turn shows one root correlation end to end |
| 4 | **Corpus** — append-only per-owner mirror at turn commit | part of 1 | build; a chat turn produces matching out/in corpus lines |
| 5 | **Provenance** — intent + created-by/at + correlation on connections; `get_neurons` explains every wire | part of 4 | build; `get_neurons` renders intents |
| 6 | **Schedules** — `every` + durable next-due + phase-preserving catch-up; timer flow re-lands on them | 2 | build; timer card + elapsed note still work in chat |
| 7 | **Behaviors** — subprocess runs, run-token identity, allow-list, run history + scripted-agent table | 3 | build; a behavior run fires into a chart |
| 8 | **Assemblies + grants + episodes** — enable/disable authority, cross-owner read, offline episode join | 4 | build; export → import → enable; `episodes.jsonl` |
| 9 | **Agent alignment + deletion II** — sectioned `get_neurons`, `FireResult`, then supersession-justified deletions | 5, 6 | full recorded walkthrough |

Slice 8's grants and slice 4's corpus partitioning both block on decision D1 below.

### 4. Owner decisions required

Everything else in the survey I can decide and record. These five change the shape of the work
and are genuinely the owner's.

**D1 — What is Cortex's "owner" in production: `OwnerId` or `PrincipalId`?**
Two identity axes exist and are routinely confused. `OwnerId` is a plain validated string forming
the first segment of every grain key; it is *process configuration* (`DigitalBrain:Owner`, default
`"dev"`), so the whole product runs as exactly one owner. `PrincipalId` is the verified Guid from
cookie auth, folded into the neuron *name* as `{principal:N}.{local}` for chat and surface grains
only. Consequences: corpus partitioning, refusals-log scope, schedule ownership, behavior run-token
owner, and what a grant is checked against all inherit this answer. A grant checked against
`OwnerId` is a no-op today (one owner). Making principals into grain-key owners rewrites every
neuron id **and** changes the AEAD purpose string that protects stored MCP tokens
(`McpTokenPresence.cs:63-74`) — every user re-authorizes.
*Recommendation:* keep `OwnerId` as the partition key for slices 1–7; treat grants as
principal-scoped read permission enforced at `SessionNeuron.ReadNeuronJournal/WatchNeuron` and
`OwnerBoundCallFilter`, decided at slice 8. Do not rewrite grain keys.

**D2 — Corpus store, retention, and redaction.**
Store: a new Azure Blob container on the existing Azurite (additive — creates no risk to existing
volumes) versus a corpus grain (inherits turn serialization but funnels every hop through one
grain). Retention: "append-only forever" versus rolling segments. Redaction: the corpus would
durably capture `CallMcpTool.Arguments` (model-authored) and `McpToolReturned.Content` (raw
Salesforce/Gmail payloads) verbatim — today reification deliberately records *no* arguments
(`OutgoingReificationFilter.cs:40-43`), so a corpus reverses that stance and no redaction rule exists.
*Recommendation:* Azure Blob, one blob per owner per UTC day (mirrors Cortex's layout), unbounded
for now with the volume measured and reported in this log; explicit field-level redaction for MCP
arguments and results before slice 4 ships.

**D3 — Standing pre-approval for additive store-format evolution.**
Almost every slice appends fields to persisted records. Orleans binary version tolerance makes this
safe *if and only if* the new `[Id(n)]` is unused and the constructor parameter has a default —
the pattern the repo already uses six times. Dense ranges to respect: `SynapseDelivery` 0-6,
`ExecutionData` 0-17, `PendingAuthorization` 0-15, `CommandAuthorizationRecord` 0-11,
`DurableTurnRecord` 0-7, `SynapseConnection` 0-5, `TimerState` 0-6.
*Ask:* pre-approve "append a new trailing `[Id(n)]` with a default; never renumber or reuse" as a
standing rule, so absorption is not gated on every field. `SynapseDelivery` is called out separately
— it is the one persisted type with an internal positional constructor and get-only properties, and
it is nested inside `JournalEntry` and `OutboxEntry` already on disk.

**D4 — Do behaviors ship, or are they a development capability?**
The shipped silo image is `aspnet` runtime, no SDK, non-root (`Dockerfile:63-76`). Behaviors as
`dotnet run <file>.cs` subprocesses work on a dev machine and under AppHost, and nowhere else,
unless the product gains a separate SDK-bearing runner resource with its own identity, NuGet feed
and warm cache volume.
*Recommendation:* dev/AppHost capability for this refit; record the production runner as explicit
deferred work rather than pretending the container can do it.

**D5 — Is `Intent` required on `db.connect`?**
Cortex makes it mandatory. Required is the honest absorption, but `db.connect` is a live wire
contract with real durable data: the assistant's system prompt hard-codes a five-field example
(`Assistant.cs:44-49`), `connect-chat-responder.cs:20-22` constructs it positionally, and
historical `db.connect` journal entries have no `intent` property (they replay to null).
*Recommendation:* add it as a field in slice 5, enforce non-empty **by refusal** (not by contract
nullability) so replay stays readable, and move the prompt + script in the same commit. This is
only safe *after* slice 1, which is why slice 1 leads.

### 5. Baseline recorded before it is deleted

The seven test-stack pins in `Directory.Packages.props` and the `test.runner` block in `global.json`
are the last trace of the deleted central suite. They are removed in slice 2; the versions are
recorded here so the final-hardening module-owned test design has its starting point:

```
Microsoft.NET.Test.Sdk 18.8.1            xunit.v3 4.0.0-pre.154
Microsoft.AspNetCore.Mvc.Testing 11.0.0-preview.6.26359.118
xunit.runner.visualstudio 4.0.0-pre.5    xunit.v3.extensibility.core 4.0.0-pre.154
Reqnroll.xunit.v3 3.3.4                  Aspire.Hosting.Testing 13.5.0-preview.1.26376.5
Testcontainers 4.13.0                    Microsoft.Orleans.TestingHost 10.2.2
global.json: "test": { "runner": "Microsoft.Testing.Platform" }
```

### 6. What surprised us

- **The kernel is closer to Cortex than the mission assumes.** Turn atomicity, stage-then-commit
  rollback, settled-vs-retried failure classification, two-entries-per-hop journaling and causation
  stamping are all already there. The gaps are the *sinks* (no corpus, no refusals log) and the
  *joins* (correlation), not the delivery model.
- **The refusal seam is three lines.** The system's most actionable diagnostics are written to an
  OpenTelemetry span and thrown away. A whole class of "the model can't self-correct" symptoms
  traces to `NeuronOutbox.cs:230-234`.
- **Half of the Execution module was never built rather than superseded.** The operation ledger
  (`Prepare/Transition/ResolveOperation`, `OutcomeUncertain` reconciliation, ~500 lines) has no
  producer; the user-action custody rail (~450 lines) has no implementation; `WorkerNeuron`/`IWorker`
  have zero implementers; auto-retry is unreachable because the only policy in the product is
  `MaximumAttempts: 1`. The remainder — idempotent command receipts, versioned cancel, single-slot
  durable worker dispatch, the 15 s worker lease, the terminal bridge — is load-bearing for chat
  durability *and* is the natural host for behavior runs. So the delete-list item resolves to
  "trim what was never built, reuse the core", not "delete the module".
- **`ISynapseTransform` as a DI extension point has zero implementations and zero registrations.**
  The relay's `GetServices<ISynapseTransform>()` always returns empty; every transform in the
  product is the declarative `to:` string, re-parsed on every single delivery.
- **Orleans Streams: five provisioning sites, zero consumers** — confirmed by search, consistent
  with the recorded decision to keep them provisioned.

---

## Session 2 — 2026-08-12 — the architecture was grilled and it did not survive intact

Still no production source changed. Deliverable: an explicit spec of the post-absorption
architecture, fifty demanding user scenarios written against it, and an adversarial review that
tried to break every one.

Method: 19 agents. Five generators wrote ten scenarios each (dashboards/sharing, integrations/OAuth,
agents/reasoning, behaviors/scripting, apps/memory/durability) as things a person would actually ask
for. Ten hostile reviewers walked each scenario through the architecture on nine axes — identity,
routing, concurrency, durability, authority, correlation, refusal, the three-tool constraint, and
whether a human can see the result — required to cite file:line for every claim. Three judges with
different lenses (correctness, authority, completeness) ruled on all 283 claimed breaks and added
what the reviewers missed. One synthesizer deduplicated.

**Result: 0 of 50 scenarios land on the architecture as I specified it.** 49 are carried by 17
amendments; 1 (an OS window on a second monitor) is outside the stack and is recorded as an honest
miss.

### 7. Trust caveat on the review

The judge panel returned **zero refutations across 639 rulings** despite being explicitly instructed
that a review confirming everything is worthless. That is agreement bias, not a clean bill of health.
The load-bearing findings were therefore checked by hand against source before being accepted. Four
were verified directly and all four hold:

- `VerifiedActor.Enter` has exactly ONE call site in the entire tree (`ChatTurnWorker.cs:163`).
- `McpServerNeuron.AuthorizedAsync:241-246` throws `NeuronAuthorizationException` on a null actor.
- `ChartNeuron` is one flat `IDurableList<byte[]>` capped at 256 with a global `RemoveAt(0)` and no
  notion of `Series`; its `Read()` is a grain method, not a `RequestSynapse`, so `fire` cannot reach
  it and no HTTP endpoint serves it (7 mapped endpoints, none reads a neuron).
- `Responded` declares `[Id(4)] ChatChartOffer[]? Charts` and **all four** construction sites
  (`Chat.cs:191,207,239,789`) omit it.

That last pair means: **a chart cannot currently appear inside a conversation.** The single canonical
example this migration is organised around — a chart in the chat — dies on a declared-but-never-
populated field plus an unreachable read. Session 1's spec did not catch it.

### 8. The four missing kernel facilities

The routing plane held: fifty scenarios found no counterexample to the cell contract (journal-is-outbox,
at-least-once + dedupe, serialized turns, bounded depth). Everything else failed on four absences,
and they compound:

1. **A verified principal that rides the delivery.** `SynapseDelivery`/`OutboxEntry` carry no
   principal; `VerifiedActor` rides `RequestContext` and is entered once. So no schedule tick,
   behavior run, button click or webhook can carry an actor — every concept the absorption adds is
   born unable to reach an integration. The mirror defect is worse: `CallMcpTool.Actor` is a plain
   wire field on a `[ClientEntryPoint]` interface while `McpTokenPresence.SubjectKey` is literally
   that principal's id, so a payload-supplied principal selects another user's protected tokens.
   `chat-probe.cs:15-18` forges exactly such an actor in committed source.
2. **An outcome rail covering more than one exception type.** Only `NeuronAuthorizationException` is
   recognised as settled at the outbox; other `[SettledDeliveryFailure]` types fall to the generic
   catch and retry 1000×/30 min. Four outcomes produce nothing at all: zero-receiver emissions,
   depth-16 abandonment, retry-horizon abandonment, connection expiry.
3. **A root correlation that survives detached and reminder boundaries.** There is no
   correlation-bearing directed send at all — `SendAsync` passes `correlation: null` and never
   consults the ambient client correlation — so Session 1's "carry the correlation in durable state"
   had nothing to spend it on. A sixth island was found (streamed capability dispatch never enters
   a capability turn).
4. **A way to read anything.** No vocabulary cell has a read the model or shell can reach; the
   corpus as specified has no read side; and the refusal rail as I wrote it delivers the reason to
   the cell that already knows it, not to the originating requester.

### 9. Amendments (17) and what they do to the sequence

Full text, per-scenario verdicts and the diagrams are in the published architecture document.
Distribution across the nine slices: **01** three amendments (outcome rail, turn/delivery hardening,
MCP rail repair) · **03** one (root correlation + a real action record) · **04** two (corpus as a
resumable projection with a read rail; broadcast becomes opt-in per fact type) · **05** two (principal
rides the delivery; connection provenance + connect-time completeness) · **06** two (wire language
gains literals/predicates/coalescing + reducer cells; origination gains calendar/identity/ingress) ·
**07** one (behavior host: durable runs, Execution-owned, compiled off-silo) · **08** three (cluster
trust boundary; principals as a real partition; durable instance registry + atomic install) ·
**09** three (read is vocabulary; capability surface tells the truth; client rail: resume, names,
login). Fourteen are rated fatal.

**Honest consequence: this is materially more work than Session 1's nine slices implied.** The spine
survives — the ordering was right, and refusal-visibility-first was vindicated hard, because six of
the seventeen amendments are refusal-shaped. But slices 01, 08 and 09 each grew from one change into
three, and two amendments (a durable instance registry; read as kernel vocabulary) are new subsystems
that were not in the plan at all.

### 10. The central claim, corrected

"Any behavior and any logic can be expressed as data" survives with two edges that must be stated
rather than glossed:

- **True of routing, origination, units and sharing** — once identity rides the delivery, and
  provided reads, the corpus and inviting a person are *also* ordinary contracts (the spec forgot all
  three, which quietly falsified "no fourth tool" since `POST /auth/users` is the only way to create
  a principal).
- **Not true of logic.** The wire language has exactly one operator: rename. With literals, a
  predicate and a coalescing interval it has about four. Arithmetic, aggregation, comparison and
  calendars all fall into a behavior, and a behavior is C# source. The honest form is: *any logic can
  be expressed as a piece of data that names a program.* Still a strong claim; not the same claim.

### 11. New owner decisions this raised

D1 (owner vs principal) is now **forcing** rather than deferrable: the review's verdict is that
principal-scoped names must extend beyond chat and surface, because chart, timer, diagram, mcp,
synapsegraph and session are all currently shared inside owner `"dev"`. Two further decisions appear:
whether behaviors compile at create-time into a stored artifact (so the SDK-less silo can load an
assembly instead of shelling out), and whether the wire language gains literals and predicates at all
— if it does not, §D must say plainly that the architecture expresses routing as data and computation
as code.

---

## Session 3 — 2026-08-12 — the third axis, and a programme that carries the vision

Owner reframed the goal: the brain should be able to compose *anything* — "build me a calculator
inside DigitalBrain", "one agent per source file that deliberate and compose a feature", "if one user
solves a problem every other user's brain knows it" — and share it with anyone. And asked the
fundamental question: what actually stops this, given Orleans is a capable actor framework?

### 12. The diagnosis: two axes are data, the third is code

DigitalBrain has three axes. Instances are data (`chart:vlad/sales`). Connections are data (the graph).
**Kinds are C#** — verified: 20 `[GrainType(...)]` attributes in `src/**`, zero runtime type creation
(`Activator.CreateInstance` appears only for DI module hooks and the dead `Participant`), and
`ModuleReflection` reflecting over compiled assemblies only. Twenty kinds of thing can ever exist.

Everything the owner wants lives on that third axis. A calculator is not new wiring between existing
cells; it is a new *kind* of cell with its own state shape, handlers and view. **Cortex does not fix
this either** — its behaviors are stateless programs that run and exit, so a behavior cannot *be* a
calculator. Cortex made routing, origination and computation into data and left kinds as code.

### 13. What Orleans actually permits (doc-verified this session)

Three independent mechanisms all close at silo start, each confirmed in Microsoft's docs:
`GrainTypeAttribute` is `AllowMultiple = false` ("each grain can only have one grain type name");
`GrainClassMap` is built from an `ImmutableDictionary` exposing only `TryGetGrainClass`; and
`GrainManifest` is `[GenerateSerializer][Immutable]` with `LocalGrainManifest` get-only and no
republish API. **A grain type cannot be added to a running silo** — not by aliasing, not by a
placement director, not by loading an assembly.

So the kind cannot live in the grain *type*. It lives in the grain **key**: one registered
`[GrainType("cell")]` grain, N keys, `cell:{owner}/{kind}@{name}`. This is idiomatic rather than a
workaround — identity is type + key, and the key is where a logical instance is named. Placement,
directory and versioning are untouched, and the product-facing `NeuronId` address stays exactly as it
is; only `NeuronId.ToGrainId()` becomes a resolver (~10 call sites). The separator is `@`, not `/`:
`NeuronId.FromGrainKey` splits at the first `/` and `IdentityPart.Validated` throws on `/` in a name,
so the design's original three-segment key would have thrown on every activation.

### 14. A second compiled axis nobody had noticed

The review found what the Session 2 framing missed: **the vocabulary is compiled too.** A `Synapse` is
a CLR record serialized by build-time codegen and dispatched by CLR type, so a new kind cannot invent
a new *fact* either. Fix: one compiled carrier `db.datum(Kind, Fields)` plus an `EffectiveAlias` rule
applied at exactly five sites (routing, relay transform target, `RequireWorkingTransform`, telemetry,
corpus), so the wire vocabulary stops being closed. Manifests become two-source: compiled manifests
reflected, kind manifests durable data.

The same gap explains a finding I made by hand: **the model is not on the wire.** `ILLM`/`IAgent`
expose only grain methods taking `IReadOnlyList<ChatMessage>`, and there are zero `RequestSynapse`
types in the AI contracts. So `fire` cannot ask the model anything and nothing can be wired to it —
which is precisely what "one agent per file, deliberating" requires.

### 15. Owner input, and where it moved the line

The owner's position — "the redeploy can be done via aspire restart resource" — was tested and
**survives, but not for the stated reason.** Restart genuinely is cheap here (AppHost runs one silo,
so rolling/heterogeneity never arises; journals survive in Blob, reminders in Tables, undelivered
outbox entries recover via the `db-outbox-wakeup` reminder). It is the wrong tool because it is
cluster-wide and all-users, so **it can never carry a solution from one person's brain to another's.**

Consequences, all recorded as deliberate kills:

- **Tier B (compiled kinds) is killed as a sharing mechanism.** A compiled kind cannot reach another
  person's brain without that operator deploying it; .NET has no in-process sandbox; `Neuron` hands
  its subclass the `ServiceProvider`; cluster membership *is* the authority boundary. Authoring
  compiled code equals deployment authority. Compiled code becomes **the palette** — new effects, new
  leaf widgets, new integration rails — shipped as an ordinary reviewed release.
- **In-silo Roslyn demoted to validation only.** Registration needs a restart regardless of where the
  DLL was produced, so compiling in-silo buys nothing. (Roslyn genuinely needs no SDK — it is an
  ordinary library — so the capability is real, just not useful here.)
- **"Automatically knows the solution" killed as a design target.** Auto-install would arm schedules,
  spend the installer's OAuth tokens and rewrite their graph without consent. The honest deliverable
  is **automatic discovery plus deliberate install**.
- **Reducer cells, arithmetic and the `when` predicate are removed from the connection morph** — a
  reversal of part of a Session 2 fatal amendment. A cell beats a transform on every axis
  (addressable, journaled, readable, provenance-bearing, rollback-enlisting, replaceable without
  touching the wire). Kept on the wire: quoted literals and `MinInterval`. **This needs the owner's
  explicit ruling.**
- **`ui.chart-card` dropped** in favour of a general `ui.view-card`; `Responded.Charts` (Id 4, zero
  producers, four dead consumers) is deleted and `Views` added at a fresh id, never recycling Id 4.
- **The team/convene stack is killed outright** — it is N models at ONE name, not N perspectives at N
  names, it is dead with only Gemma4 provisioned, and while it ships `convene_model_team` and
  `ask_llama` are a fourth and fifth tool falsifying "three tools, forever" at HEAD.
- **`assembly` is renamed.** It collides head-on with .NET assembly semantics already load-bearing in
  `src` (`ModuleAssemblies`, `ModuleReflection.ManifestOf(Assembly)`); shipping `assembly.import`
  beside a compiled-assembly import would produce exactly one catastrophic model misunderstanding.

### 16. The programme: 11 stages, and it is two programmes

Stages 0–6 are the absorption and its seventeen amendments (be told no · two people one brain ·
everything can be named and un-made · wires that cannot lie · the outside world works · time and
memory). Stages 7–10 are the third axis (kinds become data · you can press it · one brain's solution
every brain's · code where code is needed). Stage 0 is a small spike settling four one-way doors.

**The sequencing rule that matters most:** four amendments — the connection record, the wire language,
the read rail and the capability surface — **fork on the kind decision** and will be built twice if
they land before it. That is the single most expensive mistake available here.

**Honest sizing: stages 7–10 are the larger programme, not an addendum.** They need a new grain, a
durable kind registry, an expression language with an install-time cost analyzer, a
versioning-and-pinning story with no migration machinery to build on, a two-source catalog touching
find/fire/connect-validation/get_neurons, a view document format, a Flutter renderer, and a
share/install lifecycle with a trust model.

### 17. What is not reachable

- **Vision target 2 (5,000 self-implementing per-file agents) is not reachable as asked.** Three
  independent walls: the brain has no filesystem at all (zero matches for `FileSystemWatcher` /
  `Directory.GetFiles` / `File.ReadAllText` in `src`); 40 agents × 5 rounds is 200 12B inferences
  against one GPU, which Orleans parallelizes and the GPU serializes; and "properly" has no acceptance
  criterion inside the system because the central test project was deleted by owner amendment. The
  reachable form is a ~40-cell working set, three rounds, producing a **plan** with per-file stances
  and provenance — implementation handed to a human or an external coding agent. A repository rail is
  needed as a first-class integration, not as a behavior or a script.
- **Fixes do not propagate.** Installs copy rather than reference, which is exactly what stops one bad
  publish breaking forty installs — and exactly what leaves forty installs broken when the author
  fixes a real bug. The product cannot have both; this plan chooses containment.
- **There is no sandbox beneath the interpreter.** Tier A's whole safety story is that its instruction
  set is closed, total, and cannot name an actor or an arbitrary send target. Every future primitive
  request ("let templates read config") is a privilege-escalation change wearing a feature costume.
- **Continuous interaction is permanently out.** A slider at 60 Hz is 60 turns per second into a
  non-reentrant grain. Views carry discrete events only.
- **Per-tap latency is unmeasured** and is the largest threat to the calculator feeling like one:
  one delivery + one turn + one `WriteStateAsync` over Blob + one SSE hop. Above ~80 ms the
  interaction model must change to client-side accumulation.
- **Depth 16 is a real ceiling** — roughly seven external question/resume hops, or seven deliberation
  rounds before a hierarchical fold.
- **Reminder ticks due while the cluster is down are permanently missed** (verified on Learn), so
  every restart — including every palette release — is an outage schedules must collapse around.

### 18. Four facts still unverified — Stage 0 exists to settle them

Whether an Aspire project-resource restart re-builds; whether a runtime-loaded assembly's grain types
can register at startup (`Assembly.LoadFrom` + `AddSerializer` + `GrainTypeOptions.Classes` — both
APIs exist, no doc confirms the combination); whether `Microsoft.Orleans.Journaling` (an **alpha
prerelease** that is the only durable record in the system) resolves keyed durable services the way
production assumes; and whether an `@`-bearing grain key survives Azurite blobs plus Tables reminders
and clustering end to end. Nothing on the critical path may depend on these until Stage 0 answers them.

---

## Session 4 — 2026-08-12 — the 50 re-simulated against the amended architecture

Consolidated the target architecture into one authoritative document and re-ran all 50 scenarios
against it: 10 simulators produced mechanical step traces, 3 skeptics (hand-waving, hard limits,
identity) challenged every optimistic verdict, 1 reconciler applied the rulings.

### 19. The number, both ways

**30 WORKS · 20 WORKS_PARTIAL · 0 FAILS.**

- **Carried: 100%.** No scenario dies architecturally; every one has a named mechanism for its substance.
- **Exactly as asked: 60%.** Twenty scenarios lose something the person would notice.

The owner asked for ≥90%. **It clears on the loose reading and does not on the strict one**, and the
distinction is not pedantry — S10's literal ask (a native OS window on a second monitor) is unreachable
and the user gets a docked pane instead. Reporting 100% without that sentence would be the easiest
misrepresentation available here.

The simulators returned 39/11/**0 FAILS** before challenge, which was not credible on its face — the
prior round had already established S10 as unreachable. The skeptics raised 12 challenges, 11
downgrades and 1 upgrade, and **16 of 50 traces needed correction**: wire-before-fire ordering (S01),
three fires are not atomic (S02), `db.connected` is a reply and not a routable fact (S04), carrier
versus compiled fact (S21), reset is a verb (S24), and a cursor cannot be spliced into SOQL by a wire
literal (S48).

### 20. Two gaps that are in NONE of the seventeen amendments

**A18 — four stores are owner-scoped while the partition is principal.** `corpus:{owner}/log`, the
per-owner refusals inbox, the instance registry and the single synapse-graph grain are all owner-scoped,
but §K makes principals the partition. Consequences the simulation found: a refusal cannot reach a
second principal's inbox (S16); a co-principal can `corpus.find` and read the owner's sentences (S07);
and no stated rule permits *or refuses* a cross-principal connect (S50). I had spotted the inbox half
of this by hand while the run was in flight; the skeptics generalised it to all four. **This is the
single highest-leverage correction available** — it cleans S07, S16, S50 and most of S39/S44/S46.

**A19 — chat's 15-second `WaitingPolicyDeadline` versus Stage 5's browser trip.** An OAuth sign-in
parks the turn on an Execution blocker while chat's existing deadline force-cancels a parked FIFO head.
Nothing in the seventeen amendments reconciles them, and it is load-bearing for S13 and S25.

### 21. What would most raise the strict number

In order: (1) principal-scope the four stores above; (2) state a connect-time cross-principal write
rule; (3) bind a responder per conversation by default, so a second window of the *same* person is not
blocked behind one assistant grain (S26); (4) add a date/bucket primitive or a cursor-splice step to
the cell tier — without one, two feeds cannot share a week key (S03), a daily total straddles time
zones between SOQL's `YESTERDAY` and the schedule's IANA zone (S28), and a recovered window cannot be
rebuilt (S48); (5) reconcile A19.

### 22. Honest residuals worth carrying forward

`FireRowsAs` fans N rows as N independent turns — there is **no batch-complete signal**, so a `sum`
reducer cell grows monotonically and any threshold computed off it is wrong (S29, S19). "When the
nightly sync finishes" has **no completion fact** at all: the MCP result is a summary reply to the
requester, so the nearest data trigger is `ui.chart-changed`, which a zero-row night never emits (S05).
A behavior's allow-list is an authority boundary over the rail, **not an egress boundary** — a
hardcoded webhook in model-authored source reaches the outside world without touching the brain (S37).
Feedback lands but nothing puts it in front of the model next time (S45).

Interactive artifact with all 50 traces playable: 462 trace steps across 17 components.

---

## Session 5 — 2026-08-12 — Stage 1 implemented, on branch `stage1-outcome-rail`

First production code of the absorption. The plan (`plans/stage1-outcome-rail.md`) was grilled by
five lenses against real source before a line was written; five blockers came back and all were
adopted. Implementation, build (0 warnings) and runtime verification through `digitalbrain-mcp`.

### 23. What the pre-code grilling caught

- **The design would never have matched a correlation.** The outcome is journaled under its own
  envelope, whose `CorrelationId` is fresh — so every reply-matching loop, which filters on the
  envelope, would have found nothing and still spun to 15 s. Correlation now rides the **payload**.
- **Routing outcomes through the pipeline's directed send would have caused N+1 `WriteStateAsync`
  per drain** plus watcher pushes mid-flush. Replaced by a staging-only path.
- **The `IInbox` neuron was dropped entirely.** It needed `FrameworkInterfaces` + `[ClientEntryPoint]`
  registration, `Core` is not in the composed implementations list, and nothing read it — it would
  have shipped as a write-only log.
- **`FixPath` was dropped.** Nothing produces one; the existing refusal messages already embed the fix
  in prose, and a constant-empty field teaches the model to ignore it. `Reason` is capped at 2 KB.
- **The stated justification was wrong.** Settled failures do not "retry 1000×/30 min" — the receiver
  already settles and remembers, so the sender retries once and reports delivered. The change is still
  right; the reason had to match the code.

### 24. The design that survived

An outcome is **journaled into the firing neuron's incoming feed, never delivered.** That one decision
removes the self-send, the relay-injection hazard, and the possibility of an outcome-of-an-outcome —
and it lands exactly where every caller already polls. Outcomes stage into a list during the drain and
flush only after the loop, because `DrainAsync` indexes and mutates `entries` while iterating.

### 25. A regression I introduced, and what it taught

I opportunistically made the base `OnUnboundSynapseAsync` a settled refusal — the grill had recommended
it — to get a deterministic probe. **It broke every request/reply in the product**: `ReplyAsync`
addresses the caller, and callers routinely have no `IHandle<T>` for the reply type, so the session
never received its replies. The build was green; only the live run caught it.

Reverted. It belongs to the turn-and-delivery hardening amendment with a proper reply-sink accept-list,
not to this slice. `SessionNeuron` keeps an explicit override documenting that it accepts anything,
because it is the universal reply sink.

### 26. Runtime evidence (via `digitalbrain-mcp`, live AppHost)

A simulated user turn (`send_chat_message`) returned a real Gemma4 answer — reply rail intact — and
`read_neuron_journal(chat/main, incoming)` then showed `Unrouted` records, one carrying the **exact
correlation of that reply**. Before this slice that emission was journaled and dropped with no record.

### 27. Findings the live run surfaced

- **`chat.responded` has no handler anywhere**, so every assistant reply is a zero-receiver emission
  and now produces an `Unrouted` record. This is *truthful but noisy*: the UI receives `Responded` by
  **journal projection**, not by delivery. The product therefore has two delivery models, and the
  outcome rail can only see one. A neuron needs a way to declare a fact projection-only, or `Unrouted`
  will train the model to ignore it. **This is new, and it is not in any of the 19 amendments.**
- **A broadcast ghost is live in production right now**: `chart:dev/f1a35f68-…`, minted per-correlation
  by the scripting resource's `ChartPoint` emission. Trap 8 and amendment "broadcast becomes opt-in",
  observed rather than theorised.
- **The Aspire cluster id is generated** (`uxtps7dxsxgdwqg1072c1x0tb`), so a standalone scripting client
  cannot be configured by guessing; it must be read from `OrleansSiloInstances`. The scripting resource
  works only because it inherits Aspire's environment.
- **The file-based-app `PublishAot` trap is real and still bites** — reflection JSON dies silently
  without `PublishAot=false`, exactly as `poc/FINDINGS.md` recorded.

### 28. What is NOT done

Stage 1 as specified also calls for a per-owner refusals **inbox** neuron and a read verb for it; both
were dropped on evidence (see §23) and belong with the instance-registry work. Stages 2–10 are
untouched. The `outcome-probe.cs` scripting probe is committed but cannot run standalone until the
client bootstrap is solved; the MCP surface carried the verification instead.
