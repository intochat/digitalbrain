# DigitalBrain v2 — Active Execution Prompt

You are Claude Code (Fable 5), sole architect, implementer, reviewer, and committer for DigitalBrain in
`E:\brain`, branch `master`. This file is the loop contract: every session reads it, finds the first
unticked milestone, executes it, and ticks it. Work until the Definition of Done holds. Never push.

## Mission

Rebuild DigitalBrain **from scratch** as an open-source .NET framework whose paradigm is
**neurons, synapses, and simulations**:

- **Neuron** — a durable agent: an Orleans journaled grain with dual durable journals (incoming and
  outgoing synapses), typed identity, owner-bound authorization, and restart recovery.
- **Synapse** — an immutable typed message record carrying `SynapseMetadata` (synapse id, correlation
  and causation lineage stamped on every hop, caller, receiver, routing mode, timestamp). Synapses are
  the programming model: a neuron declares `IHandle<TSynapse>` for what it consumes and `IEmit<TSynapse>`
  for what it produces, wiring provable at build time through a source-generated dispatch manifest.
  Broadcast reaches every subscribed neuron durably; point-to-point delivery is guaranteed and typed.
- **Simulation** — the testing primitive, shipped as a public dev-only package: fire a synapse into a
  real in-process cluster, expect synapses on the timeline. The framework's own test suite and its
  consumers' test suites use the same machine.

This is a **foundation framework** — the strong base the full Digital Brain system will grow on.
Package quality, API design, and codebase discipline must stand next to Orleans and Aspire.

## Scope Fence — Framework, Not Product

The framework ships: neuron runtime, synapse fabric, subscription registry, multi-silo support, AI model
binding (tiers + provider isolation), client package, Aspire integration, testing package, dev tools,
quickstart. **Explicit non-goals for v2** (they come later, on top): marketplace and bundles, pack
signing, runtime code loading (Roslyn/AssemblyLoadContext), rule engines and any `.ino` language, UI
surface/widget systems, federation and peers, voice, MCP servers, fork/quarantine/world machinery.
One forward-compatibility constraint stands in their place: dispatch and the subscription registry must
tolerate neuron types registered after silo start, so a distribution layer can exist later without
kernel surgery. Adding any non-goal feature is a contract violation, not initiative.

## Verdict on v1

The v1 implementation (commits `e67c2031`..`342e4702`) is rejected wholesale: wrong execution,
unacceptable code quality. It survives only as git history and requirements evidence. **No v1 code is
adapted, copied, or wrapped.** The prototype generations under `sources/Projects/**` are read-only
requirement evidence with a harvest map below — patterns may be relearned, code is never copied.

## Testing Architecture (ratified — the single way tests are written)

Three tiers, one vocabulary, no exceptions:

- **Tier 0 — contract tests.** Plain xUnit, no cluster. Validation, serialization attributes, alias
  pinning, dispatch-manifest completeness (statically provable without a silo), public API baselines.
- **Tier 1 — simulations.** Reqnroll `.feature` files over **one shared in-process Orleans TestCluster
  per test run** (never per scenario). These features ARE the framework specification and are published
  on the website. The driver ships in `DigitalBrain.Testing`: `Fire` a synapse, `Expect`/`ExpectNone`
  synapses, observed through a collector neuron and OTel activities keyed by correlation id — never
  test hooks in production code. Scenario isolation comes from unique owner/brain grain keys inside the
  shared cluster. Multi-silo scenarios run on a dedicated 3-silo fixture tagged `@multisilo`;
  durability scenarios restart silos in-cluster tagged `@durability`. A deterministic scripted AI
  provider ships in `DigitalBrain.Testing`; real provider SDK adapters are exercised against it over
  HTTP endpoint overrides — no fake `IChatClient` in production paths.
- **Tier 2 — hosted proof.** A handful of tests over `Aspire.Hosting.Testing` and a dedicated Testing
  AppHost project (test posture is structural — a distinct AppHost type — never env-var mutation).
  Proves packaging, resource wiring, and restart recovery on the real host.

