# Neuron/Pack Authoring: Spec-First Testing + Full-Orleans-Native Cluster Architecture

- **Status:** Approved for implementation
- **Date:** 2026-07-02
- **Owner:** Vladyslav Horbachov
- **Repo:** `digitalbraintech/framework` (`brain/`) — no `app/` changes
- **Branch:** `spec/neuron-pack-testing-architecture` (based off `master`)

## 0. TL;DR

Today, authoring a new Neuron/Synapse/Pack and proving it works is real but rough: the fast single-pack loop is genuinely fast, but proving *cross-neuron* and *cross-replica* behavior means hand-rolling 150-250 lines of `TestClusterBuilder` boilerplate per spec (a prior initiative already flagged this gap and explicitly deferred it), the platform's own security boundary (`CapabilityGate`) is a blocklist with a known-open gap, and the journal's serialization layer (`JournalJsonContext`) is a second, entirely redundant, hand-maintained type registry sitting next to Orleans' own automatic one.

**Locked principle:** all new neuron behavior ships as a **pack** (`IPackBehavior`), never a new grain type compiled into the kernel — even for internal authoring — so the security boundary is already correctly shaped for when external creators show up later.

This spec redesigns four coupled pieces as one integrated architecture, all grounded in verified current code and verified current Orleans/Reqnroll documentation (not assumption):

- **A.** A spec-first Reqnroll workflow: one shared, reusable step vocabulary backed by the **Driver Pattern** (Reqnroll's actual, documented composition mechanism — not "scenarios calling scenarios," which doesn't exist in the framework), so a new pack's cross-neuron and cross-replica behavior is provable in a few lines of Gherkin instead of hand-rolled cluster setup.
- **B.** Cluster/replica interaction proven and implemented via Orleans' **native `AddBroadcastChannel`** (`[ImplicitChannelSubscription]` + `IOnBroadcastChannelSubscribed`), replacing `Neuron.cs`'s hand-rolled subscription-handle bookkeeping.
- **C.** `CapabilityGate` flipped from a blocklist to an **allowlist**, with tier-enforcement made consistent, and the gate's own rules expressed as Reqnroll scenarios using the same Driver.
- **D.** `JournalJsonContext`'s manual 126-entry registration **eliminated via a Roslyn incremental source generator** (auto-discovers every `Synapse` subtype at build time). **Updated post-implementation:** a spike (plan Task 1) confirmed Orleans.Journaling's `orleans-binary` format also eliminates the registration and works correctly (verified via a real write + forced-reactivation + read round-trip) — but also surfaced that Microsoft's own docs frame `orleans-binary` as the legacy/compat format, with migration guidance pointing *toward* JSON, not away from it. Given that, the shipped direction stays on JSON (the framework's stated forward direction) and generates the registration instead of hand-maintaining it, rather than adopting the format Microsoft is steering users away from.

## 1. Current state (verified, not assumed)

