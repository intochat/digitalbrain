# 09 — Code Quality and Cleanup

Applying the repository's own method (Elon's step 2: *delete before you add*). This chapter is the delete-first ledger: dead code, duplicate systems, oversized files, boundary violations, and refactoring candidates. Deleting these is the **cheapest, safest, highest-signal** work in the whole plan — it removes attack surface, ends duplicated authority, and shrinks the reading burden before any construction. Evidence: `CLEAN-*` and `ARCH-*` findings across [file-audit/](file-audit/).

## Deletion candidates (verified dead — zero production callers)

Grouped by subsystem. Each was confirmed by repo-wide reference search in the owning audit.

### Core (`core:CLEAN-001`, High — ~30 dead types)
Dead Salesforce OAuth records; `NuGet*`/`Architect*`/`ClosedLoop*`/chart families; `MemoryStored`; `IUserGrain`; `SalesforceOAuthCallback`; whole dead files `GrpcAuthentication.cs`, `SensitiveText.cs`, `CapabilitySynapses.cs`, `TabularDataSynapses.cs`; unused `NeuronAgentMetadata`; most of `McpContracts.cs`. **~25% of Core's public surface.** *Precondition:* audit historical journals for instances of dead synapse families before deleting their aliases (may need tombstones) — the one open question flagged by the Core audit.

### Kernel (`kernel-hosting:CLEAN-101/200`, Medium — the pre-v2 stratum, ~1,100+ lines)
`IngressNeuron` (unauthenticated broadcast), `SignalEgressBus`/`SignalEgressStreamSubscriber`, `ChatNeuron`, `TabularDataParser` (zip-bomb, `PERF-100`), `ChatUploadClassifier`, `SyncManifest`, `SqliteSchemaInspector` (registered-but-unused). All leftovers of the never-mapped `DigitalBrainGateway`. Deleting removes the SQLite + ClosedXML dependencies too ([08](08-framework-and-dependency-audit.md)).

### Legacy gateway + second auth authority (`kernel-hosting:ARCH-100/200/202`, `flutter-runtime:ARCH-700`)
`Protos/digitalbrain.proto` (compiled `Both`, served nowhere; a test pins its absence) + the stale checked-in Dart stubs; the entire orphaned Flutter v1 rail (`DigitalBrainClientScope` never mounted, ~3k deletable lines); `UserSessionNeuron`/`DevAuth` second session authority (fail-open, plaintext-password — delete rather than fix). **Note:** the dead proto's `GetPackConfig` RPC is an unauthenticated decrypted-secret-pull design — deleting it also removes a latent danger that must never be revived.

### Foundry (`foundry:SEC-152`, `CLEAN-302`)
Either wire `OutOfProcessSandbox`/`ISandboxedExecutor` as the real executor (preferred — see [05](05-self-evolution.md)/[06](06-security-threat-model.md)) or delete it; today it is registered-but-dead. `SandboxTier.Wasm` is an aspirational enum member with no implementation. The whole Foundry closed loop currently has **no production entry point** (`foundry` PROD note) — if the decision is not to ship self-evolution in the first increment, gate the entire loop off (see [11](11-product-roadmap.md)).

### Flutter (`flutter-ui:ARCH-800`, `flutter-sdk-and-tests:CLEAN-900/901`)
`palette/palette_primitives.dart` (~810 dead lines anchoring `lottie` + `flutter_earth_globe`); `LiquidGlassShaderPainter` + shader asset; the dead `GlassMaterial` shader path; the 13.3 MB `orbit.lottie` and both `assets/rfw/*.rfwtxt` demo files (referenced nowhere, shipped in every build); the "Challenger" tool scripts (one targets deleted files and always exits 1); ~9 dead pubspec deps + the `graphic` dependency.

### Docs (`mcp-hosts-build:CLEAN-500`, Medium)
`docs/` is dominated by stale one-shot agent artifacts (execution logs, superseded plans) that contradict the repo's own "living docs only" policy. Keep `README.md`, `CLAUDE.md`, and this `docs/refinement/` set; archive/delete the rest. (This dossier folder is the authorized exception per the task.)

## Duplicate / conflicting systems (end the two-of-everything)

This is the structural root cause behind most High findings. Each pair should collapse to the stronger (V2/INO) member:

