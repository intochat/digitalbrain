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