Hard rules: no `Task.Delay` or arbitrary polling in steps — if a scenario needs a sleep, the delivery
guarantee is wrong and the framework gets fixed, not the test. No `@ignore`: a scenario is green, red
while driving current TDD, or deleted. Feature files carry zero comments; a scenario needing an apology
is a design bug. Gherkin is reserved for behavior that reads as specification; mechanical checks stay
in Tier 0. `DigitalBrain.Testing` is dev-only and a CI guard forbids production packages from
referencing it.

## Quality Bar (every milestone is judged against this)

- `TreatWarningsAsErrors`, `Nullable` enabled, `LangVersion` latest, latest .NET analyzers at
  `AnalysisLevel` latest-all. Zero suppressions without a recorded justification in this file.
- `Microsoft.CodeAnalysis.PublicApiAnalyzers` on every packable project: the public surface is an
  explicit, reviewed artifact.
- Naming carries all meaning. Zero comments of any kind in tracked source, including XML doc comments
  and feature files. Package documentation lives in package READMEs and the website.
- Sealed concrete types by default. An abstraction exists only with two or more real consumers or a
  package-boundary reason. No speculative extension points. If a milestone adds more than ~15 public
  types, cut or justify here.
- Strict TDD: failing test first with the real failure recorded, minimum implementation, green,
  refactor while green. Never `dotnet test --filter`. Root gate is exactly
  `dotnet test .\DigitalBrain.slnx -c Release` (background, poll).
- Deterministic, SourceLink-enabled, snupkg-symboled packs. Gate commands and results recorded in each
  milestone's execution record.
- Every commit keeps the repository green. Conventional-commit subjects; every commit ends with:
  `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`

## Non-Negotiable Constraints

