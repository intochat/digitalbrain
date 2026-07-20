# ARCHITECTURE-REVIEW.md

The execution plan for the DigitalBrain phase that follows the v2 foundation.

This document supersedes `GOAL.md`. `GOAL.md` is retained as evidence — its 32-entry
Decision Log records why v2 is shaped as it is, including the errors this plan corrects.
It is history, not authority.

Scope note: NuGet publishing, CI, and release engineering are explicitly out of scope.
Nothing below concerns package IDs, versioning schemes, signing, or feeds.

---

## 0. How to read this

Every item states: **what it is**, **why it's wrong**, **what replaces it**, **what breaks**,
**what proves it**. Items without a proof line are not ready to execute and are marked as such.

Sections `D` (delete), `R` (refactor), `N` (rename), `B` (build new) are the catalogue.
Section 8 is the **ordered** execution plan and is the thing to work from. The catalogue is
indexed by the plan, not the other way round.

---

## 1. The root cause

`ModelTier` is not a research failure. The research had already been done.

`sources/Projects/CONTINUATION.md` — present in this repository the whole time — carries a
nine-step build order with a per-concern harvest map naming which prototype to mine for each
concern. It names `digitalbrain` twice: once for marketplace trust and economics, once
(together with `digitalbrain-app`) for server-driven UI. Alongside it,
`sources/Projects/docs/projects-survey-comparison.md` is a formal seven-way feature matrix
across `ino`, `final`, `digitalbrain`, `v4`, `v3`, `self-improving`, and `IAW`, dated
2026-06-23 to 2026-06-25. Its sibling, `brain-core-kernel-migration-assessment.md`, states:

> "`Projects/` should now be treated as old source/prototype material… Use `Projects/` as a
> quarry for specific proven mechanisms. Do not let it become a second architecture."

`GOAL.md`'s "Prototype Harvest Map" then named three trees — `final`, `ino`, `IAW` — and
dropped the rest without recording that a prior map had reached a different conclusion.

**The failure mode is a superseding plan that silently discards a prior plan's conclusions.**
`ModelTier` is one symptom. The absence of an observation primitive is another: three separate
prototypes solved it three different ways and none of the three was carried forward.

Two structural corrections follow, and they are the reason this document is shaped as it is:

1. **Rejection must be written down; silence is not rejection.** This was originally implemented
   as a living retirement ledger. §10 records why that shape failed — rows scored on "valuable
   content" cannot close — and what replaced it: a single harvest scored on "changes a decision
   that is open", executed once, with its findings and its explicit rejections recorded there.
2. **This plan records what it rejects and why**, in §10, so the next plan cannot discard it
   silently.

There is also a loose thread this plan cannot close. `sources/Projects/docs/*` describes a
codebase containing `DigitalBrain.Core`, `DigitalBrain.Silo`, `IPackBehavior`,
`PackAlcEmbodier`, and `GeneratedNeuron`. None of those identifiers exist anywhere on disk —
not in `sources/`, not in the working tree. **At least one generation between `Projects/` and
today is not in this repository.** Unresolved, and now unresolvable from disk: `sources/` was
retired in §10 and this generation was never in it. It remains a written finding rather than a
recoverable artifact.

---

## 2. Verified state of the repository

Everything in this section was checked directly. It is not restated from `GOAL.md`,
`CHANGELOG.md`, or `website/status.md`, several of which are inaccurate.

### 2.1 The kernel is coupled to two LLM vendors

`src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`:

```xml
<PackageReference Include="Anthropic" />
<PackageReference Include="Microsoft.Extensions.AI" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" />
```

`Neuron` — the base class every neuron in every system built on this framework inherits —
exposes `protected Task<string> AskModelAsync(ModelTier, string, CancellationToken)`. A
neuron that renders a button ships the Anthropic SDK.

This is the defect underneath `ModelTier`. Deleting the enum does not touch it.

### 2.2 `ModelTier` is 75 references across 23 files, not 11 across 3

Verified by grep excluding `sources/`, `bin/`, `obj/`. The count includes
`src/DigitalBrain.Testing/SimulationCluster.cs`, which keys its entire scripted-model table off
`Enum.GetValues<ModelTier>()`, and `src/DigitalBrain.Testing/NeuronSteps.cs`, which parses the
tier out of Gherkin. The abstraction reaches the test vocabulary.

### 2.3 `ModelTier.Embedding` cannot work

`ModelBindingExtensions.AddDigitalBrainModels` registers every declared tier via
`services.AddKeyedChatClient(bound.Tier, ...)`. An embedding model is not an `IChatClient`; it
is an `IEmbeddingGenerator<string, Embedding<float>>`. The `Embedding` member of the enum
binds to a client type that cannot serve it. It shipped tested and documented.

### 2.4 Dedupe is O(n) deserialization per delivery

`src/DigitalBrain.Kernel/Neuron.cs`:

```csharp
private bool HasAlreadyHandled(Synapse synapse)
    => Incoming.Any(recorded => recorded.Stamped.SynapseId == synapse.Stamped.SynapseId);

protected IReadOnlyList<Synapse> Incoming => Read(_incoming);

private List<Synapse> Read(IDurableList<byte[]> journal)
    => journal.Select(_synapses.Deserialize).ToList();
```

Every delivery deserializes the entire incoming journal, allocates a list, and linearly scans
it. Cost is O(n) per message and O(n²) over a neuron's lifetime. `website/status.md` describes
this as "journals grow without bound," which understates it — the growth is in CPU per message,
not only storage.

### 2.5 The outbox has head-of-line blocking

`Neuron.DrainAsync` processes `_outbox[0]` and `break`s out of the loop when any receiver of
that entry is undelivered. A single unreachable receiver stalls **all** outgoing traffic from
that neuron until the retry horizon expires — `DeliveryPolicy.RetryHorizon` is 30 minutes.
This is not listed as a debt anywhere.

### 2.6 The owner boundary does not constrain clients, and the code shows why

`src/DigitalBrain.Kernel/OwnerBoundCallFilter.cs`:

```csharp
if (OwnerOf(context.SourceId) is { } caller) { /* checks */ }
return context.Invoke();
```

`OwnerOf` returns `null` when the source has no `/` in its grain key — which includes every
call originating from an Orleans client rather than a grain. A null caller falls through to
`context.Invoke()` with no check. The documented debt is accurate; the mechanism is that
unattributed callers are unconstrained by construction.

**This becomes acute in §5.4.** Connection neurons will hold OAuth refresh tokens.

### 2.7 Broadcast requires the subscriber to have activated at least once

`Neuron.OnActivateAsync` registers the neuron's handled synapse types with the per-owner
`SubscriptionRegistry`. `EmitAsync` reads subscribers at emit time. A neuron that has never
activated is not registered and does not receive the broadcast.

The flagship sample works around this:

```csharp
// samples/DigitalBrain.Multiagent/Program.cs
foreach (var panellist in (string[])[nameof(Optimist), nameof(Skeptic), nameof(Scribe)])
{
    await brain.Neuron(panellist, "one").ReadJournalAsync(JournalKind.Incoming);
}
```

A dummy journal read whose only purpose is to force grain activation so subscription
registration happens. The workaround is in the showcase.

### 2.8 The client cannot observe

`src/DigitalBrain.Client/BrainClient.cs` has exactly two verbs: `FireAsync` and
`ReadJournalAsync`. `NeuronHandle` has one method. The multiagent sample polls in a
`for (var probe = 0; probe < 100; probe++)` loop with `Task.Delay(100)`.

**`ReadJournalAsync` is the entire client read API.** This is load-bearing for §4.1: the
journal is not a log *about* state, it *is* the state as far as any consumer is concerned.

### 2.9 Two mechanisms exist for one piece of knowledge

`SynapseDispatch.Build` discovers handlers by reflecting over `IHandle<>` at runtime.
`DispatchManifestGenerator` emits the same information at compile time into
`DigitalBrain.Generated.DispatchManifest`. The generated manifest is consumed only by
`tests/DigitalBrain.Tests/DispatchManifestContracts.cs`. Actual dispatch uses reflection.

The generator is decorative. It proves a property of the code; it does not participate in it.

### 2.10 Dead and vestigial surface

- `src/DigitalBrain.Abstractions/IAnswer.cs` — one property, `string Text`. Implemented by two
  test/probe records, consumed by two `OfType<IAnswer>()` filters. Not a framework concept.
- `RoutingMode` — recorded on every `SynapseMetadata`, decides nothing in the kernel. No
  branch anywhere reads it.
- `NeuronId.GrainTypeNameOf` strips a trailing `"Grain"` suffix. This framework's convention is
  `EchoNeuron`, `GreeterNeuron`, `ThinkerNeuron`. The suffix-stripping branch is unreachable
  under the framework's own naming.
- `hosts/Brain.Kernel.Host/`, `hosts/DigitalBrain.ServiceDefaults/` — gitignored, not in the
  solution, leftover from v1.
- `kernel/`, `modules/`, `integrations/` — gitignored top-level directories containing **zero
  `.cs` files**. Empty husks holding only `bin`/`obj` from the v1 demolition. `modules/` has
  fifteen subdirectories (`Brain.Modules.Ai`, `Flutter`, `Google`, `Salesforce`, …) and no
  source. They are invisible to `git status` and visible to every file search.

### 2.11 `.mcp.json` does not exist

`CLAUDE.md` mandates a Pre-Change Ritual whose second step is to use "the `codegraph` MCP
server (from .mcp.json)". There is no `.mcp.json` in this repository. `.codegraph/codegraph.db`
exists (229 MB, rebuilt recently), so the index is stood up and unreachable.

A working `.mcp.json` exists at `sources/brain_from_master/.mcp.json`, together with a
sentinel-guarded MSBuild auto-init target in that tree's `Directory.Build.targets`.

### 2.12 `sources/` leaks stale skills into every session in this repository

Nested `.claude/skills`, `.agents/skills`, and `.github/skills` directories under
`sources/Projects/{digitalbrain, final, IAW, ino, self-improving}` — with `ino` carrying three
nested copies — inject path-scoped skills into any Claude Code session that reads under
`sources/`. Observed continuously during the research for this document.

Among them is `operate`, which instructs an agent to "resume the DigitalBrain v6 simplification
roadmap" from `docs/final-simplification/PROGRESS.md`. That roadmap belongs to a different
generation.

### 2.13 `sources/` is 1.6 GB, of which 1.23 GB is build output

`bin`/`obj` account for roughly 77% of the tree. `ino/` alone contributes 1.19 GB of it —
about 94% of `sources/Projects` by size is one prototype's build artifacts.

### 2.14 The retirement reversibility premise is currently false

`.gitignore` line 12 is `sources/`. Files tracked before that line was added remain tracked
(8,452 of them), but **20 real source files are on disk and not in git**. Thirteen are
`sources/Projects/digitalbrain-app/packages/digital_brain_sdk_flutter/` — a complete Dart SDK
package. Deleting `sources/` today destroys them permanently.

### 2.15 `sources/brain_from_master` is not an earlier generation

Its design documents are dated **2026-07-16**, three days before this repository's HEAD. Its
proto declares `package digitalbrain.v2.ui`. It already solves three debts v2 lists as open:
journal compaction (bounded by count *and* bytes, compacted on every transition),
subscription/cursor cleanup, and the timeline. It contains `EVERYTHING-IS-A-NEURON.md` (626
lines, approved) and a complete module-SDK design.

It is not in `GOAL.md`'s Harvest Map and it was not in this review's original research brief.

---

## 3. Ratified decisions

These are settled. They are recorded here so the next plan cannot discard them silently.

### DEC-1 — A neuron's journal is a snapshot plus a bounded delta log

