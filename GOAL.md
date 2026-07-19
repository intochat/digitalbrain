# DigitalBrain v2 — Active Execution Prompt

You are Claude Code (Fable 5), sole architect, implementer, reviewer, and committer for DigitalBrain in
`E:\brain`, branch `master`. This file is the loop contract: every session reads it, finds the first
unticked milestone, executes it, and ticks it. Work until the Definition of Done holds. Never push.

## Mission

Rebuild DigitalBrain **from scratch** as an open-source .NET multiagent framework whose paradigm is
**neurons and synapses**:

- **Neuron** — a durable, owner-bound agent: an Orleans journaled grain addressed only through a typed
  grain interface, with exactly-once external-operation semantics and restart recovery.
- **Synapse** — a typed, durable connection between neurons: the first-class public API through which
  neurons subscribe to and react to each other, built on journaled outbox + Orleans streams
  (streams are transport, never truth).

The deliverable is a **foundation framework** — the strong base the full Digital Brain system will grow
on. Package quality, API design, and codebase discipline must stand next to Orleans and Aspire.

## Verdict on v1

The v1 implementation (commits `e67c2031`..`342e4702`, Tasks 0–10 of the 2026-07-18 plan) is rejected
wholesale: wrong execution, unacceptable code quality. It survives only as git history and as evidence
of requirements. **No v1 code is adapted, copied, or wrapped.** The design specs under
`docs/superpowers/specs/` remain valid requirement input; the architecture is yours to redesign where it
makes the framework simpler and stronger.

## Quality Bar (operationalized — every milestone is judged against this)

- `TreatWarningsAsErrors`, `Nullable` enabled, `LangVersion` latest, latest .NET analyzers at
  `AnalysisLevel` latest-all. Zero warning suppressions without a recorded justification in this file.
- `Microsoft.CodeAnalysis.PublicApiAnalyzers` on every packable project: the public surface is an
  explicit, reviewed artifact, never an accident.
- Naming carries all meaning. Zero comments of any kind in tracked source, including XML doc comments.
  Package-level documentation lives in each package README and the root README.
- Sealed concrete types by default. An abstraction exists only with two or more real consumers or a
  package-boundary reason. No speculative extension points, no "just in case" options, no unused
  parameters. If a milestone adds more than ~15 public types, cut or justify here.
- Strict TDD: assigned failing test, record the real failure, minimum implementation, green, refactor
  while green. Never `dotnet test --filter`. Root gate is exactly
  `dotnet test .\Brain.slnx -c Release` (background, poll).
- Deterministic, SourceLink-enabled, snupkg-symboled packs. Every gate command and result is recorded in
  the milestone's execution record in this file.
- Every commit keeps the repository green. Conventional-commit subjects, and every commit ends with:
  `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`

## Non-Negotiable Constraints

