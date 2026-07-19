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

1. **The retirement ledger (§9) is a living artifact, not a one-time sweep.** A row closes only
   on evidence. "Rejected" is a legitimate outcome but must be written down; silence is not
   rejection.
2. **This plan records what it rejects and why**, in §10, so the next plan cannot discard it
   silently.

There is also a loose thread this plan cannot close. `sources/Projects/docs/*` describes a
codebase containing `DigitalBrain.Core`, `DigitalBrain.Silo`, `IPackBehavior`,
`PackAlcEmbodier`, and `GeneratedNeuron`. None of those identifiers exist anywhere on disk —
not in `sources/`, not in the working tree. **At least one generation between `Projects/` and
today is not in this repository.** Ledger row L-0 covers it.

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
that neuron until the retry horizon expires — `SynapseDelivery.RetryHorizon` is 30 minutes.
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

### DEC-3 — A module ships as three packages

```
Brain.Modules.<Name>.Contracts   leaf; zero dependencies; synapse records + neuron interfaces
Brain.Modules.<Name>             grains, vendor SDKs, provider adapters
Brain.Modules.<Name>.Testing     consumer fakes + implementor conformance suite
```

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
still not an observation primitive. The correct source is `digitalbrain`'s pattern: an
unconditional write to a global timeline stream on every emission, plus a relay grain with an
implicit stream subscription that requires no registration.

**Take `brain_from_master`'s client contract and `digitalbrain`'s backend.** The client
contract is already written and has 36 test files against a fake transport; it works unchanged
the day the server stops polling.

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

A module descriptor declares:

- **Neurons** — grain types it contributes, with their handled and emitted synapse types.
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

| Current | Problem | Proposed |
|---|---|---|
| `IEmit<T>` | An empty marker interface whose only consumer is a source generator. It reads like a capability and is a declaration. | Keep the name; document it as a declaration, or fold it into the module descriptor (§4.2) where declarations belong. |
| `Synapse.Stamped` | A property that throws when unset. Reads like a boolean or a past-tense flag; is actually "metadata, or explode". | `RequireMetadata()` — a method, so the throw is expected at the call site. |
| `SynapseWiring` / `DispatchManifest` | Two names for the same knowledge, in the same file, neither of which says "what handles what". | One name after R-6 resolves which mechanism survives. |
| `SimulationCluster` | A static class holding a shared cluster and a static model table. The name says "a cluster"; it is a process-wide singleton with static mutable state. | `SharedSimulationHost`, and the model table leaves with D-1. |
| `ScriptedModel` | Names the mechanism, not the role. | `DeterministicModel` — it exists so an unscripted prompt fails loudly rather than inventing an answer. |
| `BrainService` / `BrainClientService` (Aspire.Hosting) | `BrainClientService` is not a service; it is a projection of `BrainService` for referencing consumers. | `BrainResource` / `BrainClientReference`. |
| `hosts/DigitalBrain.ProbeHost` | "Probe" describes why it was built, not what it is: an in-cluster host used because an external client cannot complete a handshake through the Aspire proxy. | Keep, and document the reason inline in the plan rather than in the name. |
| `JournalKind.Incoming` / `Outgoing` | Survives R-1 only if both still exist after the feed redesign. Re-examine then. | Deferred to R-1. |

---

## 8. BUILD NEW

### B-1 — The observation primitive

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

**What:** `IModule`, the module descriptor, `AddModule<T>()`, the three-package template, the
conformance harness, and the composition-time validation in §4.2.

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

**What:** DEC-1a. An append-only sink for proposals, approvals, module installs, rollbacks, and
connection grants.

**Why:** `CLAUDE.md` calls self-evolution "the product" and requires mutations to be durable,
replayable, and rollback-capable. DEC-1 removes that property from the neuron journal, so it is
provided here, deliberately, for the small set of events that actually need it.

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

### Phase 2 — The feed. R-1, then B-1.

| Step | Action |
|---|---|
| 2.1 | Feed state machine: snapshot, bounded delta log, cursor, invariant validation (R-1) |
| 2.2 | Bounded dedupe set replacing the journal scan — turns 1.2 green |
| 2.3 | Cursor-based read replacing `ReadJournalAsync` across every consumer |
| 2.4 | Call-filter reification (B-1 layer 1) |
| 2.5 | Wire protocol: snapshot / read-since / watch, with reset (B-1 layer 3) |
| 2.6 | `BrainClient` gains observation |
| 2.7 | R-2 and R-4 — turns 1.3 and 1.4 green |
| 2.8 | D-6 — remove the sample's activation workaround and polling loop |