State shape: a snapshot of current records (latest per key), a delta log bounded by **both**
record count and total payload bytes, and a monotonic cursor with the invariant
`History[^1].Sequence == LastSequence`.

A reader whose cursor has fallen off the log receives a **reset carrying a full snapshot and a
resume sequence** — never a gap, never silence.

**Scope correction, 2026-07-20.** Phase 2.1 proved that an incoming or outgoing synapse journal has
no stable state key. Keying by synapse type collapses distinct facts and their counts; keying by
`SynapseId` makes every fact unique and prevents compaction. For those journals, the snapshot is the
complete durable summary: total recorded, tally by synapse type, retained window bounds, and last
sequence. A reset carries that summary and its resume sequence; it does not pretend evicted payload
history still exists. The literal latest-per-key snapshot remains a requirement on B-1's keyed
per-identity feed in Phase 2.6. This records the scope distinction implemented in Phase 2.1 instead
of leaving it only in that commit's message.

**Rejected: unbounded log of record.** v2 pays quadratic dedupe (§2.4) and unbounded storage
for a replay-from-genesis capability that no code path uses. Nothing in v2 replays journals to
rebuild state; state *is* the journal. The cost is real and the benefit is unrealised.

**Rejected: bounded ring buffer without a snapshot.** `digitalbrain` ships
`while (Outgoing.Count > 500) Outgoing.RemoveAt(0);` and gets away with it because its neurons
keep working state in fields. In v2, `ReadJournalAsync` is the entire client read API (§2.8).
Capping the journal without a snapshot silently deletes the only thing consumers can read.
**A one-line `MaxJournalEntries` cap in `Neuron.cs` looks like a cheap fix for the compaction
debt and would break every reader.** Do not do it.

**Cost, stated plainly:** the journal stops being an audit log. `CLAUDE.md` requires
self-evolution mutations to be "durable, replayable, rollback-capable." That requirement is
real and is satisfied separately — see DEC-1a.

### DEC-1a — Governance gets its own append-only ledger

Proposals, approvals, module installs, rollbacks, and connection grant/revoke events go to a
separate, low-traffic, genuinely append-only sink where unbounded retention is affordable and
wanted. Everything else lives in the bounded feed.

**Bounded feed for traffic, unbounded ledger for governance.** No prior generation drew this
line cleanly. `final` put governance synapses on the same bus as everything else;
`brain_from_master` bounded everything including approval history.

### DEC-2 — Modules are compile-time only

`AddModule<T>()` is a builder call, not an installer. Install is `dotnet add package` plus a
rebuild. There is no `.brain` archive, no signature verification, no collectible
`AssemblyLoadContext`, no quarantine world.

**Rationale:** Orleans builds its grain type manifest at silo startup and Aspire freezes
AppHost topology after `builder.Build()`. A module installed at runtime cannot contribute typed
grain types. `ino/docs` records these as the two hard constraints that forced its entire
L1/L2/L3 split.

**Every generation that wanted both shipped two parallel neuron kinds and never unified them:**
`digitalbrain` had `DynamicNeuronGrain` + `InterpretedNeuronRegistry` beside typed neurons;
`ino` had Genesis (code-only, no new synapse types, no UI); `v3` had a collectible-ALC gate that
unloaded after a single test run and never hosted anything resident. Compile-time-only avoids
the second kind by never creating it.

Runtime-created neurons are deferred, not rejected. Revisit only with a resident isolated host
design, which nothing in this lineage has built.

**Scope correction, 2026-07-19.** DEC-2's rationale is verified and stands. Its *scope* was too
broad: it forbade runtime code because it assumed runtime code means new grain **types**. A
behavior (DEC-8) contributes no grain type — it is an instance of one type registered at startup,
carrying a script as durable state. The Orleans manifest never changes and no peer silo needs to
learn anything. The rationale does not reach behaviors, so the prohibition does not either.

The line DEC-2 actually draws, stated correctly:

| | Contributes | When | Requires |
|---|---|---|---|
| **Module** | Vocabulary — synapse records, neuron interfaces | Compile time | Rebuild |
| **Behavior** | Logic over existing vocabulary | Runtime | Approval only |

New nouns need a rebuild. New verbs do not. Behaviour is the common case; vocabulary is rare.

### DEC-3 — A module ships as two packages, and `.Contracts` is the capability surface

```
Brain.Modules.<Name>.Contracts   leaf; zero dependencies; synapse records + neuron interfaces
Brain.Modules.<Name>             grains, vendor SDKs, provider adapters
```

**Amended 2026-07-19: two packages, not three.** `.Testing` is deferred until a second
implementation exists to conform against — a conformance harness validated by nothing is a
template, and §12 already records what this lineage does with those. It returns with the second
provider adapter, which is the first moment it can be proved real.

**`.Contracts` gained a second, larger job than package hygiene.** Under DEC-8 a behavior script
compiles against a reference set, and that reference set *is* its capability surface: a script
that cannot name `IGmail` cannot reach Gmail. So `.Contracts` is no longer only "the leaf that
keeps vendor SDKs out of consumers" — it is the unit in which capability is granted. That makes
the split load-bearing on day one rather than aspirational.

`IHandle<T>` and `IEmit<T>` are declared **on the interface**, in `.Contracts` — the pattern
`v3` used so the wiring graph is reflectable without loading or executing the implementation
assembly.

**Rationale:** v2 already has the one-package bug at framework scale. `DigitalBrain.Kernel`
references two vendor SDKs because the AI concern's contract was never separated from its
implementation. One package per module reproduces that bug once per module, forever.

Three independent generations converged on the split: `ino`
(`Ino.Domains.Travel.Contracts`), `v3` (the capsule triplet), `final` (contract-only `.brain`
bundles that still grew subscriber counts with no implementation payload).

### DEC-4 — `.Testing` serves consumers and implementors, and references `.Contracts` only

**Deferred by DEC-3's amendment.** The design below is ratified and unbuilt. It lands with the
second provider adapter, which is the first thing that can prove a conformance suite conforms
anything. Recorded here so it is not re-derived.

Consumer fakes so a dependent's tests never hit Google. A conformance suite so an alternative
implementation can prove it behaves.

**The hard rule:** `.Testing` must not reference the implementation. If it does, a consumer
testing against the fake pulls the vendor SDK anyway, which defeats DEC-3 entirely. Enforced by
reflection test, not convention.

`brain_from_master`'s module SDK spec makes conformance the point — *"the conformance suite
ships with the SDK and runs for every module"* — and proves it wasn't theatre by implementing
Salesforce from Google's template specifically to demonstrate the template was real.

### DEC-5 — The kernel owns connection lifecycle; modules supply provider adapters

A module **declares** what it needs and never implements auth.

Three declarations, which are genuinely different and must not be collapsed:

| Declaration | Scope | Example |
|---|---|---|
| App credential | Application — one per deployment | Google OAuth client id/secret; Anthropic API key |
| User authorization | Owner — per person, refreshable, revocable | Alice's Gmail refresh token and granted scopes |
| Capability grant | Owner, with human-readable reason | "This module wants to send email on your behalf" |

The kernel owns: the state machine
(`NotConfigured → Authorizing → Authorized → Expired → Revoked`), token storage and refresh,
the consent flow, and a **closed health union**. The module supplies only the provider-specific
token exchange and refresh call.

**Health must be a closed union** — `Healthy | MissingAppCredentials | NotConfigured |
NotAuthorized | TokenExpired | ProviderError | NetworkError`. `brain_from_master`'s spec states
that *"generic bool+string health is a conformance failure"*, and it is right: the UI must
render a different action for "an administrator must configure the app" versus "you must sign
in" versus "retry". A boolean cannot drive that.

**Module isolation is not attempted, and this is deliberate.** A compile-time module is C#
compiled into the silo process. It can read any static, open any socket, read any environment
variable. Capability gating of in-process code is theatre and the prior art proves it: `ino`'s
own comment concedes *"any DI-resolvable `IAmbientFire` is effectively a sandbox escape for
whichever component owns it"*; `final` shipped `BundleHasGrant` as a hardcoded `return false`
and its own audit found grants evaluated *after* the privileged call had already fired.

The connection subsystem exists to avoid seven OAuth implementations, to make authorization
state observable, and to make consent explicit and revocable — **not** to defend against the
module.

### DEC-6 — Inference is a typed grain call to a model neuron

`IGpt5`, `IClaudeOpus47` and siblings are neuron interfaces. A caller makes a typed
request/response grain call. Observability comes from incoming-call-filter reification, not
from making inference a message.

**Rejected: raw injected `IChatClient`.** Fast and ergonomic, but an LLM call — the slowest,
costliest, least deterministic operation in the system — leaves no trace on the rail.

**Rejected: synapse hop (`LlmRequest`/`LlmResponse`).** It splits every AI neuron's logic
across two handlers with correlation state between them. `digitalbrain` built exactly this and
never called it: **`new LlmRequest(...)` appears zero times in that tree**, and `IGpt5` is
referenced exactly twice — its own declaration and its own implements-clause. The ergonomics
lost decisively.

DEC-6 is available only because of two other decisions: call-filter reification (B-1) makes any
grain call observable without caller cooperation, and DEC-1 makes journalling every inference
affordable.

**Recorded risk:** per-call overhead is unmeasured. Ten model calls in a turn is ten
activations and ten journal writes. If that proves too costly, the fallback is a raw client
plus explicit tracing — not the synapse hop.

**Resolved 2026-07-19 — §14.1's concurrency hole is closed, and DEC-6 stands.**

§14.1 rejected all three escapes from single-threaded activation. Every rejection assumed
observability comes from the **callee's** journal — which is why key-per-call was dismissed with
*"the journal holds one entry per activation and means nothing."*

That assumption is wrong under B-1. Reification records the call on the **caller's** feed. The
callee's journal is not the evidence and does not need to be meaningful. So the escape ino
shipped becomes available:

> **A model neuron is keyed by the ambient correlation id.** Each call gets its own activation, so
> nothing serialises across callers, and the traffic is fully observable because the caller
> recorded it.

`[Reentrant]` is not needed, so `History[^1].Sequence == LastSequence` is never at risk. The
"N feeds rather than one model's traffic" objection dissolves for the same reason: one model's
traffic is a query over the timeline, not a property of one grain's journal.

