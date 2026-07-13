# 07 — Performance and Reliability

Findings, evidence, scaling risks, and a benchmark plan. **Measured vs. inferred is kept distinct.** The only runtime measurement taken this pass is the test suite; everything else is static inference and is labeled so. Evidence: per-subsystem [file-audit/](file-audit/) docs.

## Measured evidence (this pass)

- **Full `dotnet test` from repo root:** 471 tests pass, 0 fail, 0 skip, ~30 s wall clock (TestKit.Tests 3, Salesforce.Tests 54, DigitalBrain.Tests 414). Target framework `net11.0`. This is the only executed measurement; no load or latency benchmarks were run (the assessment is documentation-only and did not stand up infrastructure).

Everything below is **inference from code**, to be confirmed by the benchmark plan.

## Reliability findings (highest impact first)

| Ref | Sev | Issue | Consequence |
|---|---|---|---|
| `kernel-runtime:ARCH-051` | High | Checkpoint/restore appends snapshot into journal instead of restoring state | Rollback grows state rather than reverting — recovery is unsound. |
| `kernel-runtime:REL-101` | High | Checkpoints embed the *entire timeline* into the journal | Superlinear journal growth per checkpoint. |
| `kernel-runtime:REL-100` | High | `FireAsync` is at-most-once (journaled but possibly undelivered; no outbox) | Messages can be journaled yet never delivered — undermines "durable/replayable." |
| `kernel-runtime:REL-103`/`REL-051` | High | Failed/partial self-evolution apply is terminal, non-retriable; approved-but-unapplied after crash between `DecisionRecorded` and `ApplyResult` | Governed mutations wedge half-done with no recovery. |
| `core:REL-001` | High | Synapses mint `Guid.NewGuid()`+`UtcNow` at construction; ~40 records use implicit positional Orleans `[Id]` | Replay is non-deterministic; a parameter reorder silently corrupts journal/wire compat. |
| `kernel-hosting:REL-201` | Med | `PackConfigStore` silently drops undecryptable values and rewrites the whole dict with no ETag | A transient decrypt failure can become permanent connector-credential loss. |
| `kernel-hosting:REL-101`/`FRAME-200` | Med | Orleans providers configured twice (Aspire keyed + manual); managed-identity branch may throw at boot | Rolling-deploy / managed-identity boot risk — needs a deployment boot test. |
| `mcp-hosts-build:REL-500` | Med | `/health` always returns Healthy (resolves DI only) | ACA readiness/smoke tests pass while Orleans connectivity is down. |
| `flutter-runtime:REL-700` | Med | Client reconnect loop can skip backoff and spin at zero delay | Reconnect storm against the single-replica edge. |

**Strong counterpoint (keep):** the INO `InoOperationWorkerGrain` lease/fence/outcome-unknown machine and `EncryptedPersistentState`/`PersistedStateReconciliation` are correctly built for partial-failure recovery and are well-tested (`dotnet-tests` encrypted-state coverage). The reliability problem is confined to the neuron/self-evolution substrate.

## Performance findings

| Ref | Sev | Issue |
|---|---|---|
| `kernel-runtime:PERF-100` | High | Neuron journals unbounded; every message rebuilds an O(journal) projection. Cost grows with grain lifetime. |
| `mcp-hosts-build:PERF-500` | Med | Surface feed is a **250 ms poll per client** with 2–4 grain calls per delivered item. |
| `mcp-hosts-build:PERF-501` | Med | Long-lived streams share the **32-slot edge semaphore** with all unary traffic; ~32 open tabs starve the single-replica edge. |
| `mcp-hosts-build:PROD-501` | Med | Single-replica edge is a pinned SPOF. |
| `foundry:REL-300` | High | In-process executor has no timeout/cancellation/resource cap. |
| `flutter-ui:PERF-800`/`FRAME-800` | Med | `GlassMaterial` runs a per-frame hover ticker + async shader load that are permanently dead (`_shadersEnabled => false`), on a hot path wrapping most overlays. |
| `flutter-sdk-and-tests:CLEAN-900` | High | 13.3 MB Lottie + demo assets bundled into every build (startup/download cost). |
| `kernel-hosting:PERF-100` | Med | `TabularDataParser` zip-bomb exposure (dead code, but ships). |

