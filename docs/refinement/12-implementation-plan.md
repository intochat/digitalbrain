# 12 — Implementation Plan

Phased, dependency-aware execution plan mapping the roadmap ([11](11-product-roadmap.md)) to specific files, symbols, tests, and rollback strategy. **Deletion-first within each phase.** Phases align to roadmap increments. This is a plan for a *subsequent* implementation pass — no production code was changed in this assessment.

**Conventions:** each task lists the primary files/symbols, the finding it closes, acceptance criteria (tests), and a rollback note. Effort: S (<1d), M (1–3d), L (>3d).

---

## Phase 0 — Trustworthy floor (Roadmap Inc 0)

Goal: close the exploitable/fail-open surface and delete verified-dead duplicates. Almost entirely deletion + config; low risk, high risk-reduction.

### 0.1 Disable unsafe self-evolution execution by default — `foundry:SEC-302/301/304/308`, `SEC-303` — M
- Files: `src/DigitalBrain.Kernel/Foundry/FoundryServices.cs` (DI), `CodeFoundryClosedLoopNeuron.cs` (`TrustedAutoApply`), `CodeRunNeuron.cs`/`CodeDeployNeuron.cs`.
- Change: introduce `DigitalBrain:Foundry:Enabled` (default **false**); when false, `FoundryRequest`/`RunGeneratedCode`/`DeployGeneratedCode` handlers return a fail-closed `FoundryRolledBack("foundry-disabled")`. Force `TrustedAutoApply` off unless `Foundry:Enabled`.
- Acceptance: boot test asserts Foundry disabled by default; firing an executor grain while disabled does nothing and journals a refusal.
- Rollback: single config flag; revert flips it back.

### 0.2 Close auth fail-open + secret hygiene — `kernel-hosting:SEC-100/101`, `core:SEC-001/002`, `kernel-hosting:SEC-200` — M
- Files: `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs`, `DevAuth`, the `LoginRequest`/`LocalUserRegistered` records in `Core`, DataProtection wiring in hosting extensions.
- Changes: gate `DevAutoLogin` to `IHostEnvironment.IsDevelopment()` only (not config-flippable in Production); require an out-of-band bootstrap secret for first-user provisioning; remove password fields from journaled synapse vocabulary (authenticate without journaling secrets); add `ProtectKeysWith*` (Key Vault/managed identity) to the DataProtection key ring.
- Acceptance: Production boot rejects `DevAutoLogin`; test asserts no password material in any journal; key-ring-at-rest encryption test; first-user provisioning without bootstrap secret is refused.
- Rollback: changes are additive guards; revert restores prior handler.
- Note: this task is largely subsumed by 0.5 (deleting the second auth authority) — do 0.5 and keep only the Core password-vocabulary + key-ring fixes.

### 0.3 SSRF egress allowlist + build pinning — `foundry:SEC-307/154`, `mcp-hosts-build:SEC-500` — S
- Files: `CapabilityBroker` (HTTP), `Directory.Build.targets`, `.mcp.json`, `.codex/config.toml`.
- Changes: enforce host+scheme allowlist with private/metadata/link-local blocklist, deny-by-default; pin `codegraph` to a fixed version/hash (or remove from build); pin all `@latest` tools.
- Acceptance: private/metadata targets refused; build succeeds with egress blocked.
- Rollback: allowlist is config; pins revert to prior refs.

### 0.4 Delete verified-dead Core/kernel/Flutter/proto stratum — `core:CLEAN-001`, `kernel-hosting:CLEAN-101/200`, `ARCH-100/200`, `flutter-*:CLEAN-900/ARCH-700/800` — L
- Files: ~30 Core dead types (`GrpcAuthentication.cs`, `SensitiveText.cs`, `CapabilitySynapses.cs`, `TabularDataSynapses.cs`, NuGet*/Architect*/ClosedLoop* families); kernel `IngressNeuron`, `SignalEgressBus`/subscriber, `ChatNeuron`, `TabularDataParser`, `ChatUploadClassifier`, `SyncManifest`, `SqliteSchemaInspector`; `Protos/digitalbrain.proto` + generation + stale Dart stubs; Flutter v1 rail (`DigitalBrainClientScope` + orphaned files), `palette_primitives.dart`, dead shader/lottie assets, "Challenger" scripts; remove now-orphaned deps (SQLite, ClosedXML, `graphic`, ~9 Flutter deps).
- **Precondition:** audit historical journals for dead synapse-alias instances; tombstone if present (`core:CLEAN-001` open question). If journals are ephemeral/dev-only, delete freely.
- Acceptance: solution builds; full test suite green; `git diff --stat` shows the >10% net reduction target ([09](09-code-quality-and-cleanup.md)); no dangling references.
- Rollback: pure deletion on a branch; revert restores. Do in reviewable chunks per subsystem.

### 0.5 Delete the second auth authority + legacy gateway server surface — `kernel-hosting:ARCH-101/202`, `flutter-runtime:ARCH-700` — M
- Files: `UserSessionNeuron.cs`/`DevAuth`, kernel gRPC/CORS registration with no mapped service (`kernel-hosting:CLEAN-102`), the never-mapped gateway.
- Acceptance: only `RuntimeSessionAuthority` remains as session authority; test pinning the dead proto's absence still passes; login no longer triggers Aspire orchestration (`kernel-hosting:ARCH-103`).
- Rollback: deletion on a branch.