**Second correction: the pendulum table's evidence is weaker than stated.** It records
`digitalbrain`'s `[Llm<Models.Gpt5>]` as *"Built; never invoked."* `digitalbrain/docs/v6plan/
MULTI_AGENT_LOCAL_LLM.md` describes `GroupChatNeuron` carrying `[Llm<Gpt5>] IChatClient chat` on a
live path. The document is marked *"Phase 1 in progress"*, so it describes intent rather than a
verified state — but DEC-6's "the ergonomics lost decisively" argument leans on the
never-invoked claim and should not lean on it alone.

**Third: what actually differs this time.** The typed model interface is on the call path *and*
the call path is journaled. In `digitalbrain` the typed neurons existed and every real call
bypassed them, invisibly. Here a bypass is visible, because a call that is not on the feed is a
call that did not go through the rail — which is a testable property, not a hope.

**Note on the pendulum.** This is the fourth attempt at typed model selection in this lineage:

| Generation | Design | Fate |
|---|---|---|
| `self-improving` | `[LLM<Gemma4_26b>] IChatClient` attribute injection | Deleted; `final/docs/DELETED.md` records supersession by hosting-layer tiering |
| `final` | `WithLlm<T>().AsFast()/.AsBalanced()/.AsReasoning()` | Superseded |
| `ino` 2026-03-21 | Marker class per tier | Abandoned |
| `ino` 2026-04-24 | `enum LlmTier` + keyed DI | Became v2's `ModelTier` |
| `digitalbrain` | `[Llm<Models.Gpt5>]` attribute injection | Built; never invoked; Anthropic adapter throws `NotSupportedException` |
| **this plan** | Typed model **neuron**, called as a grain | — |

What differs this time: the typed types are on the **call path**, not a catalog decoration
beside it. In `digitalbrain` the typed neurons existed and every real call bypassed them. If
that happens again, the design has failed and the fallback above applies.

### DEC-7 — UI components are a module-contributed, enumerable catalog

The Flutter **surface** is a neuron: durable, owner-scoped, observable. The cluster knows a
surface is attached, what vocabulary it declares it can render, and what is currently on it.

A **component** is a declared, versioned contract contributed by a module — enumerable by the
cluster, **not** an individually addressable actor.

Two rules, both derived from named defects:

1. **Unknown component is a hard error.** `digitalbrain-app`'s `ui_registry.dart` ends
   `default: return const SizedBox.shrink()` — a server referencing a component the client does
   not ship renders nothing, silently, with no telemetry. For a third-party catalog that is
   disqualifying. Vocabulary is negotiated at connect; a mismatch surfaces to the developer.
2. **Component-local state never round-trips.** Hover, focus, toggle, text entry, and scroll
   are client-local. Only committed intent becomes a synapse. This is the explicit fix for the
   defect where `SurfaceView._submit` disabled all actions and awaited a unary RPC, forcing a
   bespoke optimistic path just for chat.

**Rejected: generic renderer (RFW-style).** The newest generation in `sources/` built it,
shipped it, and is deleting it — budgeted at ~10–12k Dart lines and six dependencies — with the
written rule: *"New expressive power requires an SDK version bump and a new first-party view —
never a more powerful interpreter. There is deliberately no generic renderer."*

**Rejected: closed sealed union of first-party views only.** Safe and typed, but modules cannot
contribute UI, which forecloses third parties building applications with UI inside DigitalBrain.

**Why the prior art's fatal objection no longer applies.** Every generation's server-driven UI
died on *"no new widget type without a Flutter rebuild."* DEC-2 already accepts rebuild-to-
install. The thing that killed server-driven UI in every prior generation is now the install
process.

**Recorded risk:** a module shipping UI ships two languages, and the C# declaration and Dart
implementation can drift. Mitigated by making the C# declaration the source of truth and
failing the build from the module's `.Testing` package when the Dart package does not implement
what the contract declares.

**Amended 2026-07-19 — a UI component is a capability, not a catalog.**

DEC-7 as written builds a component catalog, a vocabulary negotiation, and a surface protocol.
Under DEC-8 none of that is a separate mechanism. A UI component is a **neuron interface in a UI
module's `.Contracts`**, resolved like any other capability:

```csharp
var calendar = brain.Get<ICalendar>();
var chosen   = await calendar.PickDateAsync("When shall we meet?");
```

Both of DEC-7's rules survive, and get cheaper:

1. **Unknown component is a hard error** — free. A script that does not reference the contract
   does not compile; `Get<T>()` throws when the owner's surface does not declare it. No bespoke
   negotiation protocol.
2. **Component-local state never round-trips** — unchanged, and now load-bearing for a second
   reason: only committed intent crosses the boundary, which is what keeps UI traffic off the
   feed (§14.3).

A UI interaction is an **unbounded wait for a human**, so it is the canonical case for DEC-9's
durable request — the UI validates that mechanism rather than straining it.

What is deleted: the component catalog as a distinct registry, vocabulary negotiation as a
distinct handshake, and the surface protocol as a distinct wire format. What remains: a module
whose contracts happen to render, and the drift guard above.

### DEC-8 — The client API is the programming model, and a behavior is a neuron carrying a script

This is the core feature. Everything above serves it.

A behavior is an ordinary C# file using the client API. No globals, no attributes, no base class,
no framework vocabulary:

```csharp
var brain = DigitalBrainClient.Connect();
var gpt   = brain.Get<IGpt56>();

await brain.On<NewMail>(async mail =>
{
    var verdict = await gpt.AskAsync($"Is this urgent? {mail.Body}");
    if (verdict.IsUrgent) await brain.Emit(new Escalation(mail.From));
});
```

**The same file is a client script and a behavior.** Only the hosting differs:

| | Outside the cluster | Inside, as `IBehavior` |
|---|---|---|
| `Connect()` | Opens a cluster connection | Ambient context bound to this behavior's identity |
| `Get<T>()` | Typed proxy over the wire | Typed proxy over the local grain factory |
| `On<T>()` | Subscription | Handler registration |
| Lifetime | The process | Durable — journal, state, survives restart |

**`IBehavior : INeuron` is one grain type, registered at startup.** The script is durable state,
not a type. A new behavior is a new *instance*, and Orleans grains are virtual, so every instance
name already exists. This is why DEC-2's constraint is untouched, and it is why every prior
generation's second neuron kind (`DynamicNeuronGrain`, Genesis, the collectible ALC) is not
recreated here: there is no second kind.

**Addressing.** `Get<T>()` resolves `(T, ambient owner, "default")`; `Get<T>(name)` reaches a named
sibling. **Owner is always ambient and never a parameter**, so a script cannot address another
owner's neuron — the boundary becomes unstateable rather than merely enforced.

**Install is a recording pass.** The script is run once with a context in which `On<T>` and
`Get<T>` record instead of acting. What comes back is the manifest — handled facts, requested
capabilities, emitted facts — and *that* is the approval screen. It is derived from the code, so
it cannot drift from it, and a script cannot hold a capability it did not ask for, because asking
is how it gets recorded.

**Every install is a human-approved proposal**, including a behavior authoring or modifying a
behavior. The script is the state, so approval is a journal entry and rollback is reverting one.
This is the rail `CLAUDE.md` calls the product, and B-6 is its ledger.

**Capability is enforced at `Get<T>()`, and only for behaviors.** Compiled module code is
in-process C# and DEC-5 already concedes that gating it is theatre; `final` proved it by shipping
`BundleHasGrant` as a hardcoded `return false` whose own audit found grants evaluated after the
privileged call had fired. The boundary is **script versus compiled code**, and this plan says so
plainly so it cannot decay into the same fake enforcement.

**Behaviors provide capabilities, not only consume them.** A script may implement a contract
interface, and other scripts resolve it with `Get<T>()`. Consistent with the split: the interface
is vocabulary (module, compile time, in the manifest); the implementation is behavior (script,
runtime, instance state). This is how the brain composes itself out of its own parts.

**Both hosting modes ship together, gated on R-3.** Running a script as an external cluster client
is precisely the configuration this lineage records as its failure mode — IAW's raw
unauthenticated `IClusterClient` with full host authority, which is §2.6. Nothing scriptable ships
before that hole is closed.

**Recorded risk — the generation benchmark.** This design assumes an LLM can reliably emit these
scripts. §11 records `final`'s Gate-0 as the method for such a claim, and the harvest found that
instance failed in both halves (see §10). Before the scripting rail is load-bearing, it needs a
benchmark **run against a real model**, with a pre-committed numeric threshold, and demotion
enforced against the codebase rather than the spec.

### DEC-9 — A synapse is a fact; an interface method is a request

The rule that stops the typed path and the message path from competing. Every prior generation
had both and let one silently win.

| | Shape | Direction | Reply | Journaled |
|---|---|---|---|---|
| **Fact** | thin record — `NewMail` | broadcast, undirected | none | yes |
| **Request** | interface method — `gmail.SendAsync` | directed at a capability | yes | yes, by reification |

Every behavior reads the same way: **facts in, requests out to capabilities, facts out.**

**A request may also be a trigger.** A behavior implementing a contract interface is invoked
directly, not only by a fact arriving. This is what makes behaviors composable as capabilities.
The cost is accepted knowingly: a behavior can run with no fact on the rail explaining why, so the
reification record is the *only* evidence — which is a further reason B-1 layer 1 is not optional.

**Both verbs are journaled. Neither is privileged.** DEC-6 rejected the synapse hop because
hand-written correlation splits logic across two handlers — `digitalbrain` built exactly that and
`new LlmRequest(...)` appears **zero times** in that tree. That objection is entirely about
hand-writing it. A generated proxy gives the author one `await` and the rail both records.

**MCP is a provider adapter, behind `.Contracts`.** `IGmail` may be implemented over Google's REST
API, an MCP server, or a model with tools. A script cannot tell and must not care. §4.1's phrase
"external MCP-style caller" means the *opposite* direction — something outside calling in. Two
different things; the plan previously conflated them.

### DEC-10 — A synapse is a thin record; delivery metadata lives on the envelope

`Synapse.Stamped` is a property that throws when unset — metadata welded onto the payload, so a
payload can never be a plain record. It is not renamed. It stops existing.

```csharp
public sealed record NewMail(string From, string Subject, string Body);
```

Id, correlation, sequence and timestamps ride on a delivery envelope the kernel owns and the
author never constructs. Authors — and models generating behaviors — write exactly the record they
mean, and the kernel cannot be handed an unstamped delivery because stamping is no longer the
payload's job.

This merges what were three separate items: §7's `Stamped` rename, D-3's `RoutingMode` removal
(also metadata nothing reads), and part of R-1.

**Breaks:** the serialization surface, `StampingContracts`, `SerializationContracts`.

---

## 4. Target architecture

### 4.1 The observation primitive

One durable feed per identity, not per connection. This is the load-bearing invariant and
everything else follows from it: a module mutating state and an external MCP client mutating
state converge on the same feed object, so a client observing that feed sees both.

Three layers:

1. **Reification.** An incoming grain call filter turns any direct grain call on a neuron into
   a timeline entry. The caller does not cooperate and cannot opt out. This is what makes
   external mutation observable.
2. **The feed.** Snapshot plus bounded delta log plus cursor (DEC-1), per identity.
3. **The wire.** `Snapshot → cursor`, `ReadSince(cursor) → delta`, `Watch → stream`, with a
   reset carrying a full snapshot when the cursor has fallen off the log.

**Do not reproduce the server-side poll.** `brain_from_master` presents a streaming API over
`WaitForChangeAsync`, which is a 250 ms `Task.Delay` loop reading a grain, per connected client,
forever. That is better than v2 (which pushes polling to N clients over the network) and is
still not an observation primitive.

**Correction, 2026-07-19 — this section cited `TimelineRelayGrain` for something it does not do.**
It was described here as *"a relay grain with an implicit stream subscription that requires no
registration"*, offered as the model for broadcast. It is a **fan-in observer**: `Guid.Empty`-keyed,
*"one activation per cluster"* by its own comment, reading the whole timeline and pushing onto a
silo-local UI bus. The implicit subscription buys *"the observer needs no registration and no prior
activation"* — genuinely valuable, and nothing whatever about enumerating recipients. Recipient
enumeration in that tree is catalog-driven, in `NavigatorRouter.ResolveSubscribersAsync`, which
filters a catalog by handled type. B-1 must not be written as though the relay solves addressing.

**Amended shape — the feed is a neuron, not a subsystem.**

R-1 already builds snapshot + bounded delta log + monotonic cursor **inside every neuron**. A
per-identity feed with those three properties is therefore one neuron type, not new machinery:

- **Layer 2 becomes `FeedNeuron : Neuron`** at `(Feed, owner, "feed")`, inheriting the state
  machine R-1 must build regardless.
- **Layer 3 becomes one verb.** `ReadJournalAsync(cursor)` is R-1's work; `WatchAsync` is added to
  `INeuron` generally, so **every** neuron is watchable for the same cost. There is no bespoke
  wire protocol.
- **Feed subscription is the broadcast decision.** A feed subscribes to everything in its owner
  scope, which makes it an ordinary broadcast subscriber. B-1 layer 2, R-4, and broadcast
  addressing are one mechanism, decided once — the plan previously had them as three items and
  never noticed.

**Layer 1 is the bridge, and is not optional.** Under DEC-9 a typed request carries real work and
leaves no trace unless reified. Its justification is not the external MCP caller this section
originally gave — it is that **every behavior script's typed calls must land on the rail without
the script cooperating.** A behavior triggered by a request rather than a fact has no other
evidence that it ran.

**§14.3 is answered by the code, not by a new policy.** `SynapseObserver` already observes via
`ActivityListener` over `SynapseTelemetry`, push-based, no polling. Everything is *already*
traced. The line is therefore: **domain facts are feed-worthy; call traffic is traced and reified,
not accumulated.** Two further discriminators come from ino, which asked this and left it open —
self-grain calls versus cross-grain calls, and hard caps (it used a 1 s emit timeout and a
4096-byte payload cap, with the note *"the per-call overhead is real. Profile before scaling
neuron counts up."*).

**Take `brain_from_master`'s client contract.** It is already written, has 36 test files against a
fake transport, and works unchanged the day the server stops polling.

### 4.2 The module

```csharp
builder.AddDigitalBrain()
       .AddModule<AIModule>(ai => ai.WithProvider<Ollama>())
       .AddModule<GoogleModule>()
       .AddModule<FlutterModule>();