Ends with: an external caller mutating a neuron by direct grain call, and an observing client
seeing it, with no polling anywhere and no cooperation from the caller.

### Phase 3 — Modules. B-2, then B-4, which proves B-2.

| Step | Action |
|---|---|
| 3.1 | `IModule`, descriptor, `AddModule<T>()`, composition-time validation |
| 3.2 | Three-package template with both guard tests baked in |
| 3.3 | Conformance harness in `.Testing` |
| 3.4 | B-3 — connections, health union, provider-adapter contract |
| 3.5 | B-4 — the AI module. **Turns 1.1 green.** D-1 lands here as its consequence |
| 3.6 | R-5 — re-justify or delete the metapackage now that the kernel is clean |
| 3.7 | A second module against the same template — the real proof of 3.1–3.3 |

### Phase 4 — UI. B-5.

Depends on Phase 2 for observation and Phase 3 for module contribution. Nothing in B-5 can
start before both.

### Phase 5 — Governance. B-6.

### Phase 6 — Cleanup.

D-2, D-3, D-4, R-6, and §7's renames. Deliberately last: they are the cheapest items and the
most tempting to start with, and starting with them produces motion without progress.

---

## 10. SOURCES RETIREMENT LEDGER

`sources/` is dead weight that slows every search and every architecture pass. It is retired
logically and incrementally, never wholesale.

**A row closes only when every item in its "valuable content" column has either landed in
DigitalBrain covered by a passing test, or been rejected in writing with a reason. Silence is
not rejection.**

**Reversibility, corrected.** Git history preserves what git tracks. §2.14 establishes that 20
source files under `sources/` are **not tracked**. Step 0.1 fixes this and is a prerequisite for
every row below. Until 0.1 is done, no row may close.

**A row that stays open for a long time is a correct outcome.** L-8 and L-9 are expected to
remain open for months.