- Verify every package/framework API against Microsoft Learn MCP, official Orleans/Aspire/OpenAI/
  Anthropic/Reqnroll docs, and api.nuget.org before writing code against it. This overrides the repo
  Context7 rule. Never read `C:\Users\` paths or the local NuGet cache.
- Latest deliberate versions, re-verified live at M0. Floor as of 2026-07-19: Orleans `10.2.2-rc.2`
  line with Journaling `10.2.2-rc.2.alpha.1`, Orleans Dashboard `10.2.2-rc.2`, Aspire `13.4.6`,
  MEAI + MEAI.OpenAI `10.8.0`, OpenAI `2.12.0`, Anthropic `12.36.0`, Reqnroll `3.3.4` line with
  xunit.v3, Microsoft.Agents.AI.DevUI preview (DevTools only). Never call a prerelease stable.
- Never introduce: string- or kind-based routing; `DispatchProxy`; generic JSON invocation; untyped
  ask/send taking strings or dictionaries; copied provider APIs; a custom journal provider; volatile
  production durability; streams as source of truth; client-side provider SDKs; ambient provider-key
  lookup in public APIs; production fake AI clients; provider secrets through `AsClient()`; sample
  `ProjectReference` shortcuts; a monolithic package dragging AppHost dependencies; `Task.Delay`-based
  test synchronization.
- Security boundary: provider SDKs and credentials live only in `DigitalBrain.Kernel`.
  `WithReference(brain)` is privileged and kernel-only; `brain.AsClient()` exposes only Orleans client
  discovery and safe metadata. Abstractions, Client, Testing, and Aspire packages never reference
  provider SDKs.
- `sources/**` is read-only historical evidence. Never touch it, never copy from it.
- **Docs-current invariant:** `website/` (VitePress) is part of the deliverable. Any milestone that
  changes public API or concepts updates the affected website pages in the same commit. The
  `website/tests/site.test.mjs` truth-guard is rewritten against the v2 API at M0 and kept green;
  `npm test` + `npm run build` for the website join the CI gates at M10. Tier-1 feature files are
  published on the website as the specification.
- Multiagent orchestration is authorized: workflows and subagents for research, review fan-out, and
  adversarial verification at will; parallel implementation agents only in isolated worktrees whose
  output you merge and re-gate in the main session. You are the only committer.
- Stop and ask the operator only for: any external publish (NuGet, GitHub push — explicit approval
  every time), a missing required credential, an official API disproving the design, or a decision
  that materially changes this contract. A NuGet API key must never appear in the repository.

## Prototype Harvest Map (requirement evidence only — never copy code)

- `sources/Projects/final` — `Synapse`/`SynapseMetadata` with `Stamp` lineage; `IHandle<T>`/`IEmit<T>`
  on scannable interfaces; `Neuron` base verbs and lifecycle re-broadcast; source-generated
  `DispatchManifest` with reflection-union fallback; dual durable journals; the N+1 feature file as
  scenario template. Known defect to design away: single global memory stream drops synapses for
  late subscribers.
- `sources/Projects/ino` — real subscription/routing registry (`Discovery` grain); `PinToSilo`
  placement filter; fast Aspire-free 2-silo TestCluster fixture with cross-silo fire proofs;
  structural Testing AppHost; OTel-span-based synapse assertions; reusable Gherkin step vocabulary;
  feature-corpus-driven deterministic LLM.
- `sources/Projects/IAW` — `AddIAW` resource facade with fluent model/tier declarations, single
  custom `WithReference` propagation, `AsClient()` split; per-provider factory with
  `IsConfigured`/`CreateClient`; tier-to-model indirection. Anti-lesson: mock-heavy tests that
  outsourced correctness to manual verification.
- `sources/Projects/v3` + workspace docs — Simulation-is-a-neuron testing philosophy (`Fire`/`Expect`,
  one base, no mocks for the core loop, testing as separate dev-only package); v4's packaging plan
  (thin core, kernel NuGet, Aspire hosting package, CI guard against prod→test references).
- `sources/Projects/digitalbrain` — anti-lesson: no-silo testable-implementation mirrors drift from
  real neurons and cannot prove durability; never let the fast tier replace the cluster tier.

## Loop Protocol (every session, in order)

1. Read this file and `git log --oneline -10`. Confirm clean worktree; if dirty, finish or discard the
   interrupted slice using the recorded milestone state.
2. Take the first milestone with an unticked box. Re-verify any external API it touches.
3. Execute with strict TDD in small slices. Tier-1 simulations drive design; Tier-0 tests pin
   contracts as they stabilize.
4. Run the milestone gate, then the root gate. Update `website/` if public API or concepts moved.
5. Review the diff (multiagent review for milestones marked **[review]**), fix every actionable
   finding.
6. `git diff --check`, commit with the milestone's message, tick the boxes, append a 3–6 line
   execution record (baseline SHA, red evidence, gates, review outcome, commit SHA).
7. Continue immediately. End the session only on Definition of Done or a listed stop condition.

## Milestones

### M0 — Demolition and clean skeleton **[review]**
- [x] Delete v1 wholesale: `kernel/`, `integrations/`, `hosts/`, `modules/`, `samples/`, `tests/`,
      `eng/`, `behaviors/`, `edge/`, `Brain.slnx`, `Directory.Build.props`, `Directory.Packages.props`,
      `.github/workflows/*` (all stale). Keep: `.git*`, `LICENSE`, `CLAUDE.md`, `GOAL.md`, `README.md`,
      `assets/nuget/`, `docs/`, `sources/`, `website/`.
- [x] Re-verify all dependency pins live (including Reqnroll + xunit.v3 + Microsoft Testing Platform);
      record the resolved set in the Decision Log.
- [x] Fresh skeleton: `DigitalBrain.slnx`, central `Directory.Build.props`/`Directory.Packages.props`
      implementing the Quality Bar, `.editorconfig`, empty packable projects with PublicAPI baselines
      (`DigitalBrain.Abstractions`, `DigitalBrain.Kernel`, `DigitalBrain.Client`,
      `DigitalBrain.Testing`, `DigitalBrain.Aspire`, `DigitalBrain.Aspire.Hosting`,
      `DigitalBrain.DevTools`), test projects (`tests/DigitalBrain.Tests`,
      `tests/DigitalBrain.Simulations`, `tests/DigitalBrain.HostTests`), CI workflow running the root
      gate.
- [x] Reset `website/` to a truthful minimal skeleton (home, concepts, status) telling the
      neurons+synapses+simulations story; rewrite `site.test.mjs` against the v2 skeleton; delete the
      dead Flutter deploy pipeline.
- [x] Gate: root gate green on the skeleton; website `npm test` green; `git diff --check`.
- Commit: `chore!: demolish v1 and establish v2 skeleton`
- Execution record: baseline `db85c990`, commit `29eceef8`. No TDD red (skeleton milestone); the
  site truth-guard caught two of its own regex defects red-first. Gates: root
  `dotnet test .\DigitalBrain.slnx -c Release` exit 0 (14 Tier-0 tests), website `node --test` 9/9 +
  `vitepress build` clean (local npm-script indirection is broken in this harness — commands run
  direct; CI runs the npm scripts). Review: 40-agent adversarial workflow, 12 findings, 5 confirmed
  and fixed (tracked `.config` sentinel, wrong repo URLs in Build.props and website config, README
  package list), 7 refuted. `sources/**` untouched; a `sources/` ignore line now guards it.

### M1 — Design decisions (recorded, not prose-heavy)
- [ ] Decide and record in the Decision Log: exact `SynapseMetadata` shape and `Stamp` semantics;
      broadcast delivery guarantee (ordering, at-least-once semantics, late-activation replay policy)
      and how the durable outbox + Orleans streams implement it; subscription registry design (real
      queryable registry, silo-restart behavior, late-type tolerance); neuron identity + owner
      authorization model; package target frameworks; what Gherkin vocabulary `DigitalBrain.Testing`
      ships.
- [ ] Adversarial multiagent design review: simplicity, API ergonomics, durability soundness,
      framework-not-product discipline. Cut anything speculative.
- Commit: `docs: record v2 architecture decisions`

### M2 — Neuron kernel **[review]**
- [ ] First red Tier-1 simulation: a neuron receives a fired synapse and its journals record it.
- [ ] `DigitalBrain.Abstractions`: `Synapse` base + `SynapseMetadata` + lineage stamping;
      `IHandle<TSynapse>`/`IEmit<TSynapse>`; neuron identity and owner contracts; error model. All
      Orleans serialization deliberate (`[GenerateSerializer]`, `[Id]`, `[Alias]` pinned).
- [ ] `DigitalBrain.Kernel`: `Neuron` base on official Orleans journaling with dual durable
      incoming/outgoing journals; typed emit/send verbs; recursion depth guard; owner-bound incoming
      call authorization; source-generated dispatch manifest with completeness proof and
      late-registered-type tolerance.
- [ ] `DigitalBrain.Testing` born alongside: shared-cluster fixture, `Fire`/`Expect` driver, collector
      neuron, base Gherkin steps.
- Commit: `feat: implement the neuron kernel`

### M3 — Durable synapse fabric **[review]**
- [ ] Broadcast that survives late subscription and silo restart: journaled outbox + Orleans streams
      as transport, redelivery until acknowledged, no synapse ever silently dropped (the v1-prototype
      memory-stream defect is the named enemy).
- [ ] Real subscription registry: queryable subscriber counts per synapse type, updated on neuron
      activation/registration, correct across restart.
- [ ] The N+1 gate: a neuron type registered at runtime receives the next broadcast — subscriber count
      grows by exactly one, no restart, proven in a Tier-1 simulation.
- [ ] Guaranteed typed point-to-point delivery with lineage stamping and reply addressing.
- Commit: `feat: implement the durable synapse fabric`

### M4 — Multi-silo and recovery **[review]**
- [ ] 3-silo Tier-1 fixture; `@multisilo` scenarios: cross-silo point-to-point, cross-silo broadcast,
      registry correctness cluster-wide, silo-labeled placement for pinned neurons.
- [ ] `@durability` scenarios: silo restart mid-conversation, journal replay, outbox redelivery,
      no synapse loss.
- Commit: `feat: prove multi-silo delivery and recovery`

### M5 — AI model binding **[review]**
- [ ] Typed model descriptors and role tiers (fast/balanced/reasoning/embedding); per-provider
      factories (OpenAI + Anthropic via official SDKs + MEAI) inside the kernel only; options
      validation, health checks, OTel.
- [ ] Neurons consume AI through typed capability injection bound to tiers — provider choice is
      AppHost configuration, never neuron code.
- [ ] Deterministic scripted provider in `DigitalBrain.Testing`; real adapters proven against it over
      HTTP endpoint overrides with synthetic secrets.
- Commit: `feat: bind AI model tiers to neurons`

### M6 — Client package **[review]**
- [ ] `DigitalBrain.Client`: owner sessions, typed neuron access, fire-and-observe from outside the
      cluster, outgoing owner filter. Orleans client only; no provider SDKs; no reflection routing.
- Commit: `feat: implement the owner-bound client`

### M7 — Aspire integration **[review]**
- [ ] `DigitalBrain.Aspire.Hosting`: brain resource composing Orleans + storage (separate stores per
      concern), fluent tier/model declarations, privileged vs `AsClient()` projections, secret-leakage
      tests, publish-manifest gate. `DigitalBrain.Aspire`: `IHostApplicationBuilder` client
      integration. Structural Testing AppHost for Tier 2.
- Commit: `feat: add Aspire hosting and client integrations`

### M8 — Hosts, dev tools, quickstart **[review]**
- [ ] Public kernel host on official durability (no localhost clustering or in-memory reminders in
      production paths); Orleans Dashboard + DevUI in `DigitalBrain.DevTools` behind Development-only
      guards; package-only quickstart (PackageReference from local feed).
- [ ] The multiagent sample: several neurons collaborating through synapses — broadcast, reaction,
      typed reply — as the framework's flagship demonstration.
- Commit: `feat: add hosts, dev tools, and the quickstart`

### M9 — Hosted proof
- [ ] Tier-2: quickstart AppHost under `Aspire.Hosting.Testing` with the scripted provider; durable
      turn; kernel restart; delivery and journals resume; dashboard and DevUI verified; no orphaned
      processes. Optional real-provider run if keys exist.
- Commit: `test: prove hosted restart recovery`

### M10 — Release engineering **[review]**
- [ ] Pack scripts, deterministic CI build/test/pack/consumer-restore jobs including website
      `npm test` + build, `DigitalBrain` convenience metapackage (Abstractions + Client + Aspire
      only), CI guard that no production package references `DigitalBrain.Testing`, changelog,
      prerelease versioning, dependency hygiene gate, empty-cache consumer proof.
- Commit: `build: prepare the NuGet release`

### M11 — Final verification and docs **[review]**
- [ ] Website complete and truthful: neurons+synapses+simulations story, runnable 5-minute start,
      per-package pages, Tier-1 features rendered as the specification, contributing guide;
      `site.test.mjs` green against final API.
- [ ] Two independent read-only reviews (architecture; then packaging, secrets, durability, forbidden
      shortcuts) with every actionable finding fixed.
- [ ] Full gate sweep from clean state; prerelease packages + checksums staged locally; clean
      worktree; delete `docs/superpowers/` v1 planning material.
- Commit: `docs: complete the DigitalBrain v2 foundation`

## Decision Log

1. **2026-07-19 — Dependency pins re-verified live** against api.nuget.org; every floor exists:
   Orleans `10.2.2-rc.2` (Sdk, Server, Client, Core.Abstractions, Serialization, TestingHost),
   `Microsoft.Orleans.Journaling 10.2.2-rc.2.alpha.1`, `Microsoft.Orleans.Dashboard 10.2.2-rc.2`
   (official package, not community `OrleansDashboard`), Aspire `13.4.6` (Hosting, Hosting.AppHost,
   AppHost.Sdk, Hosting.Testing, Hosting.Orleans), `Microsoft.Extensions.AI` + `.Abstractions` +
   `.OpenAI` `10.8.0`, `OpenAI 2.12.0`, `Anthropic 12.36.0`, `Reqnroll 3.3.4` +
   `Reqnroll.xunit.v3 3.3.4`, `xunit.v3 3.2.2`, `xunit.runner.visualstudio 3.1.5`,
   `Microsoft.NET.Test.Sdk 18.8.1`, `Microsoft.CodeAnalysis.PublicApiAnalyzers 5.6.0`,
   `Microsoft.Agents.AI.DevUI 1.13.0-preview.260703.1`. `Directory.Packages.props` carries only
   what the code currently references; remaining pins join it as milestones consume them.
2. **2026-07-19 — `dotnet test` stays in VSTest mode** (no `test` section in `global.json`): the
   contract's exact root gate passes the `.slnx` positionally, which MTP-mode `dotnet test`
   rejects (`--solution` only); VSTest accepts the two intentionally empty test projects (MTP
   exits 8 on zero tests); Reqnroll.xunit.v3's documented setup is VSTest-based. Revisit only if
   the gate command itself is revised.
3. **2026-07-19 — `net10.0` (GA, LTS) is the working target framework**; `global.json` pins the
   10.0 SDK line (`rollForward: latestFeature`, prerelease allowed for the local
   10.0.400-preview SDK). The formal package-TFM decision stays with M1.
4. **2026-07-19 — Demolition judgment calls** beyond the listed set: `workspace/` (v1 Flutter UI,
   an explicit non-goal), `Directory.Build.targets`, `aspire.config.json`, `.lsp.json`,
   `.mcp.json`, `.codex/`, `.config/`, and all of `.github/` deleted as v1 machinery; `AGENTS.md`
   kept as a one-line pointer to CLAUDE.md; `nuget.config`, `.editorconfig`, `.gitignore`
   rewritten fresh; `README.md` rewritten truthful; untracked `.digitalbrain/keys` left
   untouched.
5. **2026-07-19 — Synapse and `SynapseMetadata` shape.** `Synapse` is an abstract
   `[GenerateSerializer]` record with exactly one serialized member,
   `[Id(0)] SynapseMetadata Metadata`, plus unserialized convenience accessors.
   `SynapseMetadata` is a sealed record: `[Id(0)] SynapseId SynapseId` (unique per synapse, the
   dedupe key), `[Id(1)] CorrelationId CorrelationId` (constant across a conversation),
   `[Id(2)] SynapseId? CausationId` (the parent's `SynapseId`, changes every hop — no separate
   causation id type), `[Id(3)] NeuronId Caller`, `[Id(4)] NeuronId? Receiver` (null under
   broadcast — no `None` sentinel exists), `[Id(5)] RoutingMode RoutingMode`
   (`PointToPoint | Broadcast`), `[Id(6)] DateTimeOffset Timestamp`. `SynapseId` and
   `CorrelationId` are `readonly record struct`s over `Guid` with pinned `[Alias]`es. The v1
   prototype's `BrainScope` is cut (federation is a non-goal). Stamping is the runtime's job at
   fire time — a pure construction, never user-set and never the v1 fill-if-default mutation: new
   `SynapseId`; `CorrelationId` inherited from the incoming synapse else fresh; `CausationId` =
   incoming `SynapseId` else null; `Caller` = the firing neuron; `Receiver`+`RoutingMode` per verb
   (`Emit` → null+`Broadcast`, `Send` → target+`PointToPoint`, `Reply` → incoming
   `Caller`+`PointToPoint`); `Timestamp` from `TimeProvider`. Every fire originates from a neuron:
   client sessions (M6) and simulations (the Testing driver) are neurons themselves, so `Caller`
   is always real and `Reply` always addressable — a neuron-less fire does not exist.
6. **2026-07-19 — Broadcast delivery guarantee and mechanism.** Broadcast is owner-scoped: the
   delivery set at emit time is every neuron instance registered in the subscription registry for
   the synapse type within the emitter's owner scope. The guarantee is at-least-once delivery per
   registered subscriber with per-(emitter, subscriber) FIFO, effectively-once processing, and no
   retroactive replay for later registrations (the N+1 rule is "receives the next broadcast", not
   history). Verified Orleans facts this rests on: grain calls are at-most-once with no
   configurable runtime retry; memory streams are officially non-durable and drop for late
   subscribers (the named enemy); `BroadcastChannel` is fire-and-forget. Mechanism: the source of
   truth is the emitting neuron's durable outbox — each entry holds the synapse, its emit
   sequence, and per-subscriber delivery state, committed atomically with the neuron's state in
   its single WAL (`WriteStateAsync` batches all dirty durable states — the transactional-outbox
   property). Delivery is exclusively ordered, guaranteed typed `DeliverAsync` grain calls drained
   from the outbox per subscriber in emit order; the drain runs immediately after commit, on
   activation (covering the crash-after-commit window), and on a grain timer, with a reminder as
   the durable wake-up for dormant emitters — the reminder is ensured to exist before the first
   outbox commit, so no committed entry can lack its durable wake-up (floor 1 minute, configurable;
   tests never wait on it because activation triggers the drain). The timeline stream carries every
   synapse for observability and fire-and-observe — that is its transport role; it is never the
   delivery guarantee, and a stream fast-path delivery to handlers is deliberately cut (it bought
   latency at the price of ordering machinery). Acknowledgment: the receiver runs the handler,
   then commits the incoming-journal append and any emissions it produced in one atomic
   `WriteStateAsync` batch, and only then does `DeliverAsync` return — a crash mid-handler leaves
   the synapse unacknowledged and it is redelivered; receivers dedupe by `SynapseId` (the
   duplicate horizon is bounded by live outbox entries), making processing effectively-once.
   Delivery state is pruned on full acknowledgment; an unreachable subscriber stops redelivery
   after a bounded, configurable horizon (attempts and age) with the failure recorded in the
   outbox entry and surfaced through OTel — observable, never silent.
7. **2026-07-19 — Subscription registry.** A journaled cluster-singleton registry grain
   (`DurableGrain`, well-known key) mapping (owner, synapse type) → the set of registered
   `NeuronId`s. A neuron instance registers all its `IHandle<>` types on first activation
   (idempotent upsert — the "updated on neuron activation/registration" M3 requirement);
   subscriptions persist across deactivation, and redelivery reactivates the subscriber. The
   source-generated dispatch manifest stays the build-time completeness proof of which neuron
   types can handle which synapses; the registry tracks which instances do. Late-type tolerance:
   an instance of a newly present neuron type registers through the same call — no silo surgery,
   the owner-scoped subscriber count grows by exactly one (the N+1 gate). Queryable: subscriber
   count and subscriber list per (owner, synapse type). Restart-correct two ways: the registry's
   own journal replays, and re-registration on activation is a converging no-op. Orleans implicit
   stream subscriptions are ruled out (unsupported with heterogeneous silos; not documented for
   late-registered types) — the registry is the framework's own.
8. **2026-07-19 — Neuron identity and owner authorization.** `NeuronId` is a readonly record
   struct `(string Type, OwnerId Owner, string Name)` with a canonical string encoding
   `{owner}/{name}` as the grain key over `IGrainWithStringKey` (Orleans 10 grain identity is
   string type + string key; no string+string compound marker exists) and the neuron type as the
   `[GrainType]` name. `OwnerId` and `Name` are validated at construction: non-empty, no `/`, no
   whitespace — making the encoding provably bijective (Orleans validates no key syntax itself;
   an ambiguous encoding would let two owners share one grain identity and defeat the owner
   filter). Owner authorization is an incoming grain call filter in the kernel: grain-to-grain
   calls are validated by `IGrainCallContext.SourceId` (runtime-provided, not caller-supplied)
   resolving to a same-owner neuron; client-originated calls (no `SourceId`) reach neurons only
   through the owner session neuron `DigitalBrain.Client` establishes (M6 pins the mechanism).
   `RequestContext` is treated as untrusted metadata per official guidance; the cluster network
   boundary is the trust boundary at this layer, recorded honestly as such. The filter must
   tolerate grain-extension calls (streams/cancellation are `IGrainExtension`s).
9. **2026-07-19 — Package target frameworks.** All seven packages single-target `net10.0` (GA,
   LTS). Verified: every pinned dependency ships a net10.0-compatible asset group (Orleans
   10.2.2-rc.2 targets net10.0/net8.0; Aspire.Hosting 13.4.6 net8.0; MEAI 10.8.0 and OpenAI
   2.12.0 net10.0; Anthropic 12.36.0 net9.0/net8.0; Reqnroll 3.3.4 netstandard2.0). No
   multi-targeting, no netstandard authoring. Anthropic transitively references
   MEAI.Abstractions 10.5.1; central pinning unifies it at 10.8.0.
10. **2026-07-19 — Journaling base.** Neurons build on `Microsoft.Orleans.Journaling`'s
    `DurableGrain` with two keyed durable states — `incoming` and `outgoing` journals — injected
    at construction (the package forbids post-activation state registration; the journal set per
    neuron is fixed). One WAL per grain; replay completes before `OnActivateAsync` (SetupState
    stage); JSON journal format. The package is alpha-suffixed and experimental with Azure Blob as
    the only durable backend at this pin: accepted deliberately because the contract mandates
    official Orleans journaling and forbids a custom provider — the production host path (M8)
    uses `AddAzureBlobJournalStorage` (Azurite locally via Aspire). Tier-1 fixtures register the
    volatile journal storage, but NOT the official per-silo `new VolatileJournalStorageProvider()`
    pattern: that store dies with its silo host, which would make `@durability` restart scenarios
    vacuous. The durability fixture shares one volatile journal store across in-cluster silo
    restarts (a per-cluster store outliving the silo host), so restart scenarios prove journal
    replay from surviving storage — exactly what production proves against Blob. Any
    `ORLEANSEXP*` suppression this forces will be recorded here when it lands (v1's global
    suppression is the anti-pattern). Durability proofs must rely on journal replay plus outbox
    redelivery, never on stream transport.
11. **2026-07-19 — Gherkin vocabulary `DigitalBrain.Testing` ships.** A deliberately small step
    set over the `Fire`/`Expect`/`ExpectNone` driver (observed through the collector neuron and
    OTel activities keyed by correlation id). The simulation itself is the firing neuron — there
    is no fire-on-command of other neurons (that would need a production test hook or forged
    stamping). Steps: `Given a brain for owner "X"` (scenario isolation = unique owner key inside
    the shared cluster); `When <SynapseType> is fired` (+ `with <table>`) — broadcast stimulus;
    `When <SynapseType> is sent to <neuron>` (+ `with <table>`) — point-to-point stimulus;
    `When a <Type> neuron named "<name>" is created` (registration, the N+1 driver); `When the
    silo hosting <neuron> is restarted` (`@durability`); `Then <neuron> emitted <SynapseType>` (+
    `with <table>`); `Then no <SynapseType> was emitted`; `Then the subscriber count for
    <SynapseType> has grown by <n>` (relative, shared-cluster safe — absolute counts are not);
    `Then the incoming|outgoing journal of <neuron> contains <SynapseType>`. Steps bind through a
    per-scenario session object via Reqnroll's `IObjectContainer`. v1's chat/card/LLM step
    families are cut as product vocabulary; the deterministic scripted AI provider gets its own
    minimal steps at M5.

## Definition of Done

All milestone boxes ticked with execution records; root Release suite, pack, empty-cache quickstart
restore, hosted restart proof, and website test+build all green from final HEAD; two clean final
reviews; public API baselines committed; prerelease packages and checksums staged locally; clean
worktree; a closing report listing exactly what awaits operator approval to publish. Nothing is pushed
or published without that approval.