```

A module is **an assembly plus a descriptor**. Not a manifest file, not a DI bundle alone.

Discovery is explicit registration, not assembly scanning. `AddModule<T>()` names the module;
the module's descriptor declares its contributions. Assembly scanning was used by `ino`,
`IAW`, and `digitalbrain`; `IAW` ended up with three independent `AppDomain` scans, each with
its own swallow-and-continue error handling. Explicit registration costs one line per module
and removes an entire class of "why isn't my neuron registered" failure.

**§14.6 resolved, 2026-07-19 — the descriptor does not restate the wiring graph.**

The tension was invented by listing neurons in the descriptor. Two prior generations converged on
not doing that. `brain_from_master`'s `ModuleDescriptor` — the only finished descriptor design in
the lineage — carries `(ModuleId, Version, DisplayName, Icon, Publisher, ConfigurationSchema,
SecretRequirement[], CapabilityDescriptor[], EffectDescriptor[], OAuthDescriptor?)` and **declares
no neurons and no synapse types at all**. `digitalbrain`'s v5 domain manifest is the same shape.
And ino states the rule outright: *"No manifest file. No custom descriptor. The `.csproj` metadata
+ attribute-driven source generation is the manifest."*

So: **wiring stays on the interfaces and is generated from them** (R-6's contract-manifest work);
the descriptor carries only what interfaces cannot express — configuration, secrets, capabilities,
effects, OAuth. There is no second declaration, therefore nothing to drift. `IEmit<T>` is not
redundant with the descriptor; it is the canonical source the descriptor's wiring half is
generated *from*, which also closes §11's open question about its future.

A module descriptor declares:

- ~~**Neurons** — grain types it contributes, with their handled and emitted synapse types.~~
  Struck: generated from the interfaces, never hand-declared.
- **Connections** — provider descriptors (DEC-5), including required app credentials, OAuth
  scopes, and callback shape.
- **Capabilities** — grants it will request, each carrying a user-facing reason string.
- **UI components** — declared vocabulary with versions (DEC-7).
- **Configuration** — schema and the scope that owns each key. Modules never choose the read
  scope themselves.
- **Module dependencies** — by contracts-package reference only.

Failure policy:

- **Duplicate neuron id** — reject at startup with both contributors named. `v4`'s
  `BundleInstaller` does `GroupBy(...).ToDictionary(..., g => g.First())`, silently keeping the
  first and dropping the rest. `v3`'s catalog does not detect collisions at all.
- **Missing module dependency** — reject at startup, naming the missing module. `final` checked
  requirements and, at one HEAD, warned without blocking.
- **Module fails to load** — fail the host. A partially composed brain is worse than one that
  refuses to start.

### 4.3 Scope discipline

v2 has `OwnerId` and nothing else. Five scopes, with explicit ownership:

| Scope | Owns |
|---|---|
| Application | App credentials, module registration, descriptors |
| Owner | Authorization state, refresh tokens, connections, grants |
| Actor | Private drafts, actor-scoped conversations |
| Session | Transport identity, feed cursors — **never product state** |
| Operation | Command ids, idempotency keys, effect fences |

The Session row is the one that matters most: it is what makes cross-transport reactivity work.
A feed keyed by identity rather than by connection is why an MCP mutation and a Flutter client
converge.

### 4.4 Identity

**Keep `NeuronId(type, owner, name)`.** Every prototype's hierarchical `ai/llm/openai/gpt-5`
was an unvalidated string that never routed. `digitalbrain` proved this against itself: it has
the hierarchical id *and* still infers domain from the CLR namespace, with a hardcoded
`sqlite → data` special case. Three incompatible conventions coexist in its `NeuronId` field —
slash paths, dotted CLR FQNs, and raw GUIDs.

Adopt the path form as a **display and grouping convention** on module descriptors. It reads
well and groups for free. It is documentation, and this plan says so rather than pretending it
is routing.

### 4.5 The programming model

DEC-8 in one page, because it is what the rest of the architecture exists to serve.

**One surface.** The client API is the programming model. There is exactly one thing to learn, and
it is the same whether the code runs on a laptop or inside the cluster.

```
Connect()      bind to a brain; identity is ambient thereafter
Get<T>()       resolve a capability          -> a request, replies       (DEC-9)
Get<T>(name)   resolve a named instance
On<T>()        react to a fact                                           (DEC-9)
Emit(fact)     announce a fact; broadcast, no reply                      (DEC-9)
```

**Two subscription regimes, and the split is principled.**

| | Handled set known | Registered | Cost |
|---|---|---|---|
| Compiled neuron | Composition time, from the interfaces | Startup | Zero — the kernel can skip emitting a fact nothing handles |
| Behavior | Install time, from the recording pass | Once, at install | Small — behaviors are few and explicitly created |

The zero-cost property is only available because compiled subscription is static: the kernel knows
at startup whether anything handles a type, so an unhandled lifecycle fact is never constructed. A
dynamic registry could never do this, because someone might subscribe later. **This is what makes
"everything is a fact" affordable rather than ruinous**, and it is the structural half of §14.3's
answer.

The dynamic half is bounded and is *not* §2.7's defect. That defect is a registry write on the
activation path of every neuron. This is a registry write at install, for behaviors only.

**Lifecycle facts, and what is deliberately not one.** `Activated` and `Deactivated` are Orleans
placement events — they fire on silo restart, rebalance, and idle-timeout expiry, so a behavior
handling one is coupled to cluster topology rather than to the domain. `final` put them on the bus
as first-class synapses and no document in any tree records anything handling one. What authors
actually reach for is a domain fact — *created*, or *first fact received* — and those are
deterministic and replayable. Runtime activation is traced, not fed.

**The script's top level is its activation path.** Handler closures do not survive deactivation, so
the top level re-runs on activation to rebuild them. This falls out of the shape rather than being
designed, and it is why the surface has no separate initialization concept.

---

## 5. DELETE

### D-1 — The model-tier abstraction, as a consequence not a task

**What:** `src/DigitalBrain.Abstractions/ModelTier.cs`,
`src/DigitalBrain.Abstractions/ModelProviders.cs`,
`src/DigitalBrain.Kernel/ModelBinding.cs`, `src/DigitalBrain.Kernel/ModelConfiguration.cs`,
`Neuron.AskModelAsync`, `BrainService.WithModel`, the `Anthropic` /
`Microsoft.Extensions.AI` / `Microsoft.Extensions.AI.OpenAI` package references,
`SimulationCluster.Models`, and the tier parsing in `NeuronSteps`.

**Why wrong:** tier indirection over concrete models; one member (`Embedding`) that cannot
work (§2.3); and — the real defect — it is the reason the kernel references two vendor SDKs.

**Replaces:** nothing directly. AI becomes an ordinary module (B-4) and all of the above leaves
the kernel as a consequence.

**Breaks:** `hosts/DigitalBrain.AppHost`, `hosts/DigitalBrain.TestingAppHost`,
`hosts/DigitalBrain.ProbeHost`, `tests/DigitalBrain.Simulations/ThinkerNeuron.cs`,
`tests/DigitalBrain.Simulations/Models.feature`,
`tests/DigitalBrain.Tests/{BrainHostingContracts,ProviderAdapterContracts,SerializationContracts}.cs`,
`website/packages/{abstractions,aspire-hosting,kernel}.md`. 23 files.

**Proves it:** a reflection test asserting `DigitalBrain.Kernel`'s referenced assemblies contain
no vendor SDK — the pattern `final` used
(`Assert.DoesNotContain("DigitalBrain.Core", asm.GetReferencedAssemblies()...)`). This test is
written **first**, fails, and is the gate for the whole item.

### D-2 — `IAnswer`

**What:** `src/DigitalBrain.Abstractions/IAnswer.cs`.

**Why wrong:** a one-property interface (`string Text`) implemented by two test/probe records
and consumed by two `OfType<IAnswer>()` filters. It is a test convenience shipped as a
framework concept.

**Replaces:** nothing. Test neurons assert on their own concrete types.

**Breaks:** `hosts/DigitalBrain.ProbeHost/{Neurons,Program}.cs`,
`src/DigitalBrain.Testing/NeuronSteps.cs`, `tests/DigitalBrain.Simulations/ThinkerNeuron.cs`.

**Proves it:** the public API baseline test (`PublicApiBaselineContracts`) shows the removal;
the simulation suite stays green.

### D-3 — `RoutingMode`

**What:** `src/DigitalBrain.Abstractions/RoutingMode.cs` and the `RoutingMode` member of
`SynapseMetadata`.

**Why wrong:** recorded on every synapse, read by no branch in the kernel. Pure ceremony on the
hot path and on every serialized payload.

**Caveat, and the reason this is not a pure delete:** `v3/docs/02-ino-and-broadcast.md` argues
explicitly that routing must be metadata on the fire-act rather than a `Broadcast : Synapse`
subtype, because payload and delivery are orthogonal and a subtype split forces duplicate
payload types for one fact. **That argument is correct and this plan preserves it.** What is
being deleted is a field nothing reads. If a delivery decision later needs to branch on routing,
it comes back as a decision input rather than a recorded fact.

**Breaks:** `SynapseMetadata` shape (serialization surface),
`tests/DigitalBrain.Tests/{StampingContracts,SerializationContracts}.cs`.

**Proves it:** serialization contract test updated; simulation suite green.

### D-4 — Vestigial identity code

**What:** the `"Grain"` suffix-stripping branch in `NeuronId.GrainTypeNameOf`.

**Why wrong:** unreachable under this framework's own naming convention (`EchoNeuron`, not
`EchoGrain`). Cargo-culted from Orleans.

**Breaks:** `tests/DigitalBrain.Tests/GrainTypeNamingContracts.cs` — which currently tests the
unreachable branch.

**Proves it:** naming contract test rewritten to assert the actual convention.

### D-5 — The v1 husks

**What:** `kernel/`, `modules/`, `integrations/`, `hosts/Brain.Kernel.Host/`,
`hosts/DigitalBrain.ServiceDefaults/`, `.superpowers/`.

**Why wrong:** zero `.cs` files (§2.10). Empty directory skeletons holding `bin`/`obj` from the
v1 demolition. Gitignored, so invisible to `git status` and visible to every grep, glob, and
file search — including `modules/` with fifteen subdirectories that look like a module system
and contain nothing.

**Breaks:** nothing. Verified.

**Proves it:** `find kernel modules integrations -name '*.cs'` returns empty before deletion;
root suite green after.

### D-6 — The activation workaround in the flagship sample

**What:** the `foreach` loop in `samples/DigitalBrain.Multiagent/Program.cs` that reads each
neuron's journal purely to force activation, and the 100-iteration `Settled` polling helper.

**Why wrong:** it is a defect (§2.7) documented as a code pattern in the showcase sample.

**Replaces:** R-4 fixes late subscription; B-1 replaces polling with observation.

**Breaks:** the sample, until R-4 and B-1 land. **This item is ordered after them.**

**Proves it:** the sample runs correctly with the loop removed and with no `Task.Delay`.

---

## 6. REFACTOR

### R-1 — Journal becomes snapshot plus bounded delta log

**What:** `src/DigitalBrain.Kernel/Neuron.cs` — `_incoming`, `_outgoing`, `Read`,
`HasAlreadyHandled`, `ReadJournalAsync`.

**Why wrong:** unbounded growth; O(n) deserialization per delivery (§2.4).

**Replaces:** DEC-1's shape. Dedupe moves from a linear scan of the full journal to a bounded
set of recent synapse ids — O(1). Two version axes, kept distinct: a grain revision for
optimistic concurrency, and a per-record sequence for reader cursors. Conflating them is the
classic bug in this shape.

**Breaks:** `INeuron.ReadJournalAsync(JournalKind)` — the entire client read API. Becomes a
cursor-based read. Every consumer changes: `BrainClient`, `NeuronHandle`, both samples,
`ProbeHost`, `SimulationCluster`, the Reqnroll steps.

**Proves it:**
- a scenario firing more synapses than the bound and asserting the snapshot remains correct
  while the delta log evicts;
- a scenario asserting a reader with a stale cursor receives a reset with a full snapshot and a
  resume sequence, not a gap;
- a test asserting the invariant `History[^1].Sequence == LastSequence` after compaction;
- a benchmark or counter proving dedupe cost is constant with respect to journal length.

### R-2 — Outbox head-of-line blocking

**What:** `Neuron.DrainAsync`.

**Why wrong:** one unreachable receiver stalls all outgoing traffic from that neuron for up to
the 30-minute retry horizon (§2.5). Undocumented.

**Replaces:** per-receiver progress. An entry with a failing receiver must not block entries
behind it.

**Breaks:** delivery ordering guarantees. **This needs an explicit decision on what ordering is
promised** — currently the code promises FIFO by construction and the documentation does not
state it. Resolve before implementing.

**Proves it:** a scenario with one unreachable receiver asserting that traffic to reachable
receivers continues to flow.

### R-3 — The owner boundary

**What:** `src/DigitalBrain.Kernel/OwnerBoundCallFilter.cs`.

**Why wrong:** an unattributed caller (any Orleans client) falls through unchecked (§2.6). This
is documented as a known limitation and becomes a credential-exposure vector once connection
neurons hold refresh tokens (DEC-5).

**Replaces:** not a code fix alone. The correct boundary is at the edge, and the plan must state
that the Orleans client endpoint is never publicly reachable as a **load-bearing invariant with
a test**, not a deployment footnote.

**Breaks:** potentially the hosted test topology, which currently drives proofs from a probe
host inside the cluster precisely because an external client cannot complete a handshake through
an Aspire-proxied gateway.

**Proves it:** a test asserting the silo's client endpoint is not bound to a public interface in
the AppHost topology. Insufficient on its own; documented as such.

### R-4 — Late subscription

**What:** `Neuron.OnActivateAsync` registration and `SubscriptionRegistry`.

**Why wrong:** three separate problems. A neuron that has never activated never receives a
broadcast (§2.7). Subscriptions are never removed, so fan-out grows monotonically. The
per-owner registry grain is on the path of every emit and every activation — a single-threaded
hot spot by construction.

**Replaces:** subscription derived from the module descriptor at composition time rather than
discovered at activation time. Under DEC-2, the set of neuron types is known at startup — there
is no reason to learn it lazily. This removes the registration write from the activation path
and makes late subscription impossible rather than tolerated.

**Breaks:** the N+1 late-registration scenario, which currently proves a property that ceases to
exist. It is replaced by a startup-composition proof.

**Proves it:** a scenario asserting a never-activated neuron receives a broadcast; a scenario
asserting fan-out does not grow across repeated activation cycles.

### R-5 — Framework package decomposition

**What:** `DigitalBrain.Abstractions`, `.Kernel`, `.Client`, `.Testing`, `.Aspire`,
`.Aspire.Hosting`, `.DevTools`, and the `DigitalBrain` metapackage.

**Why wrong, in part:** the metapackage's stated rationale is that it *"deliberately excludes
`DigitalBrain.Kernel`, which is where provider SDKs and credentials live."* After D-1 the kernel
has neither, so the rationale evaporates and the metapackage needs a new one or should go.

**What survives scrutiny:** the `Aspire` / `Aspire.Hosting` split. `IAW` used the identical
split by name and responsibility, and it is right — hosting-integration types must not leak into
the runtime processes the client integration targets.

**Replaces:** `Abstractions` and `Kernel` stay. `Client` gains observation (B-1). `Testing`
gains the module conformance harness (B-2). The metapackage is re-justified or deleted.

**Breaks:** `tests/DigitalBrain.Tests/{PackableProjects,PackageBoundaryContracts,PackableSurfaceContracts}.cs`.

**Proves it:** leaf-assembly reflection tests, one per boundary, as in `final`.

### R-6 — Dispatch has one mechanism

**What:** `SynapseDispatch` (runtime reflection) and `DispatchManifestGenerator` (compile-time
manifest) both encode handler wiring; only the reflection path dispatches (§2.9).

**Why wrong:** two sources of truth for one fact, with the compile-time one decorative.

**Replaces:** two options, and this plan does not choose between them because the choice depends
on measurement not on argument:
- promote the generated manifest to the actual dispatch path (`digitalbrain` compiles
  `Expression.Lambda` per handler and caches it; `final`/`self-improving` emit a
  `FrozenDictionary` of pre-resolved invokers), or
- delete the generator and keep reflection.

**What is not optional:** the generator gains a second output that is genuinely load-bearing —
the module contract manifest (B-2). `final`'s `DispatchManifest.KnownContracts` already computes
exactly the `(NeuronInterface, SynapseType, IsHandle)` shape a module's contract declaration
needs, and never emits it as an artifact. `final/docs/DISTRIBUTION.md` names this as a deferred
follow-up. It is the lowest-effort, highest-leverage generator work available.

**Proves it:** dispatch benchmark if promoting; contract manifest round-trip test regardless.

### R-7 — The Pre-Change Ritual and `CLAUDE.md`

**What:** `CLAUDE.md`.

**Why wrong, item by item:**

- The ritual mandates a `codegraph` MCP server "from .mcp.json". **`.mcp.json` does not exist**
  (§2.11). The documented ritual is unfollowable as written, which means it is either being
  skipped silently or worked around — both worse than an accurate instruction. Fix: copy
  `sources/brain_from_master/.mcp.json` and its sentinel-guarded MSBuild auto-init target.
  Additionally, `CLAUDE.md`'s "do not manually explore files for architecture" is an absolute
  rule resting on an unpinned npm package invoked with `|| echo skipped` — it fails open and
  silently. Keep the tool; drop the absolutism.
- *"All plan/\*.md, archive, superpowers, old specs = trash. 99% of historical plans/specs are
  noise — kill them."* This rule came from `brain_from_master`'s `CLAUDE.md`. In that same tree
  the two highest-value documents live under `docs/superpowers/`. **The rule, applied literally,
  destroys the best artifacts its own repository produced.** Replace with: durable design
  rationale and decision records are kept; session logs, progress reports, and task checklists
  are deleted. That distinction is real and the current rule cannot express it.
- `COMMENTS ARE FORBIDDEN`. The evidence for what this buys: a ~140-line uncommented method with
  six catch clauses and a nested duplicated retry block, in the tree the rule came from. Narrow
  it to what it was for — no narrative comments, no commented-out code, no generated
  boilerplate — and stop forbidding the case where a name cannot carry the information.
- The North Star execution path names removed components (`Foundry execution loop`, `pack
  runtime`, `legacy gateway`, `second auth system`) that no longer correspond to anything on
  disk. Rewrite against the architecture in §4.

**Proves it:** the ritual is executable end to end by someone following it literally, from a
clean clone.

---

## 7. RENAME

Naming is mostly good. `Neuron`, `Synapse`, `NeuronId`, `OwnerId`, `CorrelationId` all carry
their meaning. The following do not.

**Reduced 2026-07-19. Churn is not progress.** A rename earns its place only if a new reader
stumbles over the current name. Rows that were matters of taste are struck, with the reasoning
kept so they are not silently reinstated.

| Current | Problem | Resolution |
|---|---|---|
| `Synapse.Stamped` | A property that throws when unset — metadata welded onto the payload, so a synapse can never be a plain record. | **Deleted, not renamed.** DEC-10: the synapse is a thin record and metadata rides on the delivery envelope. Lands in Phase 2.4. |
| `BrainService` / `BrainClientService` (Aspire.Hosting) | `BrainClientService` is not a service; it is a projection of `BrainService` for referencing consumers. The Aspire hosting model has a name for this and the code does not use it. | `BrainResource` / `BrainClientReference`, **and adopt the Aspire resource model properly** — a resource is a resource, a reference is a reference. This is the one rename that fixes a reader actively misled by the hosting integration. |
| ~~`IEmit<T>`~~ | — | Struck. §4.2 resolves it: `IEmit<T>` is the canonical declaration the descriptor's wiring half is generated *from*. The name is correct; it was the role that was unclear. |
| ~~`SynapseWiring` / `DispatchManifest`~~ | — | Struck as a rename. R-6 (Phase 3.3) resolves it by making one of them generated from the other; whatever survives keeps its name. |
| ~~`SimulationCluster`~~ | — | Struck. The static model table leaves with D-1, which removes the actual defect. The remaining name is accurate enough. |
| ~~`ScriptedModel`~~ | — | Struck as taste. `DeterministicModel` is marginally better and not worth a public-surface change. |
| ~~`hosts/DigitalBrain.ProbeHost`~~ | — | Struck; the original row already said keep. The reason it exists is recorded in R-3. |
| ~~`JournalKind.Incoming` / `Outgoing`~~ | — | Struck as a rename. Re-examined during R-1 as that row said; both survive. |

---

## 8. BUILD NEW

### B-1 — The observation primitive

**Reduced 2026-07-19 — see §4.1.** Layer 2 is a neuron (`FeedNeuron`) reusing R-1's state machine,
not a subsystem. Layer 3 is one verb (`WatchAsync` on `INeuron`), not a wire protocol. Layer 1
stays and is the load-bearing one: it is the bridge that puts DEC-9's typed requests on the rail
without the caller cooperating, and under DEC-9 a behavior triggered by a request has **no other
evidence that it ran.** Its justification is behaviors, not the external MCP caller named below.

**What:** §4.1's three layers — call-filter reification, the per-identity feed, and the wire
protocol.

**Why:** open debt #1. Clients can fire and read but cannot observe. It is also the prerequisite
for every other new thing in this plan: the UI module, connection health, and module install
events all need it.

**Sources:** `digitalbrain`'s `QuerySynapseSynthesizingIncomingFilter` and
`BrainTimelineRelayGrain` for the backend; `brain_from_master`'s `SurfaceFeedNeuron` state
machine and `ReadPage` gap detection for the feed; `brain_from_master`'s `RuntimeController` and
`FeedController.accept` for the client contract.

**Breaks:** `BrainClient` gains verbs. `ReadJournalAsync` becomes cursor-based (R-1).

**Proves it:**
- an external MCP-style caller mutates a neuron by direct grain call and an observing client
  receives the change **without the caller cooperating**;
- a client disconnects, changes occur, it reconnects with its cursor and catches up;
- a client's cursor falls off the bounded log and it receives a reset with a full snapshot;
- no `Task.Delay` appears in the server-side wait path.

### B-2 — The module system

**What:** `IModule`, the module descriptor, `AddModule<T>()`, the two-package template (DEC-3 as
amended; the conformance harness returns with the second adapter at Phase 3.7), and the
composition-time validation in §4.2.

**Why:** the core ask. It does not exist anywhere in this lineage — verified: zero occurrences
of `IModule`, `AddModule`, `ModuleRegistry`, or `ModuleDescriptor` across every `.cs` on disk,
including gitignored directories and all of `sources/`. This is genuinely new ground.

**Design inputs, none of them a template to copy wholesale:**
- `brain_from_master`'s `ModuleDescriptor` family and its five-scope ownership table — the
  closest thing to a finished design, and it was never built.
- `ino`'s `IDomain` — a hand-written verb manifest separate from the reflection scan. The
  separation of "what should be discoverable" from "what grain classes exist" is right.
- `IAW`'s `static virtual` interface metadata — metadata on the interface, compiler-checked,
  readable without instantiation.
- `final`'s contract-only bundles — publish the interface, let the consumer implement.
- `v3`'s `IHandle`/`IEmit` on the interface — the wiring graph reflectable without loading the
  implementation.

**Proves it:**
- a second module (`Salesforce`) built from the first module's template, as `brain_from_master`
  did deliberately to prove the template was real;
- duplicate neuron id across two modules fails startup naming both contributors;
- a missing module dependency fails startup naming the missing module;
- a `.Testing` package that references its implementation fails its own guard test;
- the AI module (B-4) passes conformance.

### B-3 — Connections

**What:** DEC-5. The connection neuron, the state machine, the closed health union, the consent
flow, and the provider-adapter contract.

**Why:** without it, every integration module reimplements OAuth. `ino`'s named leak points are
what the alternative produces: a hardcoded `KnownProviders` list, a hardcoded redirect
allowlist, a display-name switch, and provider strings hardcoded in the Flutter client — four
places to edit to add one connector.

**Breaks:** nothing existing. New subsystem.

**Proves it:**
- an unauthorized connection reports `NotAuthorized`, not `false`;
- a missing app credential reports `MissingAppCredentials` and the UI renders an
  administrator-facing action distinct from a sign-in action;
- token expiry emits a synapse that an observing client receives (depends on B-1);
- revocation is durable and survives silo restart;
- a second provider adapter (Salesforce) passes the same conformance suite as the first.

### B-4 — The AI module

**What:** `Brain.Modules.Ai.Contracts` / `.Ai` / `.Ai.Testing`, per-provider adapters, and the
typed model neurons of DEC-6.

**Why:** it is the replacement for D-1 and, more importantly, **it is the proof that the module
system works.** If AI cannot be expressed as an ordinary module, B-2 is wrong.

**Sources:** `digitalbrain`'s `LlmModel` descriptor with derived service key, its uniform
`ChatClientBuilder` middleware chain (logging, streaming usage, OpenTelemetry with GenAI
semantic conventions), and its single-flag local-model override — one configuration value
rerouting every model to a local provider with no call-site change. That flag is the correct
offline story and nothing in v2 has an equivalent.

**Explicitly not copied:** `NeuronCapability`. It is a `[Flags]` enum fusing three unrelated
axes — cost/latency (`Fast`, `Balanced`, `Reasoning`), modality (`Voice`, `Embedding`), and
structure (`Storage`, `External`, `Generated`). `Fast | Balanced` is representable and
meaningless. It is written on ~40 neurons in that tree and **read zero times** — no `HasFlag`,
no bitwise test, no routing decision, repo-wide. Adopting it replaces one bad abstraction with a
laxer one. If a capability concept enters this framework it arrives with a consumer on day one,
or it rots identically.

**Breaks:** everything in D-1's blast radius. They land together.

**Proves it:**
- the kernel's referenced-assembly test (D-1) passes;
- a neuron that does no inference has no transitive vendor dependency;
- an inference call appears on the timeline without the caller instrumenting it (depends on
  B-1);
- the deterministic model fails loudly on an unscripted prompt;
- swapping provider is a configuration change with no call-site edit.

### B-5 — The Flutter module

**Reduced 2026-07-19 — see DEC-7 as amended.** A UI component is a neuron interface in a UI
module's `.Contracts`, resolved by `Get<ICalendar>()` like any other capability. The component
catalog as a distinct registry, vocabulary negotiation as a distinct handshake, and the surface
protocol as a distinct wire format are all deleted — they are the generic mechanism, applied.
What remains is a module whose contracts happen to render, plus the drift guard.

**What:** DEC-7. The surface neuron, the component catalog and vocabulary negotiation, and the
Dart package contributed alongside the C# module.

**Why:** the UI ask, and the second proof that modules can contribute more than grains.

**Sources:** `digitalbrain`'s surface-as-neuron (`RfwCard : Synapse`, and a `Flutter` neuron
addressable by any other neuron) and its shell-attach hook that made the presence of a running
Flutter surface a cluster-observable event; `brain_from_master`'s client runtime — the
reconnect/cursor/generation state machine, its forbidden-payload-key guard that prevents a
surface payload carrying a token, and its bounded-depth JSON validation.

**Explicitly not copied:** the RFW interpreter (DEC-7), and `flutter_bloc`, which appears in
both prototypes' pubspecs with **zero usages** in either `lib/` tree. `ChangeNotifier` plus
`InheritedNotifier` was the actual choice in both and was the right one.

**Depends on:** B-1 (a UI that cannot observe cannot react), B-2.

**Proves it:**
- an external MCP-style caller mutates a neuron and the Flutter surface updates with no polling;
- a component the client does not ship produces a developer-visible error, not a blank space;
- toggling, typing, and hovering produce no network traffic;
- the cluster can enumerate what surfaces are attached and what vocabulary each declares;
- a module contributes a component and it renders without kernel changes.

### B-6 — The governance ledger

**Promoted 2026-07-19 into Phase 4, from Phase 5.** This item was challenged as *"a table with no
rows"* — self-evolution named as the product with nothing performing it. That challenge was correct
when it was made and is no longer. **Under DEC-8 every behavior install is a row**, and the
approval gate is the only thing standing between a self-modifying script and an unattended loop at
machine speed. B-6 is not a phase after the scripting rail; it is that rail's safety property and
ships with it.

**What:** DEC-1a. An append-only sink for proposals, approvals, behavior installs, module installs,
rollbacks, and connection grants.

**Why:** `CLAUDE.md` calls self-evolution "the product" and requires mutations to be durable,
replayable, and rollback-capable. DEC-1 removes that property from the neuron journal, so it is
provided here, deliberately, for the small set of events that actually need it. Under DEC-8 the
script *is* the behavior's state, so this requirement is satisfied literally: an approval is a
journal entry and a rollback is reverting one.

**The approval payload is derived, not declared.** DEC-8's recording pass runs the script once with
`On<T>` and `Get<T>` recording instead of acting; its output — handled facts, requested
capabilities, emitted facts — is what a human approves. A script cannot hold a capability it did
not ask for, because asking is how it gets recorded.

**Sources:** `final`'s propose/approve handshake — a closed `IsPrivilegedAction` allowlist gating
a two-synapse exchange, entirely inside normal handler dispatch, with no separate approval
service. That shape is right.

**Explicitly not copied:** `final`'s grant enforcement, which is a hardcoded `return false` with
a comment admitting it is a stub, and which its own audit found evaluating after the privileged
action had fired.

**Proves it:** an approval sequence is fully reconstructable after restart; a rollback returns
the system to a prior state and the ledger records both the change and its reversal.

---

## 9. Ordered execution plan

Each phase ends green on the root gate. Phases are ordered by dependency, not by value.

### Phase 0 — Hygiene. No decisions required, no architecture touched.

| Step | Action | Proof |
|---|---|---|
| 0.1 | **Commit the 20 untracked source files under `sources/`**, especially `digitalbrain-app/packages/digital_brain_sdk_flutter/` (13 files) | `git ls-files --others --exclude-standard --ignored sources` shows no `.cs`/`.dart`/`.md`/`.proto` outside build output |
| 0.2 | Delete all `bin`/`obj`/`.vs` under `sources/` | `sources/` drops from 1.6 GB to roughly 370 MB |
| 0.3 | Rename every nested `.claude/`, `.agents/`, `.github/skills/` under `sources/` to `.disabled-*` | A fresh session reading under `sources/` surfaces no path-scoped skills |
| 0.4 | Delete `kernel/`, `modules/`, `integrations/`, `hosts/Brain.Kernel.Host/`, `hosts/DigitalBrain.ServiceDefaults/`, `.superpowers/` (D-5) | Root suite green |
| 0.5 | Add `.mcp.json` and the sentinel-guarded auto-init target (R-7) | The Pre-Change Ritual is executable from a clean clone |
| 0.6 | Mark `sources/Projects/CLAUDE.md` and `CONTINUATION.md` archival | They no longer read as live instructions |

0.1 gates everything in §10. Nothing is deleted from `sources/` before it.

### Phase 1 — Truth. Make the tests assert what the code does.

| Step | Action | Proof |
|---|---|---|
| 1.1 | Leaf-assembly reflection tests, one per package boundary (R-5) | The `DigitalBrain.Kernel` → vendor SDK test **fails**. This is the gate for Phase 3 |
| 1.2 | A dedupe cost test asserting constant cost per delivery | **Fails.** Gate for Phase 2 |
| 1.3 | A scenario proving one unreachable receiver blocks all outgoing traffic (R-2) | **Fails**, documenting §2.5 |
| 1.4 | A scenario proving a never-activated neuron misses a broadcast (R-4) | **Fails**, documenting §2.7 |
| 1.5 | Correct `website/status.md` and `CHANGELOG.md` against §2 | Claims match verified behaviour |

Phase 1 writes failing tests for defects that exist. Nothing is fixed. This is the phase that
makes the rest verifiable, and skipping it is how a plan becomes a narrative.

### Phase 2 — The feed and the boundary. Split at 2.4, green at both ends.

**Re-ordered 2026-07-19.** R-3 is promoted into this phase. §14.2 filed the owner boundary as a
mitigation to be scheduled later; DEC-8 makes it the gate on the entire scripting rail, because an
external script *is* the unattributed Orleans client of §2.6 — the configuration this lineage
records as its failure mode. Nothing client-facing ships before it.

**Phase 2a — the feed.** Ends green; this is the rollback point §14.4 asked for.

| Step | Action |
|---|---|
| 2.1 | Feed state machine: snapshot, bounded delta log, cursor, invariant validation (R-1) — *done* |
| 2.2 | Bounded dedupe set replacing the journal scan — turns 1.2 green — *done* |
| 2.3 | Cursor-based read replacing `ReadJournalAsync` across every consumer — *done* |
| 2.4 | DEC-10 — synapse becomes a thin record; metadata moves to the delivery envelope. Absorbs D-3 — *done* |

**Phase 2b — the boundary and the rail.**

| Step | Action |
|---|---|
| 2.5 | **R-3 — the owner boundary.** Blocks everything client-facing. `OwnerOf` must not fall through for unattributed callers |
| 2.6 | `WatchAsync` on `INeuron`, and `FeedNeuron` — §4.1's collapse. Deletes both polling loops |
| 2.7 | Call-filter reification (B-1 layer 1) — the bridge that puts typed requests on the rail |
| 2.8 | R-4 and broadcast addressing as **one** change: composition-time type-level registration, instance minted per DEC-6's correlation keying. Turns 1.4 green |
| 2.9 | R-2 — per-receiver outbox progress. **State the ordering guarantee first**; no prior generation documents one. Turns 1.3 green |
| 2.10 | D-6 — remove the sample's activation workaround and polling loop |

Ends with: a typed request appearing on the feed with no cooperation from the caller, no
`Task.Delay` in any wait path, and an owner boundary that holds against a client.

### Phase 3 — Modules. B-2, then B-4, which proves B-2.

| Step | Action |
|---|---|
| 3.1 | `IModule`, descriptor, `AddModule<T>()`, composition-time validation. Descriptor carries no wiring (§4.2) |
| 3.2 | **Two**-package template with the contracts guard test baked in (DEC-3 as amended) |
| 3.3 | R-6 — generate the contract manifest from the interfaces. This is what makes 3.1's descriptor derivable rather than hand-written |
| 3.4 | B-3 — connections, health union, provider-adapter contract. Unblocked by 2.5 |
| 3.5 | B-4 — the AI module, model neurons keyed by correlation (DEC-6 as resolved). **Turns 1.1 green.** D-1 lands here as its consequence |
| 3.6 | R-5 — re-justify or delete the metapackage now that the kernel is clean |
| 3.7 | A second provider adapter against the same template — the real proof of 3.1–3.3, and the first thing `.Testing` could conform. DEC-4 returns here or not at all |

### Phase 4 — The scripting rail. DEC-8, and B-6 as its gate.

**Moved ahead of UI and merged with governance.** B-6 was scheduled at Phase 5 and NEXT.md
challenged it as *"a table with no rows"*. Under DEC-8 every behavior install is a row, and the
approval gate is the only thing standing between a self-modifying script and an unattended loop.
It is not a separate phase; it is this one's safety property.

| Step | Action |
|---|---|
| 4.1 | The client API surface: `Connect`, `Get<T>`, `Get<T>(name)`, `On<T>`, `Emit` |
| 4.2 | Roslyn compilation of a script against a `.Contracts` reference set |
| 4.3 | `IBehavior : INeuron` — one grain type, script as durable state, top level as activation path |
| 4.4 | The recording pass — `On<T>` and `Get<T>` record instead of act; output is the approval manifest |
| 4.5 | B-6 — the governance ledger. Propose → approve → install, journaled and reversible |
| 4.6 | Capability enforcement at `Get<T>()`, for behaviors only, with the script/compiled boundary stated |
| 4.7 | External hosting mode — the same script as a cluster client. Gated on 2.5 |
| 4.8 | **The generation benchmark.** A pre-committed numeric threshold, run against a real model, demotion enforced against the codebase. §11's standard, applied with the correction in §10 |

### Phase 5 — UI. B-5, reduced.

Under DEC-7 as amended this is a module whose contracts happen to render — not a catalog, not a
negotiation, not a surface protocol. Depends on Phase 4: the reason a UI exists here is that a
behavior resolves `Get<ICalendar>()` and asks a human something.

| Step | Action |
|---|---|
| 5.1 | Durable request/response for unbounded waits — the human-input case (DEC-9) |
| 5.2 | UI module `.Contracts` with component interfaces |
| 5.3 | The Dart implementation, plus the drift guard failing the build when it does not implement the contract |

### Phase 6 — Cleanup.

D-2, D-4, R-7, and §7's surviving renames. Deliberately last: they are the cheapest items and the
most tempting to start with, and starting with them produces motion without progress. D-3 and the
`Stamped` rename are gone from this phase — DEC-10 absorbed both into 2.4.

---

## 10. SOURCES — RETIRED, AND WHAT THE HARVEST FOUND

**Closed 2026-07-19. `sources/` is deleted from the working tree.**

This section was a ten-row retirement ledger with rows *"expected to remain open for months."*
It rested on a premise that Phase 0.1 destroyed: that deleting `sources/` would destroy
information. It no longer would.

| Checked directly | Result |
|---|---|
| Files under `sources/` in HEAD | **8,471** — committed, not merely staged |
| The Flutter SDK package that gated every row | 15 files, in HEAD |
| `ino/docs` — L-8's "~13 design docs" | 156 files, in HEAD |
| `brain_from_master` markdown — L-10, "retires last" | 41 files, in HEAD |
| Untracked non-`node_modules` files repo-wide | 38 — every one generated (`.feature.cs`, `.razor.css`), user-local (`.csproj.user`, `daemon.pid`), build output (`.nupkg`), or public reference data |
| The two "seed databases" (78 MB + 62 MB) | `locations.json`, `airports.json` — world airport/location reference data. Not artifacts of this lineage. Re-downloadable. 140 MB of the 374 MB |

**Not one untracked hand-written design artifact existed anywhere under `sources/`.** Every row's
"safe to delete when" condition was therefore satisfiable immediately, and the tree is recoverable
in full via `git show <sha>:sources/…` forever.

**The ledger's real defect was its scoring column.** "Valuable content" cannot close a row,
because everything in a 374 MB quarry looks valuable. The replacement rule, used for the harvest
below: **a document earns transcription only if it changes a decision that is currently open.**
Everything else is either already captured in §§1–4 and §12, or dead.

### The harvest, executed once and closed

Three readers scored every design document in `ino`, `digitalbrain`, `brain_from_master`, `v3`,
`final` and `IAW` against the seven then-open decisions. Findings that changed the plan are folded
into the sections they change; recorded here so they cannot be silently reversed.

**Four factual errors in this plan, found by the harvest:**

1. **§11's Gate-0 endorsement was false in both halves.** This document called `final`'s Gate-0
   *"the standard"* — 20 prompts, 80% bar, scored 60%, language formally demoted. The source:
   *"Execution used deterministic stub returning 'first attempt' JSON for the prompt set"*
   (`INOLANG-RFC.md` L232) and *"its 60% is simulated, not measured"* (`UNIFICATION-PLAN.md` L44).
   The demotion also did not hold — *"`RuleHostNeuron` + parser + interpreter + BDD landed
   afterward and are green."* **A method that failed in both halves cannot be cited as a standard
   without its corrections**: the benchmark must run against a real model, and demotion must be
   enforced against the codebase, not the spec. DEC-8 depends on this.
2. **§11's other pillar — ino rejecting multi-tier test ladders — is contested inside ino.** The
   *"Just e2e"* line sits under a heading reading *"The user has explicitly rejected:"* with no
   reasoning, in a file that leaves a tier question *"Decision pending"*, while
   `2026-04-16-ino-poc-phase-2-cross-silo-runtime-design.md` §12.1 specifies a **five-layer ladder
   with per-layer speed budgets**. ino did not settle this.
3. **L-5 mischaracterised two of its three headline artifacts.** `IAW/docs/orleans_scheduling.md`,
   cited as primary research bearing on grain scheduling, is a timers/reminders/durable-jobs
   matrix with nothing on `[Reentrant]` or serialising expensive calls. IAW's `static virtual`
   metadata is `AgentDisplayName`/`AgentDescription`/`AgentInstructions` — **display identity, not
   `IHandle`/`IEmit` wiring.** B-2 listed it as a design input for the wrong reason.
4. **§4.1 cited `TimelineRelayGrain` for something it does not do.** Corrected in place.

**Findings that resolved open decisions:**

- **Broadcast addressing.** ino: startup reflection populates a Discovery registry with handler
  **types**; the grain key comes from the ambient correlation id. `digitalbrain`'s
  `NavigatorRouter.ResolveSubscribersAsync` independently confirms catalog-driven enumeration by
  handled type. Both remove the *activation* prerequisite, which is §2.7's actual bug. → Phase 2.8.
- **§14.6, descriptor vs interface.** `brain_from_master`'s `ModuleDescriptor` declares **no
  neurons and no synapse types**; `digitalbrain`'s v5 domain manifest is the same shape; ino states
  *"No manifest file. No custom descriptor."* → §4.2.
- **§14.1, DEC-6's concurrency hole.** ino shipped the raw-injected-client alternative
  (`TripPlanner(IChatClient)`), and `digitalbrain`'s `MULTI_AGENT_LOCAL_LLM.md` observes that with
  a shared inference server the serialisation point is the provider, not the activation — so
  fan-out across caller grains buys no parallelism. → DEC-6, resolved.
- **§14.3, feed-worthy vs traced.** ino asked the same question and left it open, with two useful
  artifacts: the self-grain-versus-cross-grain axis, and measured caps (1 s emit timeout,
  4096-byte payload) with the note *"the per-call overhead is real."* → §4.1.
- **§14.2, credentials.** Three shapes exist beyond this plan's binary framing: tokens in a
  non-neuron grain reached through an in-process broker, encrypted at rest with an OS keystore
  (`digitalbrain`); tokens outside the cluster entirely in a per-brain DPAPI-encrypted file (its
  own later v5 generation — **a recorded reversal**); and in-cluster with a per-experience consent
  list (ino). None closes §2.6. → R-3, promoted to Phase 2.5.

**Named artifacts that bear on nothing open**, contradicting §10's own "valuable content" column:
ino's decay model (never implemented — *"(implementation pending)"*), the graph-DB rejection
(scoped to visualization cost), UI-patch-as-synapse (a draft whose argument is circular),
`digitalbrain`'s ABI two-interlocking-guards pattern (a good technique for Phase 1.1's leaf tests;
changes no decision), and `brain_from_master`'s failure-behaviour matrix (eleven rows, none on
ordering, eviction, or fan-out).

**Negative findings, stated because silence is not rejection:**

- **Delivery ordering (R-2) has no prior art.** No document in any tree states an ordering
  guarantee or addresses an unreachable receiver. `final` forbids asserting order in three separate
  documents — *"flaky by design"*. §11's instruction to state the guarantee first stands entirely
  unassisted, which is why Phase 2.9 says so explicitly.
- **Nothing in the lineage built per-neuron dynamic subscription.** Everything built type-level
  static enumeration. DEC-8's install-time registry for behaviors is new ground.
- **`final`'s `VISION.md` treats total observability as an axiom** — *"No side channels."* That is
  why no generation questioned wrapper-synapse volume: §14.3 is a question the lineage's stated law
  forbade asking.

### Reversibility

`sources/` is in git history at every commit up to and including the one that removes it. Use
`git log --diff-filter=D -- sources/` to find the removal commit and `git show <sha>^:<path>` to
read any file. The tree is not gone; it is no longer on disk, in every grep, and in every
architecture pass.

---

## 11. Deliberately left open

Recorded so the next plan cannot mistake silence for a decision.

**The three-tier test split.** v2 runs 88 Tier-0 contract tests, 26 Tier-1 Reqnroll simulations
on a shared 3-silo in-process cluster, and 5 Tier-2 Aspire hosted tests.

**Corrected 2026-07-19 — this row previously cited `ino` as having settled the question. It did
not.** The *"Multi-tier test ladders (Tier 0/1/1.5/2/3). Just e2e."* line sits under a heading
reading *"The user has explicitly rejected:"*, carries no reasoning, and appears in a file that
leaves a tier question *"Decision pending."* Two months earlier,
`2026-04-16-ino-poc-phase-2-cross-silo-runtime-design.md` §12.1 specified a **five-layer ladder
with a numeric speed budget per layer** (L1 <5 s, L2 <30 s, L3 <60 s, L5 ~3 min, suite <5 min).
`ino` contradicts itself; it is not a precedent either way.

What survives as argument: `IAW`'s middle tier was functionally indistinguishable from its first.
`v3` collapsed four overlapping drivers into one `Simulation` class on the principle that *"the
test framework and the safety gate are the same machine"* — and its motive is the one that
matters here: *"An AI-authored neuron has no human to hand-write its xUnit."* Under DEC-8 that is
no longer hypothetical, so **the gate must be machine-runnable**, which is a real constraint on
whether a Gherkin tier can host it.

Two better discriminators than "Gherkin", both from the harvest:

- **A numeric speed budget per tier** (ino). A tier that cannot state its budget is not a tier.
- **What is real** (`digitalbrain`): mocked model → real model → real everything. A membership
  criterion about fidelity, not about syntax.

Tier-0 and Tier-2 clearly earn their place. **Tier-1 still needs a stated reason.** Decide before
Phase 3.7, where the second adapter's conformance lands.

**Dispatch mechanism (R-6).** Promote the generated manifest or delete it. Depends on
measurement. **Narrowed:** the generator is no longer decorative regardless of the outcome —
§4.2 makes the contract manifest the thing a module descriptor's wiring half is generated from,
so the generator becomes load-bearing at Phase 3.3 even if reflection keeps the dispatch path.

**Delivery ordering (R-2).** The code currently promises FIFO by construction; nothing documents
it. Fixing head-of-line blocking changes an unstated guarantee. State the guarantee first.
**Confirmed by the harvest: no prior generation states one either.** `final` forbids asserting
order in three separate documents (*"flaky by design"*); `brain_from_master`'s outbox is
single-destination and never met the problem. There is nothing to inherit. Two useful fragments:
ino's vocabulary for the promise it did make elsewhere — *"best-effort ordering within a target,
at-least-once, idempotency is the handler's responsibility"* — and its split by verb, where a
broadcast returns `reached_count`/`failed_count`/`failed_grain_types` and *"one listener's failure
doesn't fail the broadcast."* **A directed request and an undirected fact cannot share an ordering
guarantee**, which DEC-9 already separates.

**~~`IEmit<T>`'s future.~~ Resolved.** §4.2: the descriptor does not restate the wiring graph, so
`IEmit<T>` is not redundant with it — it is the canonical declaration the wiring half is generated
*from*. Confirmed independently by `brain_from_master` (`ModuleDescriptor` declares no neurons and
no synapse types), `digitalbrain`'s v5 domain manifest (same shape), and ino (*"No manifest file.
No custom descriptor."*).

**Whether any DSL is ever proposed again.** **Corrected 2026-07-19 — this row endorsed a method
that failed.** It claimed `final` *"ran 20 prompts against an 80% bar, scored 60%, and formally
demoted the language."* Both halves are contradicted by `final`'s own documents: *"Execution used
deterministic stub returning 'first attempt' JSON for the prompt set"* and *"its 60% is simulated,
not measured"* — a benchmark whose failure rate was authored, not observed — and the demotion did
not hold, because *"`RuleHostNeuron` + parser + interpreter + BDD landed afterward and are green."*

The method is still the right idea and is **the standard for DEC-8's generation benchmark**, but
only with the two corrections its own failure exposes:

1. **The benchmark runs against the real model, or it does not count.** A stub cannot kill anything.
2. **Demotion is enforced against the codebase, not the format spec.** A gate that fires while the
   machinery ships anyway is theatre with a number attached.

That lineage attempted a full interpreted language three times and abandoned it three times. DEC-8
is deliberately not a fourth attempt — it is C# compiled by Roslyn, so the type checker is the
gate and there is no interpreter to demote.

**Runtime-created neurons.** Deferred by DEC-2, not rejected. Revisit only with a resident
isolated host design. Nothing in this lineage has built one — `v3`'s collectible
`AssemblyLoadContext` unloaded after a single test run and never hosted anything resident.

---

## 12. What this plan rejects, and why

Recorded in one place so it cannot be silently reversed.

| Rejected | Reason |
|---|---|
| `NeuronCapability` flags enum | Fuses three unrelated axes; `Fast \| Balanced` is representable and meaningless; written ~40 times and read zero times in its own tree |
| Hierarchical `NeuronId` as a routing key | Never routed anywhere; `digitalbrain` has it and still infers domain from the CLR namespace with a hardcoded special case; three incompatible conventions coexist in one field |
| Generic UI renderer / RFW | Built, shipped, and being deleted by the newest generation, with a written rule against interpreters |
| Runtime module install | Orleans manifest and Aspire topology are both frozen at startup; every attempt produced a second-class neuron kind |
| Module sandboxing | Compile-time modules run in-process; gating is theatre and the prior art documents its own escape hatches |
| Inference as a synapse hop | Built in `digitalbrain`, invoked zero times; splits handler logic across two methods |
| Unbounded journals | Quadratic dedupe cost for a replay capability nothing uses |
| Bounded journals without a snapshot | Silently destroys the only data v2's client API can read |
| Assembly-scanning module discovery | `IAW` ended with three independent scans and three swallow-and-continue handlers |
| `.brain` archives, signing, quarantine worlds | Consequences of runtime install, which DEC-2 rejects |
| `flutter_bloc` | Present in both prototypes' pubspecs with zero usages in either |
| Server-side polling behind a streaming API | `brain_from_master` does this at 250 ms per client; it is not an observation primitive |

---

## 13. First action

**Phase 0 is complete and `sources/` is retired (§10).** Phase 2a (steps 2.1 through 2.4) is
committed and is the green rollback point.

The next action is **2.5, R-3, the owner boundary.** It is the gate on everything client-facing, and under
DEC-8 that means it is the gate on the product. Nothing in Phase 4 may start before it.

**Standing gate, every phase, no exceptions:** `dotnet test --logger "console;verbosity=minimal"`
from the root. Never `--filter`. Held-red proofs assert the behaviour the system *should* have and
are excluded, never left failing — `[Fact(Explicit = true)]` for xUnit, `@ignore` for Gherkin.

---

## 14. Known defects in this plan

This document was reviewed adversarially after being written. Six problems survived. **All six are
now closed** — five by the decisions of 2026-07-19, one by the harvest. Each is kept with its
original statement and its resolution, because a defect deleted without a recorded answer is a
defect that returns.

### 14.1 — CLOSED. DEC-6's concurrency hole.

**Was:** Orleans grains are single-threaded per activation, so `IGpt5` at a stable key serialises
every inference call per owner. All three escapes were judged bad — `[Reentrant]` breaks the
journal invariant, key-per-call makes the journal meaningless, key-per-caller fragments the
model's traffic into N feeds.

**Resolved.** Every rejection assumed observability comes from the **callee's** journal. Under
B-1 the call is recorded on the **caller's** feed, so the callee's journal is not the evidence.
Keying the model neuron by the ambient correlation id therefore costs nothing it was accused of
costing: each call gets its own activation, nothing serialises across callers, `[Reentrant]` is
never needed, and one model's traffic is a query over the timeline rather than a property of one
grain's journal. See DEC-6.

Supporting evidence from the harvest: ino shipped the raw-injected-client alternative
(`TripPlanner(IChatClient)`), and `digitalbrain` observed that with a shared inference server the
serialisation point is **the provider, not the activation** — so fanning out across caller grains
buys no parallelism against a single local model anyway.

### 14.2 — CLOSED by ordering. Credential storage was scheduled ahead of its boundary.

**Was:** DEC-5 places OAuth refresh tokens in cluster-addressable grains while §2.6 establishes
that an unattributed caller — any Orleans client — passes the owner filter unchecked, with R-3's
own proof admitted as *"insufficient on its own."*

**Resolved by promotion, not by mitigation.** R-3 moves to **Phase 2.5**, ahead of everything
client-facing. This is no longer only a credential concern: DEC-8 makes an external script exactly
the unattributed Orleans client of §2.6 — the configuration this lineage records as its failure
mode (IAW's raw unauthenticated `IClusterClient` with full host authority). The boundary now gates
the product direction, not just B-3.

The harvest found three storage shapes beyond this plan's binary framing, recorded in §10 and none
of which closes §2.6 on its own: encrypted-at-rest in a non-neuron grain behind an in-process
broker; outside the cluster entirely in a per-brain DPAPI-encrypted file; and in-cluster with a
per-experience consent list. Encryption at rest narrows the blast radius from "all tokens" to
"ciphertext plus a consent check" and is worth having **in addition to** the boundary.

### 14.3 — CLOSED. DEC-1 and B-1 interact badly.

**Was:** reification writes every grain call to a bounded feed, so a neuron making ten model calls
in a turn evicts its own domain events. The bound protects storage and CPU, not signal-to-noise,
and nothing stated what is feed-worthy.

**Resolved, and the code had already answered half of it.** `SynapseObserver` observes via
`ActivityListener` over `SynapseTelemetry` — push-based, no polling. Everything is *already*
traced. The question was never "traced or not"; it is what additionally deserves to be durable:

> **Domain facts are feed-worthy. Call traffic is traced and reified, not accumulated.**

The structural half is stronger than the rule. Because compiled subscription is composition-time
(§4.5), the kernel knows at startup whether anything handles a fact type and **never constructs
one nothing handles.** A dynamic registry could not do this. That is what makes "everything is a
fact" affordable rather than ruinous.

Two discriminators carried over from ino, which asked this and left it open: the self-grain versus
cross-grain call axis, and hard caps — it used a 1 s emit timeout and a 4096-byte payload cap with
the note *"the per-call overhead is real. Profile before scaling neuron counts up."*

### 14.4 — CLOSED. Phase 2 was too large and had no rollback point.

**Was:** R-1 changes `ReadJournalAsync`, the entire client read API, and §8 sized it in one line.

**Resolved by splitting**, as the defect demanded: **Phase 2a** (feed, dedupe, cursor read, DEC-10)
ends green and is the rollback point; **Phase 2b** (boundary, watch, reification, broadcast,
ordering) follows.

**The blast radius was also overstated.** It is not 26 scenarios rewritten. Every scenario reaches
the journal through a shared step layer — `NeuronSteps` has 5 call sites and `Simulation` has 6 —
so the real edit surface is roughly 20 call sites across the Testing package, two samples,
`ProbeHost`, and `BrainClient`. Verified by direct search.

### 14.5 — CLOSED. Two unanswered questions.

- **Is `LLMNeuron` the right name?** Moot. There is no `LLMNeuron`. Under DEC-6 a model is a
  typed neuron interface named for the model (`IGpt56`), resolved by `Get<T>()` like any other
  capability, and §7's reduced table carries no row for it.
- **Does the owner concept belong in the kernel?** **Yes, and DEC-8 is the argument the plan
  previously assumed.** `Get<T>()` takes no owner — owner is ambient and never a parameter — which
  is what makes "a script cannot address another owner's neuron" a property of the API's shape
  rather than a check that can be forgotten. An owner concept outside the kernel could not do
  that. `brain_from_master`'s finding that the feed must be keyed by identity rather than
  transport points the same way.

### 14.6 — CLOSED. DEC-3 and §4.2 were in tension.

**Was:** DEC-3 puts `IHandle<T>`/`IEmit<T>` on the interface so the wiring graph is reflectable
without loading the implementation; §4.2 then said discovery is explicit registration. If the
descriptor declares the neurons anyway, the interface declaration is redundant.

**Resolved: the descriptor never declares them.** The tension was invented by listing neurons in
the descriptor. Three prior generations independently did not — `brain_from_master`'s
`ModuleDescriptor` carries configuration, secrets, capabilities, effects and OAuth and **no
neurons or synapse types**; `digitalbrain`'s v5 domain manifest is the same shape; ino states
*"No manifest file. No custom descriptor."*

So wiring stays on the interfaces and is **generated** from them (R-6, Phase 3.3), while the
descriptor carries only what interfaces cannot express. There is no second declaration, therefore
nothing to drift. This also closes §11's `IEmit<T>` question.