- Verify every package/framework API against Microsoft Learn MCP, official Orleans/Aspire/OpenAI/
  Anthropic docs, and api.nuget.org before writing code against it. This overrides the repo Context7
  rule. Never read `C:\Users\` paths or the local NuGet cache.
- Latest deliberate versions, re-verified live at M0. Floor as of 2026-07-19: Orleans `10.2.2-rc.2`
  line with Journaling `10.2.2-rc.2.alpha.1` (no stable exists), Orleans Dashboard `10.2.2-rc.2`,
  Aspire `13.4.6`, MEAI + MEAI.OpenAI `10.8.0`, OpenAI `2.12.0`, Anthropic `12.36.0`,
  Microsoft.Agents.AI.DevUI `1.13.0-preview.*` (preview, DevTools only). Never call a prerelease stable.
- Never introduce: Kind routing; DispatchProxy; generic JSON invocation; keyed provider DI; copied
  provider APIs; `Ask`; `InvokeMcpTool`; a custom journal provider; volatile production durability;
  streams as source of truth; client-side provider SDKs; ambient provider-key lookup; production fake
  AI clients; provider secrets through `AsClient()`; sample `ProjectReference` shortcuts; direct
  provider calls from console or DevUI; a monolithic package dragging AppHost dependencies.
- Security boundary: provider SDKs and credentials live only in `DigitalBrain.Kernel`.
  `WithReference(brain)` is privileged and kernel-only; `brain.AsClient()` exposes only Orleans client
  discovery and safe metadata. Abstractions, Client, and Aspire packages never reference provider SDKs.
- `sources/**` is read-only historical evidence. Never touch it.
- Multiagent orchestration is authorized: workflows and subagents for research, review fan-out, and
  adversarial verification at will; parallel implementation agents only in isolated worktrees whose
  output you merge and re-gate in the main session. You are the only committer.
- Stop and ask the operator only for: any external publish (NuGet test service, nuget.org, GitHub
  push — explicit approval every time), a missing required credential (finish everything else, report
  the one blocker), an official API disproving the design, or a decision that materially changes this
  contract. A NuGet API key must never appear in the repository.

## Loop Protocol (every session, in order)

1. Read this file and `git log --oneline -10`. Confirm clean worktree; if dirty, finish or discard the
   interrupted slice first using the recorded milestone state.
2. Take the first milestone with an unticked box. Re-verify any external API the milestone touches.
3. Execute with strict TDD in small slices. Run the milestone gate, then the root gate.
4. Review the diff (multiagent review for milestones marked **[review]**), fix every actionable finding.
5. `git diff --check`, commit with the milestone's message, tick the boxes, append a 3–6 line execution
   record (baseline SHA, red evidence, gates, review outcome, commit SHA) under the milestone.
6. Continue to the next milestone immediately. End the session only on Definition of Done or a listed
   stop condition.

## Milestones

### M0 — Demolition and clean skeleton **[review]**
- [ ] Delete v1 wholesale: `kernel/`, `integrations/`, `hosts/`, `modules/`, `samples/`, `tests/`,
      `eng/`, `behaviors/`, `edge/`, `Brain.slnx`, `Directory.Build.props`, `Directory.Packages.props`,
      old workflow files. Keep: `.git*`, `LICENSE`, `CLAUDE.md`, `GOAL.md`, `README.md`,
      `assets/nuget/`, `docs/`, `sources/`.
- [ ] Re-verify all dependency pins live; record the resolved set here.
- [ ] Create the fresh solution skeleton: `DigitalBrain.slnx`, central `Directory.Build.props` /
      `Directory.Packages.props` implementing the full Quality Bar, `.editorconfig`, empty package
      projects with PublicAPI baselines, one placeholder test project, CI workflow running the root gate.
- [ ] Gate: root gate green on the skeleton; `git diff --check`.
- Commit: `chore!: demolish v1 and establish v2 skeleton`

### M1 — Design decisions (recorded, not prose-heavy)
- [ ] Decide and record in the Decision Log: package graph and IDs; target frameworks for packages
      (net8.0 vs multi-target — justify against consumer reach); the Neuron public API; the Synapse
      public API (subscription model, delivery guarantees, replay semantics); conversation surface;
      ownership/authorization model; what v1 concepts are dropped.
- [ ] Adversarial multiagent design review: simplicity, API ergonomics, durability soundness. Cut
      anything speculative.
- Commit: `docs: record v2 architecture decisions`

### M2 — Abstractions: neuron and synapse contracts
- [ ] `DigitalBrain.Abstractions`: neuron marker + typed identity, owner identity, error model, model
      descriptors and role configuration, conversation contracts, synapse contracts. All Orleans
      serialization deliberate (`[GenerateSerializer]`, `[Id]`, `[Alias]` pinned).
- [ ] Contract tests: validation, serialization attributes, alias stability, API surface baseline.
- Commit: `feat: define neuron and synapse contracts`

### M3 — Kernel runtime: durable neurons **[review]**
- [ ] `DigitalBrain.Kernel`: durable Neuron base on official Orleans journaling; owner-bound incoming
      call authorization; external-operation ledger with exactly-once transitions; reminder-driven
      recovery; capability catalog.
- [ ] TestCluster suites proving authorization denial, ledger transitions, and journal replay recovery.
- Commit: `feat: implement the durable neuron runtime`

### M4 — Synapses **[review]**
- [ ] Journaled outbox + stream delivery as the one neuron-to-neuron rail; typed subscription API;
      recovery after silo restart; no stream-as-truth anywhere.
- [ ] Tests: delivery, redelivery after restart, ordering guarantees as designed, misuse rejection.
- Commit: `feat: implement durable synapses`

### M5 — Client package
- [ ] `DigitalBrain.Client`: owner sessions, typed neuron access, conversation and role facades, outgoing
      owner filter. Orleans client only; no provider SDKs; no reflection routing.
- Commit: `feat: implement the owner-bound client`

### M6 — AI providers in the kernel **[review]**
- [ ] OpenAI + Anthropic chat and embedding adapters via official SDKs + MEAI inside the kernel only;
      options validation, health checks, OTel; conversation neuron persists turns on the ledger with
      idempotent turn IDs.
- [ ] Fake-transport HTTP tests through the real adapters; no production fake `IChatClient`.
- Commit: `feat: bind AI providers to conversation neurons`

### M7 — Aspire integration **[review]**
- [ ] `DigitalBrain.Aspire.Hosting`: composite resource (Orleans + Azure Storage stores split per
      concern), model/embedding declarations, privileged vs `AsClient()` projections, secret-leakage
      tests, publish-manifest gate. `DigitalBrain.Aspire`: `IHostApplicationBuilder` client integration.
- Commit: `feat: add Aspire hosting and client integrations`

### M8 — Hosts, DevTools, quickstart **[review]**
- [ ] Public kernel host on official durability (no localhost clustering, no in-memory reminders in
      production paths); Orleans Dashboard + DevUI in `DigitalBrain.DevTools` behind Development-only
      guards; package-only quickstart (PackageReference from local feed) with an interactive console.
- [ ] A multi-neuron sample where neurons collaborate through synapses — the multiagent proof.
- Commit: `feat: add hosts, dev tools, and the quickstart`

### M9 — Live proof and restart recovery
- [ ] Quickstart AppHost with test-only HTTP provider endpoints + synthetic secrets through the real
      adapters; durable turn; kernel restart; conversation resumes; synapse delivery resumes; dashboard
      and DevUI verified; no orphaned processes. Optional real-provider turns if keys exist.
- Commit: `test: prove restart recovery end to end`

### M10 — Release engineering **[review]**
- [ ] Pack scripts, deterministic CI build/test/pack/consumer-restore jobs, `DigitalBrain` convenience
      metapackage (Abstractions + Client + Aspire only), changelog, prerelease versioning, dependency
      hygiene gate (vulnerable/deprecated/unexpected-preview fails; DevUI preview allowlisted),
      empty-cache consumer proof.
- Commit: `build: prepare the NuGet release`

### M11 — Final verification and docs **[review]**
- [ ] Root README telling the neurons+synapses story with a runnable 5-minute start; per-package READMEs;
      CONTRIBUTING.md; two independent read-only reviews (architecture; then packaging, secrets,
      durability, forbidden shortcuts) with every actionable finding fixed.
- [ ] Full gate sweep from clean state; prerelease packages + checksums staged locally; clean worktree.
- [ ] Delete `docs/superpowers/` v1 planning material and absorb anything still load-bearing; this file
      shrinks to its Decision Log or is deleted per operator choice.
- Commit: `docs: complete the DigitalBrain v2 foundation`

## Decision Log

(append dated, numbered decisions here as milestones record them)

## Definition of Done

All milestone boxes ticked with execution records; root Release suite, pack, empty-cache quickstart
restore, and live restart proof all green from final HEAD; two clean final reviews; public API baselines
committed; prerelease packages and checksums staged locally; clean worktree; a closing report listing
exactly what awaits operator approval to publish. Nothing is pushed or published without that approval.