---

## Phase 1 — MLP: Governed Gmail + Salesforce (Roadmap Inc 1)

Goal: the lovable v1 on the strong INO substrate.

### 1.1 Harden connector auth loop — `connectors:SEC-400`, `ARCH-402/403`, `PROD-400` — M
- Files: `integrations/DigitalBrain.Google/GoogleConnector.cs` + OAuth flow; shared OAuth state machine; Gmail send path.
- Changes: add S256 PKCE to Google (wire the dead `OAuthCodeVerifierKey`); unify the Google/Salesforce OAuth state machines into one tested flow; add Gmail `OutcomeUnknown` + idempotent send (persist a client `Message-ID` pre-send; reconcile on retry).
- Acceptance: Google OAuth PKCE test; single-flow tests cover both providers; Gmail retry after indeterminate send does not double-send.
- Rollback: per-connector; feature-flag the unified flow during migration.

### 1.2 Control surface (connections / proposals / history / permissions) — new UI on existing data — L
- Files: `app/lib/...` (V2 rail surfaces), reads from journal + grants + connector state via `UiGrpcService`.
- Changes: build the four user-legible views; wire revoke to token revocation; every mutation preview shows reversibility + provenance.
- Acceptance: journeys 1/2/6 demonstrated; revoke removes access; history is complete and readable.
- Rollback: additive UI; no server contract change.

### 1.3 Client platform reachability — `platform-and-skills:PROD-1000/1001/1002`, `REL-1000` — S
- Files: macOS entitlements (add `com.apple.security.network.client` + mic), `AndroidManifest.xml` (`INTERNET`, `RECORD_AUDIO`), iOS/macOS mic usage strings, remove `web/index.html` `kernel_port.txt` fetch.
- Acceptance: app connects on macOS/Android/web; voice input works where declared.
- Rollback: manifest/plist revert.

### 1.4 Constrain RFW action surface + URI allowlist — `flutter-ui:SEC-800/802` — S
- Files: RFW widget dictionary, `UiKitLink`.
- Changes: declared action allowlist for server-emitted UI; URI scheme allowlist.
- Acceptance: server cannot trigger an unlisted action or arbitrary URI scheme.
- Rollback: allowlist config.

---

## Phase 2 — One identity, one core (Roadmap Inc 2)

### 2.1 Mandatory principal/tenant on Synapse — `core:ARCH-002`, `kernel-hosting:ARCH-101` — L
- Files: `Core/Synapse.cs` base + records; `RuntimeRequestContext` population; dispatch.
- Changes: add `PrincipalRef`/`TenantId`/`WorkspaceId` as additive fields (populate from authenticated context), then enforce non-empty on ingress; explicit `[Id(n)]` on all serialized fields.
- Acceptance: contract-freeze tests; ingress rejects unattributed synapses; journal replay stable across a parameter reorder (negative test).
- Rollback: additive phase is safe; enforcement behind a flag until validated.

### 2.2 Delineate TCB; relocate Core crypto/rate-limiter — `core:ARCH-001` — M
- Files: move HMAC token crypto + rate limiter out of `Core` into the kernel/TCB; document the TCB boundary.
- Acceptance: `Core` contains only contracts/vocabulary; architecture test enforces the boundary.
- Rollback: mechanical move.

### 2.3 Durability groundwork — `core:REL-001`, `kernel-runtime:PERF-100/REL-050` — L
- Files: synapse id derivation (deterministic; logical idempotency key), journal compaction/archival in `NeuronJournals`.
- Acceptance: journal-growth benchmark ([07](07-performance-and-reliability.md) step 1) shows flat per-message cost; deterministic replay test.
- Rollback: compaction behind a flag; id change requires the additive-id migration (do with 2.1).

---

## Phase 3 — Governed self-evolution (Roadmap Inc 3)

### 3.1 Bind approvals to the INO evidence model — `kernel-runtime:SEC-050/051/052`, `core:PROD-001` — L
- Files: `SelfEvolutionNeuron.cs`, `Core/SelfEvolution.cs` (`SelfEvolutionDecision`), reuse `DurableInoContracts` patterns.
- Changes: decisions carry principal + content-hash + single-use nonce; verifier (in TCB) checks all three; enforce `RequiresHumanApproval`; classify risk in-kernel (ignore proposer-supplied).
- Acceptance: forged/unbound/replayed decision rejected; approval whose content-hash ≠ proposal rejected; `RequiresHumanApproval=false` cannot skip a human on T1+.
- Rollback: verifier is additive and fail-closed; if it over-rejects, the safe failure is "nothing applies."