- **Pipeline works:** `IPackBehavior` requires only `Respond(string)`; everything else has defaults. Publish → Roslyn compile → collectible ALC → embody → dispatch, all through one grain type (`GeneratedNeuron`). The single-silo fast loop is genuinely ~15 minutes per `docs/authoring-a-bundle.md`.
- **Journal storage is already Orleans-native:** `Neuron.cs` uses `Orleans.Journaling`'s `DurableGrain` base class and `IDurableList<Synapse>` (`#pragma warning disable ORLEANSEXP005 // Alpha/experimental journalling APIs`) for both `IncomingJournal`/`OutgoingJournal`. This is not something to migrate *to* — it's already there.
- **But broadcast is hand-rolled on top:** `Neuron.FireAsync` checks `stamped.IsBroadcast` and pushes onto a memory stream (`GetStreamProvider(...).Timeline().OnNextAsync(...)`); `SubscribeTimelineIfNeeded` (`Neuron.cs:110-136`) manually manages subscription handles across reactivations (`GetAllSubscriptionHandles`, `ResumeAsync`, manual dedup of duplicate subscriptions) — ~25 lines reimplementing what Orleans' `[ImplicitChannelSubscription]` already does automatically and correctly on grain activation.
- **`JournalJsonContext` is a redundant parallel registry:** a plain `System.Text.Json.Serialization.JsonSerializerContext` with 126 flat `[JsonSerializable(typeof(X))]` entries, wired as the serializer for Orleans.Journaling's `UseJsonJournalFormat()`. Every `Synapse` subtype is *already* `[GenerateSerializer]`-annotated for Orleans' own automatic serializer (zero manual registration) — this second registry exists only because journaling happens to be configured for JSON format. It already caused one real bug (a missing registration, found during a prior test-harness initiative).
- **`CapabilityGate` does real semantic analysis** (`compilation.GetSemanticModel` → `GetSymbolInfo` → resolved symbol name), not brittle text matching — but it's a 5-entry blocklist (`Process`, `Reflection.Emit`, `InteropServices`, `Runtime.Loader`, `Registry`); `System.Net`/`System.IO` are open by default, and the gate only runs in the Tier-1 in-process path per the repo's own `Foundry/README.md`.
- **Reqnroll is thin and duplicative:** `Features/` is flat (5 files, 181 lines); the richest cross-neuron scenario (Telegram's N+1 reactivity) hand-rolls its own `TestClusterBuilder`/`ISiloConfigurator`, duplicating the DI wiring `NeuronTestBase` already provides to 19 migrated xUnit files — because a prior initiative's harness migration explicitly excluded `Steps/*.cs`, flagging it as "a future round" (`.superpowers/sdd/progress.md`).

## 2. Design

### 2.1 A — Spec-first Reqnroll workflow (Driver Pattern)

**Correction from initial framing:** Reqnroll's own migration docs state plainly that calling steps by string (SpecFlow v3 behavior) is *removed* in Reqnroll — "Refactor to the Driver Pattern or call methods directly." There is no scenario-calls-scenario mechanism in Gherkin or Reqnroll, in this tool or any BDD tool. The Driver Pattern is the real, documented replacement: extract shared logic into a plain class, inject it via Reqnroll's per-scenario DI container into `[Binding]` step classes. Multiple step definitions — and thus multiple scenarios, across multiple feature files — share the same underlying driver instead of each reimplementing it.

**`PackSpecDriver`** (new, `DigitalBrain.Tests/Specs/PackSpecDriver.cs`): the single shared engine behind every pack's spec. Built on `NeuronTestBase` (already proven — single-silo `TestCluster`, `PackAlcEmbodier` pre-registered, `RejectUnsignedPacks=false`), extended with an `InitialSilosCount` override for cluster scenarios. Exposes driver methods covering the vocabulary a pack spec needs: publish a pack from source, install it, fire a synapse at it, assert an emitted synapse (type + props), assert a named neuron received a synapse, assert a config-form surface was emitted for a pack with `RequiredConfig`, assert an installed pack's compilation was accepted/rejected by `CapabilityGate`.

**Step-binding classes** (`DigitalBrain.Tests/Steps/PackSpecSteps.cs`) become thin: constructor-inject `PackSpecDriver`, each `[Given]`/`[When]`/[Then]` step is a one-line delegation to a driver method. This is what actually closes the boilerplate gap the prior initiative flagged — not by migrating the *existing* duplicated files line-by-line, but by giving new pack specs a path that never needs the duplication in the first place. `TelegramReactiveLoopSteps.cs`'s hand-rolled `TestClusterBuilder`/`TelegramReactiveLoopSiloConfig` (`Steps/TelegramReactiveLoopSteps.cs:52-56,207-237`) gets deleted and its scenario re-expressed on the new driver, as the first proof this actually replaces the old pattern rather than sitting alongside it.

**Co-located, spec-first convention:** feature files move from the flat `Features/` directory to living alongside pack source — one folder per pack (e.g. `DigitalBrain.Tests/Authoring/TelegramResponder/TelegramResponder.feature` next to the existing pack source). The intended workflow: write the `.feature` first (RED — the pack doesn't exist, or the scenario fails), then write the pack (GREEN). This is TDD applied to pack authoring, matching this repo's existing culture.

### 2.2 B — Cluster interaction via native Orleans BroadcastChannel

Verified against Orleans docs: `[ImplicitChannelSubscription]` on a grain causes automatic subscription on activation — `IOnBroadcastChannelSubscribed.OnSubscribed` receives an `IBroadcastChannelSubscription` and calls `.Attach<T>(onItem, onError)`. Since a grain ID is one logical instance cluster-wide regardless of silo count (Orleans doesn't run N copies of a grain for N replicas — replicas provide fault-tolerant placement, not concurrent duplication), "prove behavior across replicas" means: a channel write from whichever silo hosts the publisher reaches every subscribed grain regardless of which silo happens to host *them*. That's exactly what `[ImplicitChannelSubscription]` gives natively.

**Changes to `Neuron.cs`:**
- Delete `SubscribeTimelineIfNeeded` (`Neuron.cs:110-136`) and the manual `GetAllSubscriptionHandles`/`ResumeAsync`/dedup logic entirely.
- **A real subtlety, not glossed over:** `[ImplicitChannelSubscription]` is a compile-time class attribute — every activated instance of the annotated class subscribes unconditionally. But `ShouldSubscribeToTimeline` (`Neuron.cs:103`) is a *runtime* decision some subclasses override (static neurons only care if they declare at least one `IHandle<T>`; `GeneratedNeuron` always subscribes since its handled types are dynamic). The two don't compose for free. Resolution: put `[ImplicitChannelSubscription]` on the base `Neuron` class (satisfies the compile-time requirement) and implement `IOnBroadcastChannelSubscribed.OnSubscribed` to call `subscription.Attach(...)` **only when `ShouldSubscribeToTimeline` is true** — otherwise no-op. This preserves the existing runtime-conditional behavior; the implementation task must verify (not assume) that a grain which never calls `.Attach()` incurs no meaningful per-message overhead from Orleans' broadcast-channel plumbing, since every neuron in the system is now unconditionally in the "activated and eligible" set even when it opts out of attaching.
- `Broadcast(Synapse s)` (`Neuron.cs:177`) changes from `FireAsync(s with { IsBroadcast = true })` (which pushes onto `GetStreamProvider(...).Timeline().OnNextAsync(...)`) to obtaining an `IBroadcastChannelWriter<Synapse>` via `IBroadcastChannelProvider.GetChannelWriter(channelId)` and calling `.Publish(stamped)`. The journal-write and causal-lineage stamping (`AddToJournal`, `Stamp`) stay exactly as they are — only the fan-out mechanism changes.
- The silo builder needs `siloBuilder.AddBroadcastChannel("digitalbrain-timeline")` (or equivalent channel name) registered wherever `WireKernelSilo`/`AddFoundry` currently registers memory streams for `SynapseStream.ProviderName`.

**Risk:** this is the highest-blast-radius change in this spec — every neuron in the system depends on broadcast dispatch. Sequenced last among the four (after A gives us the cluster-interaction test harness to validate it), with full-suite verification at each step, not batched with D.

### 2.3 C — CapabilityGate: blocklist → allowlist

`CapabilityGate.FindViolations` (`Foundry/CapabilityGate.cs`) already does correct Roslyn semantic analysis — resolves the bound symbol via `GetSymbolInfo`, not raw text, so aliasing can't bypass it. The fix is purely about *what* it checks: replace `BannedNamespacePrefixes` (5 entries) with `AllowedNamespacePrefixes` (an explicit safe surface: `System` primitives/collections/LINQ, `DigitalBrain.Core`, `System.Text.Json` for payload construction) and reject any resolved symbol whose containing namespace doesn't match. Every existing seeded pack (`MarketplaceSeeds.cs`) must still compile under the new allowlist — this is the concrete regression check, not a judgment call.

Second, close the Tier-1/Tier-2 inconsistency the repo's own `Foundry/README.md` already documents: either wire `CapabilityGate.FindViolations` into `Sandbox/OutOfProcessSandbox.cs`'s compile path too, or update the README to state explicitly that out-of-process execution's OS-level process isolation is the intended substitute (a decision, not an oversight left undocumented).

Third: express the gate's own accept/reject rules as Reqnroll scenarios using the Driver from 2.1 (`Given a pack source that calls "System.Net.Http.HttpClient" / When it is compiled / Then compilation is rejected with violation "System.Net"`) — the platform's own security invariants become spec-first too, using the exact same mechanism as pack business logic, not a separate xUnit-only concern.

### 2.4 D — Auto-generate JournalJsonContext (resolved after the spike ran)

Every `Synapse` subtype already carries `[GenerateSerializer]` for Orleans' own automatic serializer — zero manual registration, already used for every grain-to-grain message in the system. `JournalJsonContext`'s 126-entry manual list exists only because Orleans.Journaling is currently configured for `UseJsonJournalFormat()`.

A bounded spike (plan Task 1) tested the real alternative — `JournaledStateManagerOptions.JournalFormatKey = "orleans-binary"` — via an actual write + forced-reactivation + read round-trip against the real `Microsoft.Orleans.Journaling` package, not just documentation. It works, with zero manual type registration. But the spike also checked Context7's Orleans docs before writing code (per standing policy) and found `"orleans-binary"` documented as the *legacy* format, with migration guidance pointing toward JSON — i.e., the framework's own stated direction is away from the format this spec originally favored.

**Resolved direction:** stay on JSON (Microsoft's stated forward direction) and eliminate the manual-registration pain with a Roslyn incremental source generator that scans the compilation for `Synapse`-derived types and auto-emits `JournalJsonContext`'s `[JsonSerializable]` list at build time — turning "forgot to register a new Synapse type" (a bug that already shipped once) into a structural impossibility, without adopting a path Microsoft is steering users away from. The spike's round-trip test remains valuable prior art even though its specific format choice isn't what ships.

## 3. Sequencing and risk

Ordered for safety, not just logical grouping — each step independently verifiable before the next begins:

1. **D spike** (small, bounded, answers a yes/no question before anything else commits to a direction).
2. **A** (additive only — new test infrastructure, zero production code touched; gives every later step a real cluster/cross-neuron test harness to verify against).
3. **C** (contained to `Foundry/CapabilityGate.cs` + tests; regression-checked against every seeded pack).
4. **D implementation** (using the spike's confirmed direction; deletes `JournalJsonContext`).
5. **B** (highest blast radius — touches `Neuron.cs`, which every neuron in the system extends; done last, verified against the full suite plus the new 3-silo cluster scenarios from step 2).

## 4. Testing

- A's own deliverable *is* the testing story — no separate test-the-tests task.
- C: a scenario per allowlist boundary (accepted: `DigitalBrain.Core`/collections/LINQ; rejected: `System.Net`, `System.IO`, plus the original 5 blocklist entries as regression cases) using the Driver.
- D: the spike's own pass/fail is the test; the implementation task re-runs the full journal-dependent suite (`PackConfigStoreTests`, `HandlerGrowthTests`, anything touching `OutgoingJournal`/`IncomingJournal`) to confirm no serialization regressions.
- B: the 3-silo cluster scenario from A/2.1, plus the existing 2-silo `HomeFeedCrossSiloTests` pattern extended, proving a broadcast synapse fired on one silo reaches a subscriber grain hosted on another.

```
dotnet build Brain.slnx
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj
aspire doctor
aspire run   # manual: install a pack via marketplace, confirm cross-neuron + cross-replica behavior unchanged
```

## 5. Non-goals

- No changes to `app/` (Flutter) — this is entirely backend/test-architecture.
- No change to the pack-vs-grain-type split itself beyond locking "packs only" as the creation path going forward — existing kernel-resident grain types are unaffected.
- No sandboxing/trust-model redesign beyond CapabilityGate's blocklist→allowlist flip — signing/trust (ECDSA, `RejectUnsignedPacks`) is unchanged.
- No migration of *every* existing `.feature`/`Steps/*.cs` file onto the new Driver in this pass — only `TelegramReactiveLoopSteps.cs` (as the proof) and any net-new specs. The rest is a follow-up, not blocked by this spec.
- No swap to Orleans' `AddBroadcastChannel` for anything other than the Synapse timeline broadcast — `HomeFeedBus`/UI surface streaming is untouched.