| Duplicated authority | Weak member (delete/retire) | Strong member (keep) | Refs |
|---|---|---|---|
| Identity / tenancy | legacy `UserId="anonymous"` on synapses | `TenantId`/`WorkspaceId`/`PrincipalRef` | `core:ARCH-001/002` |
| Session / auth | `UserSessionNeuron`/`DevAuth` (12h, no revoke) | `RuntimeSessionAuthority` (15m, revocable) | `kernel-hosting:ARCH-101/202` |
| Client↔kernel gateway | legacy `DigitalBrainGateway` (dead) | V2 `UiGrpcService` | `kernel-hosting:ARCH-100`, `flutter-runtime:ARCH-700` |
| Approval evidence | `SelfEvolutionDecision` (unbound string) | `DurableInoContracts` (principal+hash+single-use) | `core:PROD-001`, `kernel-runtime:SEC-050` |
| Code execution | 4 overlapping mechanisms, inconsistent gating | one gated, out-of-process path | `foundry:ARCH-300` |
| Model selection | test-only second authority | production registry | `core:ARCH-005`, `kernel-runtime` |
| Widget dispatch | `ui_registry.dart` vs `UiSurfaceTreeRenderer.build` | one renderer | `flutter-ui:ARCH-801` |
| OAuth state machine | duplicated Google vs Salesforce | one shared flow | `connectors:ARCH-403` |

## Oversized / god files (split)

- `core` `Synapse.cs` — ~40 unrelated contracts in one file (`core:ARCH-004`). Split by domain (identity, connectors, self-evolution, foundry, UI). This also naturally quarantines the dead vocabulary for deletion.
- `flutter-ui` `digital_brain_rfw_library.dart` — required two review passes; already partially split (P1.14) but still bulky. Continue the split; delete the dead demo widgets.

## Boundary violations (fix)

1. **Provider names in the kernel** (`connectors:ARCH-401`, `core:ARCH-003`, `kernel-hosting:ARCH-102/201`) — Google/Salesforce hardcoded in orchestration, grants, and OAuth endpoints. Replace with the capability registry ([04](04-connectors-and-auth.md)).
2. **Crypto/rate-limiter in the "shared" Core layer** (`core:ARCH-001`) — move into the kernel/TCB.
3. **Login handler triggers Aspire orchestration** (`kernel-hosting:ARCH-103`) — a session event should not start distributed-app orchestration; decouple.
4. **SDK leaks app internals** (`flutter-sdk-and-tests:ARCH-901`) — hardcoded `GlowIcon`, app render tuning inside the SDK package; the closure-adapter *pattern* is clean but the boundary leaks in reverse.
5. **MCP write-only surface** (`mcp-hosts-build:ARCH-501`) — a machine client cannot observe an operation outcome; asymmetric boundary.

## Misleading code / comments (correctness of the record)

- `CapabilityGate` comment is stale and self-contradicting while the code disclaims itself as a boundary (`foundry:CLEAN-158/300`).
- `GeneratedNeuron` presents **fabricated Gmail sample data as "analyzed locally"** (`kernel-runtime:PROD-100`) — actively misleading; remove or label as demo.
- `PerfProbe.rebuildsPerSecond` reports frames/sec, not rebuilds (`flutter-sdk-and-tests:PROD-900`) — mislabeled telemetry.
- Vacuous `/// <summary>` blocks (`foundry:CLEAN-303`) violate the repo's own CLAUDE.md rule.
- Stale comments referencing deleted screens (`flutter-runtime:CLEAN-704`), the dead CLAUDE.md MCP standalone-run instruction (`mcp-hosts-build:CLEAN-501`).

## Refactoring candidates (after deletion, not before)

- Extract the shared connector host (rate limit/backoff/pagination/minimization) once provider neurons collapse into capability implementations ([04](04-connectors-and-auth.md)).
- Unify the four code-execution mechanisms into one gated, out-of-process path ([05](05-self-evolution.md)).
- Deduplicate `FindStagedAsync`/`Failed` across the two apply handlers (`foundry:CLEAN-301`).
- Consolidate Aspire-detection logic reading raw env vars (`kernel-hosting:CLEAN-204`).

## Net effect and the >10% target

The repository's method targets >10% net reduction. Between the ~30 dead Core types, the ~1,100-line dead kernel stratum, the ~3k-line orphaned Flutter v1 rail, the dead proto + stubs, the dead visual/asset code, the second auth authority, and the stale docs, **the deletable surface comfortably exceeds 10%** — and every deletion here also *removes a security or reliability liability*, not just lines. Delete-first is not cleanup around the edges; it is the first phase of the remediation ([12](12-implementation-plan.md)).