### 3.2 Real checkpoint/restore + retriable apply + verify phase — `kernel-runtime:ARCH-051/REL-101/103` — L
- Files: `Neuron.CreateCheckpointAsync`/`RestoreCheckpointAsync` (use the existing `CheckpointProtector`), apply registry.
- Changes: restore replaces state (not append); checkpoint cost independent of timeline length; approved apply is retriable + idempotent; add a post-apply verify step before marking success.
- Acceptance: rollback test restores prior state exactly; crash-between-decision-and-apply recovers and completes; verify-failure triggers rollback.
- Rollback: keep the old additive path behind a flag until the new restore is proven.

### 3.3 Out-of-process execution for all tiers — `foundry:SEC-302/301/308`, `REL-300` — L
- Files: `FoundryServices.cs` (stop registering `InProcessAlcExecutor`; wire `OutOfProcessSandbox` as `ICodeExecutor`), `ScriptRunner` (fix zero-reference gate), `CodeDeployNeuron` (add gate), executor grains (reachable only from apply registry).
- Changes: Run/Deploy/scripts all go out-of-process with timeout + memory/CPU caps; Deploy gated; fix the ScriptRunner compilation-reference bug so the gate actually binds symbols.
- Acceptance: escape-attempt tests (reflection/`dynamic`/`System.Environment`/`Environment.Exit`/infinite-loop) fail to execute; timeout fires on a hanging script; test asserts no in-process executor registered; direct grain fire outside the rail refused.
- Rollback: executor selection is DI config; keep Foundry gated off (Phase 0.1) until these pass.

### 3.4 Enforce pack signing — `connectors:SEC-405` — M
- Files: `PackAlcEmbodier`/`GeneratedPackRuntime` call sites; `PackSignatureVerifier`/`PublisherTrust`.
- Changes: verify signature + publisher trust before embodiment; refuse unsigned/untrusted.
- Acceptance: unsigned/untrusted pack refused; signed+trusted pack embodies.
- Rollback: verification is fail-closed; disable pack embodiment if issues.

### 3.5 Per-tier policy + remove TrustedAutoApply — `foundry:SEC-303`, [05](05-self-evolution.md) — M
- Changes: implement T0–T5 policy table; remove or hard-restrict `TrustedAutoApply` (T0/T1 only, mandatory journal, hard config guard).
- Acceptance: each tier enforces its required controls; T5 (kernel) unreachable by the rail.

---

## Phase 4 — Connect to anything (Roadmap Inc 4)

### 4.1 ConnectorRegistry + CapabilityManifest — `connectors:ARCH-400/401`, `core:ARCH-003` — L
- Files: new `ConnectorRegistry`/`CapabilityManifest`; migrate `GmailReadNeuron`/`Salesforce*Neuron` to capability implementations; delete hardcoded provider branches in `ConversationNeuron.IsProviderTool`/`InoMutationGrants.RequiredForTool` (default-deny unknown).
- Acceptance: add a third connector with zero kernel/INO edits; unknown capability denied.
- Rollback: registry resolves the same capabilities; behavior-preserving; feature-flag the switchover.

### 4.2 Shared ConnectorHost + generalized mutation contract — `connectors:PROD-400` — M
- Files: extract rate-limit/backoff/pagination/minimization/egress wrapper; promote Salesforce preview→apply→verify to the shared contract.
- Acceptance: both connectors run through the shared host; mutation contract has mandatory idempotency + `OutcomeUnknown`.

---

## Phase 5 — Scale and operate (Roadmap Inc 5)

### 5.1 Multi-replica edge + real health + rolling deploy — `mcp-hosts-build:REL-500/PROD-501`, `kernel-runtime:ARCH-050`, `kernel-hosting:FRAME-200` — L
- Changes: replica-safe session/feed; resolve Orleans double-config; `/health` reflects Orleans connectivity; implement real drain/verify rolling update (replace the simulation; remove the `FailAtReplica` prod test hook).
- Acceptance: zero-downtime deploy across ≥2 replicas; health fails when Orleans is down.

### 5.2 Benchmark plan + toolchain decision — [07](07-performance-and-reliability.md), `mcp-hosts-build:PROD-500`, `kernel-runtime:FRAME-050` — M
- Changes: run benchmarks 1–7; publish capacity SLOs; decide `net11.0`/alpha-journaling (pin LTS or accept+document with a servicing/migration plan).
- Acceptance: capacity numbers published; toolchain decision recorded in an ADR.

---

## Cross-cutting acceptance gates

- After **every** phase: full `dotnet test` from root (min verbosity), `aspire doctor`, and `git diff --check` clean.
- Security phases (0, 3) additionally require the negative/adversarial tests named above to pass **and** a re-run of the relevant [06](06-security-threat-model.md) threat checks.
- No phase merges while a control it introduces has a test that "passes" against an unwired class (the `dotnet-tests:SEC-600/601` anti-pattern) — every asserted control must be exercised on the production path.

## Rollback strategy (global)

- Phase 0 is dominated by deletion and config flags — trivially reversible per chunk.
- Phases 2–3 make risky changes (identity enforcement, execution model, durability) **additive-then-enforced behind flags**, so the safe failure mode is always "fall back to the prior path" or "nothing applies," never "wrong thing applies."
- The journal-format migration (2.3/3.2) is the one irreversible-ish step; gate it behind a one-time, tested replay/rewrite with a verified backup of the durable store before cutover.