| # | Directory | Valuable content | Where it must land | Proof it landed | Safe to delete when |
|---|---|---|---|---|---|
| **L-0** | *(absent generation)* | `DigitalBrain.Core`, `DigitalBrain.Silo`, `IPackBehavior`, `PackAlcEmbodier`, `GeneratedNeuron` — cited by `Projects/docs`, present nowhere on disk | Located, or declared lost in writing | A written finding: recovered, or lost with the reason it cannot be recovered | N/A — this row must be answered before L-4 closes |
| **L-1** | `self-improving` (79 C#, 79 md) | `[LLM<TModel>]` attribute injection — nothing else. `IDigitalBrain` is an admitted stub whose `GetFullJournalAsync` returns empty unconditionally; docs byte-identical to `final`'s | Nowhere. Rejected: the attribute form was superseded in `final` by hosting-layer selection, recorded in `final/docs/DELETED.md`; DEC-6 supersedes both | This ledger row, as the written rejection | **After Phase 0.1.** No migration required |
| **L-2** | `v4` (54 C#) | `BundleId`/`BundleVersion`/`IBundleSource`/`IBundleInstaller` type shapes; install-as-observable-event | B-2's descriptor design. Types only — the tree does not compile; it references a `DigitalBrain.Core` that exists nowhere; `Sdk`, `Sdk.Testing`, and `Ino` contain zero `.cs`; the consumer sample's entire demonstration is `_ = new BundleId("demo-from-new-structure");` | B-2 descriptor exists and a duplicate-id test fails startup naming both contributors — explicitly **not** `v4`'s silent `.First()` | After Phase 3.1 |
| **L-3** | `v3` (38 C#) | `IHandle`/`IEmit` **on the interface** so the graph is reflectable without loading the impl; capsule triplet shape; routing-as-metadata argument; the one-`Simulation`-class `Fire`/`Expect`/`ExpectNone` collapse | DEC-3 (contracts leaf), D-3's caveat (routing argument preserved in writing), §11's test-tier question | DEC-3 landed with the contracts guard test green; D-3's caveat present in this document | After Phase 3.2 |
| **L-4** | `Projects/docs` (7 md) | The prior 7-way harvest matrix; the migration assessment; the "do not let it become a second architecture" conclusion; the harvest map that `GOAL.md` contradicted | §1 of this document, verbatim in substance | §1 exists and L-0 is answered | After L-0 is answered. **Not before** — this is the paper trail that explains why the rest is being deleted |
| **L-5** | `IAW` (339 C#, 119 md) | `static virtual` interface metadata; registry-grain shape; DevUI wiring (complete and working); `ToolApprovalMiddleware`; architecture-guard tests; the `Aspire`/`Aspire.Hosting` split validation; `orleans_scheduling.md` and `durable-tasks-research-for-iaw.md` (primary research) | B-2 (metadata + registry); R-5 (split validated); Phase 1.1 (guard tests); the two research docs copied into this repo's docs | B-2 conformance green; guard tests present; research docs present. DevUI: wired, **or** rejected in writing | After Phase 3.7 |
| **L-6** | `final` (109 C#, 106 md) | `.brain` capsule format; **contract-only bundles**; the four-rung trust ladder; **leaf-assembly architecture tests**; `SynapseIncoming`/`SynapseOutgoing`/`Activated`/`Deactivated` as first-class synapses; the InoLang Gate-0 method (a 20-prompt benchmark with a pre-committed numeric kill threshold, executed, 12/20 against an 80% bar); the propose/approve handshake; `DISTRIBUTION.md`; `multirepo-distribution-design.md` | DEC-3 (contract-only → contracts package); Phase 1.1 (leaf tests); B-1 (lifecycle as observable); B-6 (handshake); Gate-0 method recorded in §11 as the standard for any future DSL proposal | Leaf tests green; B-1 emits lifecycle events; B-6 proof green; Gate-0 method recorded. **Explicit rejections required in writing:** grant enforcement (stub returning `false`, checked after the fact), name-prefix bundle activation (never real registration), the documented fake-green test infrastructure | After Phase 5 |
| **L-7** | `digitalbrain-app` + `brain_from_master/app` (318 Dart) | `RuntimeController` reconnect/cursor/generation machine; `FeedController.accept`; forbidden-payload-key guard; component-registry dispatch shape; `ui_layout_bridge.dart`; `uigateway.proto`'s `UiInputSynapse`; the perf-tier closed loop; the fake-transport reactivity test suite | B-1 (client contract), B-5 (component catalog, surface neuron) | B-5 proofs green, including "no network traffic on local interaction" and "unknown component is a hard error". **Explicit rejections in writing:** RFW interpreter, `flutter_bloc`, `default: SizedBox.shrink()` | After Phase 4 |
| **L-8** | `ino` (2942 C#, 101 Dart, 1248 md, 15 `.feature`) | `INeuron<T>`/`IReactsTo<T>` interface-only dispatch; `IDomain` verb manifest; `Capability` discriminated union; `BrainTraceFilter`; the L1 propose→gate→approve→hot-register pipeline; BDD-Gherkin-as-LLM-mock with provenance; the E2E harness with OTel-`Activity`-based assertions; ephemeral-port test isolation; **~13 design docs carrying rationale that exists nowhere else** — the decay model, the L1/L2/L3 constraints, the graph-DB rejection, the UI-patch-as-synapse argument, the C#-interface-as-canonical-schema decision | B-2 (manifest, capability); B-1 (trace filter); B-6 (proposal pipeline); B-4 `.Testing` (BDD mock); the ~13 docs → decision records in this repository | Each of the ~13 docs is either transcribed as a decision record or rejected in writing with a reason | **Expected to remain open for months.** Transcribing 13 design documents into decision records is real work and marking this row done early is exactly how the good designs get lost. Delete `tripradar/` (2,162 files, a vendored external product) and the 841 vendored `node_modules` markdown **immediately** — those are separable and require no analysis |
| **L-9** | `digitalbrain` + `sdk` (683 C#, 107 Dart, 696 md) | `QuerySynapseSynthesizingIncomingFilter` — **the highest value-per-line artifact in `sources/`**; `BrainTimelineRelayGrain` cursor ring buffer; `TimelineRelayGrain` implicit subscription; `LlmModel` descriptor + derived service key; the single-flag local-provider override; uniform `ChatClientBuilder` middleware with GenAI semantic conventions; mocks keyed identically to real clients; fingerprint priming bound to the real system-prompt constant; `IconPlan` deterministic icon derivation; `docs/ABI.md`'s two-interlocking-guards freeze pattern; `docs/DIGITALBRAIN_RESEARCH.md` (52 KB ADR corpus); `docs/redesign/01-ARCHITECTURE.md` (the palette/layout rebuild seam with a rejected-alternatives table) | B-1 (filter, relay, ring buffer); B-4 (descriptor, override, middleware, mocks); B-5 (icons, seam); ADR corpus → decision records | B-1 and B-4 proofs green; ABI freeze pattern adopted or rejected in writing; the four named documents transcribed or rejected | **Expected to remain open for months.** 568 of its 696 markdown files are agent session scratch and are deletable immediately with this one-line justification. The `.agents - Copy/` directory (~120 session directories) likewise |
| **L-10** | `brain_from_master` (284 C#, 210 Dart, 41 md) | `SurfaceFeedNeuron` — the complete feed state machine, two-axis compaction, gap detection, reset protocol; the six-check revision-bound action model; the timer-plus-reminder outbox with self-unregistering reminders; the five-scope ownership table; the error taxonomy with mandated client behaviours; the failure-behaviour matrix; `.mcp.json`, `.lsp.json`, and the MSBuild auto-init; `EVERYTHING-IS-A-NEURON.md` (626 lines); the module SDK spec | R-1 and B-1 (feed); §4.3 (scopes); B-2 (module SDK); Phase 0.5 (`.mcp.json`) | R-1 and B-1 proofs green including reset-on-gap; five-scope table present in §4.3; B-2 conformance green. **Explicit rejections in writing:** the 250 ms server-side poll, JSON-in-proto, RFW | **Retires last.** It is not an earlier generation (§2.15) — it is contemporaneous and ahead of v2 in three dimensions. Its two design documents answer questions this plan has not yet asked. Do not delete while any part of §8 remains unbuilt |

### Immediate deletions requiring no analysis

These are separable from their rows and can go in Phase 0:

- `sources/Projects/ino/domains/travel/tripradar/` — 2,162 files, a wholly vendored external
  SaaS product with its own stack, integrated over HTTP. Not part of the architecture.
- 841 markdown files under `tripradar/**/node_modules/` — third-party package READMEs.
- 568 files under `sources/Projects/digitalbrain/.agents - Copy/` — multi-agent session scratch
  from ~120 runs.
- All duplicated vendored skill directories (~89 files in `digitalbrain` alone across
  `.claude`, `.github`, `.agents - Copy`).
- `sources/Projects/ino/website/` — verified byte-identical to `ino/IAW/website/`, and `ino`'s
  own `CLAUDE.md` already calls the root copy "legacy and purged".

### Fate of `sources/Projects/CLAUDE.md`, `CONTINUATION.md`, and the skills directories

`CLAUDE.md` instructs any agent in that tree that *"`final/` is the canonical, current
codebase. Start there for all new work"* and describes a workspace rooted at `E:\Projects`.
`CONTINUATION.md` instructs a from-scratch NeuroOS build with a nine-step order and a
typed-C#-only constraint. Both are live-voiced instructions for a repository that is not this
one, and the harness injects `CLAUDE.md` into context on any read beneath it.

**Both are retained until L-4 closes** — `CONTINUATION.md` is the primary evidence for §1 — and
**both are marked archival in Phase 0.6**, with a header stating they describe a superseded
workspace.

The nested skills directories are renamed in Phase 0.3, not deleted, because they belong to
rows still open. They are deleted with their rows.

---

## 11. Deliberately left open

Recorded so the next plan cannot mistake silence for a decision.

**The three-tier test split.** v2 runs 84 Tier-0 contract tests, 23 Tier-1 Reqnroll simulations
on a shared 3-silo in-process cluster, and 5 Tier-2 Aspire hosted tests. `ino` explicitly
rejected multi-tier ladders (*"Multi-tier test ladders (Tier 0/1/1.5/2/3). Just e2e."*). `IAW`'s
middle tier was functionally indistinguishable from its first. `v3` collapsed four overlapping
drivers into a single `Simulation` class on the principle that *"the test framework and the
safety gate are the same machine."*

Tier-0 and Tier-2 clearly earn their place. **Tier-1 needs a stated reason to exist beyond
"Gherkin."** The candidate reason is that natural-language scenarios are readable by
non-engineers — which is only a benefit if non-engineers read them. If they do not, Tier-1 is
Tier-0 with a parser in front. Decide before Phase 3 adds module conformance, because
conformance will land in whichever tier is judged to earn it.

**Dispatch mechanism (R-6).** Promote the generated manifest or delete it. Depends on
measurement.

**Delivery ordering (R-2).** The code currently promises FIFO by construction; nothing documents
it. Fixing head-of-line blocking changes an unstated guarantee. State the guarantee first.

**`IEmit<T>`'s future.** Under DEC-2 a module descriptor declares its contributions explicitly.
`IEmit<T>` may become redundant with the descriptor, or may remain as the compile-time-checked
form the descriptor is generated from. Resolve during Phase 3.1.

**Whether any DSL is ever proposed again.** If one is, `final`'s Gate-0 method is the standard:
a pre-registered benchmark with a numeric kill threshold, executed before an interpreter is
written. `final` ran 20 prompts against an 80% bar, scored 60%, and formally demoted the
language. That lineage attempted a full interpreted language three times and abandoned it three
times. A fourth attempt needs the gate first.

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

Phase 0.1. Twenty source files under `sources/` are not in git, thirteen of them a complete
Dart SDK package. Until they are committed, this entire ledger rests on a false premise and
`sources/` cannot be touched.

---

## 14. Known defects in this plan

This document was reviewed adversarially after being written. Six problems survived. Two are
unresolved decisions and block specific phases; four are drafting gaps that must be closed
during execution. **None of them is fixed. Do not start the blocked phases without closing the
blocking item first.**

### 14.1 — BLOCKER for Phase 3.5. DEC-6 has a concurrency hole.

Orleans grains are single-threaded per activation. If `IGpt5` is a neuron at a stable key,
**every inference call serializes through one activation per owner.** Four agents asking
concurrently queue behind each other for the full latency of every call.

Every escape is bad in a different way:

- `[Reentrant]` — journal writes then interleave and `History[^1].Sequence == LastSequence`
  is no longer safe without locking.
- Key per call — it is a factory with a grain-shaped API; the journal holds one entry per
  activation and means nothing.
- Key per caller — plausible, but then "the GPT-5 neuron" is N neurons and the timeline shows
  N feeds rather than one model's traffic.

DEC-6 chose the typed grain call over a raw injected client largely on observability grounds.
**That was mispriced.** A raw client is observable too — `digitalbrain` did it properly with
OpenTelemetry GenAI semantic conventions. What the grain call uniquely buys is presence on the
*synapse timeline* specifically, which is worth less than DEC-6 claims, against a concurrency
cost that is worth more.

**Re-decide before Phase 3.5.** The raw-client option and the model-neuron option are much
closer than DEC-6 states.

### 14.2 — BLOCKER for B-3. The plan schedules credential storage ahead of the boundary that protects it.

R-3 states its own proof is "insufficient on its own." DEC-5 then places OAuth refresh tokens
in cluster-addressable grains, and §2.6 establishes that an unattributed caller — any Orleans
client — passes the owner filter unchecked.

As written, the plan schedules "put every user's Google credentials in a grain any cluster peer
can address" and files the mitigation as future work. **That ordering is wrong.** Resolve one
of two ways before B-3:

- store credentials outside the cluster-addressable surface, or
- promote R-3 to a hard prerequisite for B-3 rather than a note beside it.

### 14.3 — DEC-1 and B-1 interact badly, and no section notices.

Call-filter reification writes every grain call to the caller's feed. The feed is bounded. A
neuron making ten model calls in a turn fills its own feed with inference traffic and **evicts
its actual domain events.**

The bound protects storage and CPU. It does not protect signal-to-noise. **Nothing in this plan
states what is feed-worthy versus merely traced.** Decide during Phase 2.4, before the filter
ships.

### 14.4 — Phase 2 is too large to be one phase and silently rewrites the test suite.

R-1 changes `ReadJournalAsync`, the entire client read API, and all 23 Reqnroll scenarios go
through it. §8 says "every consumer changes" in one line and never sizes it. Phase 2 realistically
touches the kernel, the client, both samples, the probe host, the testing package, and all 23
feature files, and it has no rollback point. **Split it before starting, with a green gate
between the feed state machine (2.1–2.3) and the observation rail (2.4–2.6).**

### 14.5 — Two explicit questions are unanswered.

- **Is `LLMNeuron` the right name?** §7's rename table does not cover it.
- **Does the owner concept belong in the kernel at all?** §4.3 adds scope machinery without
  questioning the premise. `brain_from_master` proved the feed must be keyed by identity rather
  than transport, which suggests owner-in-kernel is right — but this plan assumes it rather than
  arguing it.

### 14.6 — DEC-3 and §4.2 are in tension.

DEC-3 puts `IHandle<T>`/`IEmit<T>` on the interface *so the wiring graph is reflectable without
loading the implementation*. §4.2 then says discovery is explicit registration, **not**
reflection. If the descriptor declares the neurons anyway, the interface declaration is
redundant — unless the descriptor is *generated from* it, which is exactly R-6's contract-
manifest work. §11 flags this for `IEmit<T>` and leaves it unresolved for the descriptor.
**Resolve in Phase 3.1.**
