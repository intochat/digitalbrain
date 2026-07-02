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
- **B.** Cluster/replica interaction proven via a reusable 3-silo Reqnroll scenario (delivered — see §2.1/Task 4). **Attempted and reverted:** replacing `Neuron.cs`'s broadcast mechanism with Orleans' native `AddBroadcastChannel` — empirically found to be key-partitioned, not a global fan-out, and incompatible with non-grain stream consumers; the existing hand-rolled `IsBroadcast`+memory-stream mechanism is the correct primitive for this use case and was kept unchanged. See §2.2.
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

### 2.2 B — Cluster interaction (REVERTED: BroadcastChannel does not fit; kept the existing mechanism)

**Original premise (wrong, kept here for the record):** `[ImplicitChannelSubscription]` on a grain causes automatic subscription on activation, and since a grain ID is one logical instance cluster-wide regardless of silo count, "prove behavior across replicas" should mean a channel write reaches every subscribed grain regardless of which silo hosts them.

**What plan Task 9 found empirically (not theoretically) when it actually tried this:** `AddBroadcastChannel`/`[ImplicitChannelSubscription]` is **key-partitioned**, not a global fan-out. A publish to a given `ChannelId(name, key)` only reaches grain activations whose *own* key matches — it does not reach arbitrarily-keyed grains subscribed to "the same logical channel" the way the existing hand-rolled mechanism does. Task 9's implementer ran the real broadcast-critical test suite against the swap: publishing to `ChannelId("digitalbrain-timeline", Guid.Empty)` spun up parallel `Guid.Empty`-keyed instances of ~30 neuron types that received the broadcast, while the actual arbitrarily-string-keyed grains the system uses (`"broadcast-receiver"`, `"generated-telegramresponder"`, etc.) never received it at all. 4 of 5 broadcast-dependent test files failed under the swap; all 5 pass on the original mechanism.

**Second, independent blocker found:** `SignalEgressStreamSubscriber` is a plain (non-grain) service that also consumes the same broadcast stream. `[ImplicitChannelSubscription]` is a grain-activation-only mechanism — there is no way for a non-grain consumer to subscribe to a `BroadcastChannel` at all, forcing an awkward dual-publish even if the grain-side problem above were solved.

**What was confirmed to work correctly** (so the composition reasoning below wasn't wasted, even though the underlying primitive was wrong): `[ImplicitChannelSubscription]` on an abstract base class *is* correctly discovered on subclasses, and a runtime `ShouldSubscribeToTimeline`-style opt-out *does* compose correctly with the compile-time attribute (verified: a grain that never calls `.Attach()` genuinely opts out). If a future, different use case ever needs true per-key broadcast channels, that composition pattern is sound — it just isn't the right primitive for *this* use case (one shared timeline every neuron listens to, regardless of its own key).

**Decision: kept the existing hand-rolled `IsBroadcast` + memory-stream mechanism in `Neuron.cs` (`FireAsync`, `SubscribeTimelineIfNeeded`, `OnNextAsync`) unchanged.** No code swap. This is not a compromise or a fallback — it's the correct primitive for a globally-shared, non-grain-consumable, arbitrary-key-addressed broadcast surface, which `AddBroadcastChannel` was never designed for.

**Lesson, recorded for calibration:** this was the controller's own research mistake, not a delegated sub-agent's — reading Orleans' `AddBroadcastChannel` docs for API shape ("decouples producer from consumer") without tracing the actual key-partitioning delivery semantics produced a fundamentally wrong design premise. It was caught only by an implementer actually running the swap against real tests, not by more reading. Same class of lesson as verifying delegated research, applied to verifying one's own.

### 2.3 C — CapabilityGate: blocklist → allowlist

`CapabilityGate.FindViolations` (`Foundry/CapabilityGate.cs`) already does correct Roslyn semantic analysis — resolves the bound symbol via `GetSymbolInfo`, not raw text, so aliasing can't bypass it. The fix is purely about *what* it checks: replace `BannedNamespacePrefixes` (5 entries) with `AllowedNamespacePrefixes`. **As actually implemented, this is much broader than "primitives/collections/LINQ":** the allowlist is the *entire* `System.` namespace plus `DigitalBrain.Core.`, narrowed only by six explicit exclusions (`System.Net.`, `System.IO.`, `System.Diagnostics.Process.`, `System.Reflection.Emit.`, `System.Runtime.InteropServices.`, `System.Runtime.Loader.`) rather than an enumerated safe subset. Every existing seeded pack (`MarketplaceSeeds.cs`) must still compile under the new allowlist — this is the concrete regression check, not a judgment call.

**Confirmed gap, not closed by this design:** because the allowlist is "all of `System.` minus 6 prefixes" rather than a true enumerated safe surface, `System.Type.GetType(...)` + `System.Activator.CreateInstance(...)` (also `System.Reflection.Assembly.Load`) fall inside the broad allowance and are not excluded — a pack can reflectively construct/invoke any of the explicitly-banned APIs above via a string-keyed type name with zero statically-resolvable symbol reference for the walker to catch. `CapabilityGate` is therefore a guardrail against accidental misuse today, not a security boundary against adversarial/untrusted code. Tracked in `docs/specs/2026-07-02-capability-gate-hardening-followup.md`.

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
5. **B** (highest blast radius — touches `Neuron.cs`, which every neuron in the system extends; done last, verified against the full suite plus the new 3-silo cluster scenarios from step 2). **Outcome:** attempted, empirically found to not fit (see §2.2), reverted — no code change landed; the 3-silo scenario built in step 2 already proves the *existing*, unchanged mechanism works, so this step's actual deliverable ended up being the correction to this spec.

## 4. Testing

- A's own deliverable *is* the testing story — no separate test-the-tests task.
- C: a scenario per allowlist boundary (accepted: `DigitalBrain.Core`/collections/LINQ; rejected: `System.Net`, `System.IO`, plus the original 5 blocklist entries as regression cases) using the Driver.
- D: the spike's own pass/fail is the test; the implementation task re-runs the full journal-dependent suite (`PackConfigStoreTests`, `HandlerGrowthTests`, anything touching `OutgoingJournal`/`IncomingJournal`) to confirm no serialization regressions.
- B: the 3-silo cluster scenario from A/2.1 already proves a broadcast synapse fired on one silo reaches a subscriber grain hosted on another, against the existing (unchanged) mechanism — no separate testing task needed since no mechanism change landed.

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