## Scaling risks (inference)

1. **Journal growth is the dominant scaling risk.** Unbounded journals + O(journal) projection rebuild per message + full-timeline checkpoints mean long-lived, active grains degrade over time and inflate storage/serialization cost. This caps the lifetime and throughput of any hot neuron. **This is the first thing to benchmark.**
2. **Edge is a single-replica SPOF with poll-based fan-out.** The 250 ms poll × 2–4 grain calls × shared 32-slot semaphore means concurrent-client scaling is bounded well below what Orleans could sustain, and the edge cannot be scaled out until session/feed state is replica-safe and the Orleans double-config boot issue is resolved.
3. **Serialization/encryption overhead** on the INO encrypted-state path is per-operation but bounded; not a top risk, but should be measured.
4. **Model invocation frequency** — `MetaOptimizerNeuron` calls the LLM every 5th telemetry event; Foundry calls per request. Not hot today, but unbounded if wired to high-frequency triggers.
5. **Multi-replica correctness is unproven** — the rolling self-update is simulated (`kernel-runtime:ARCH-050`) and the Orleans provider double-config may throw under managed identity (`kernel-hosting:FRAME-200`). Horizontal scale is not yet demonstrated.

## Cold start / boot

- Preview/alpha base image + Orleans clustering + Azure Storage dependencies; boot time not measured. The `UiTransportHealthCheck` eagerly resolves the transport graph (reasonable), but `/health` overclaims (`mcp-hosts-build:REL-500`), so boot/readiness signals are unreliable for orchestration.

## Flutter client performance

- The v2 rail is efficient (in-memory tokens, bounded payloads, profiler disabled). Waste is concentrated in dead visual machinery (`GlassMaterial` shader path, `palette_primitives` Lottie/earth-globe ~810 dead lines — `flutter-ui:ARCH-800`) and the 13.3 MB bundled asset. Deleting these improves startup and memory with zero functional loss.

## Benchmark and load-test plan (recommended; none exist today)

Priority order, each with a concrete target to make "measured" replace "inferred":

1. **Journal growth microbenchmark** (highest value). Drive a single neuron through N messages (N = 1e3…1e6); measure per-message latency, activation-load time, and storage size; confirm/deny the O(journal) projection cost. **Target:** flat per-message cost after compaction is added; define a max journal size + compaction trigger.
2. **Checkpoint cost.** Measure checkpoint latency and journal-size delta vs timeline length. **Target:** checkpoint cost independent of timeline length (requires the ARCH-051/REL-101 fix).
3. **Edge concurrency load test.** Ramp concurrent `WatchSurfaceFeed` clients; measure semaphore saturation, poll amplification, p50/p95/p99 surface-delivery latency, and the client count at which the single replica saturates. **Target:** define a supported concurrent-client number per replica; establish the path to multi-replica.
4. **Connector throughput + rate-limit behavior.** Drive Gmail/Salesforce read/mutation under provider rate limits; verify backoff/pagination and the preview→apply→verify latency budget.
5. **Sandbox execution cost** (after out-of-process is wired). Measure per-execution overhead (process spawn) and confirm timeout/resource caps fire. **Target:** bounded execution time; caps enforced.
6. **Multi-replica correctness + rolling deploy.** Stand up ≥2 replicas; verify session/feed correctness, no cross-replica state loss, and a real drain/verify rolling update (replacing the simulation). **Target:** zero-downtime deploy demonstrated.
7. **Cold-start budget.** Measure boot to ready; fix `/health` to reflect real Orleans connectivity first.

## Capacity targets (to be set after benchmarking)

The repo states no measurable capacity target. Recommend adopting explicit ones after step 3/6: supported concurrent users per replica, max neurons per silo, journal-size ceiling per grain, and a p95 surface-delivery latency SLO. Do not claim scale until these are measured.
