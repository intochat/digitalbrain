# 05 — Self-Evolution Governance

The project's north star is that safe, journaled, human-approved self-evolution is the *only* path to user-visible mutation. This chapter traces every current self-modification path, identifies where governance is asserted but not enforced, and defines the target rail and per-risk-tier policies. Evidence: [file-audit/kernel-runtime.md](file-audit/kernel-runtime.md), [file-audit/foundry.md](file-audit/foundry.md), [file-audit/core.md](file-audit/core.md).

## Current self-modification paths

| Path | Mechanism | Governed? |
|---|---|---|
| Prompt / wiring proposals | `MetaOptimizerNeuron` emits `WiringOptimizationProposed` | Not routed through the rail; informational only. |
| Automation define/remove | `SelfEvolutionApplyVia.Automation*` → `AutomationDefinition/RemovalApplyHandler` | Through the rail — but rail unenforced (below); script gate is a no-op (`foundry:SEC-301`). |
| Foundry **Run** (in-process code) | `FoundryRun` → `FoundryRunApplyHandler` → `CodeRunNeuron` → `InProcessAlcExecutor` | Through the rail *or* direct grain fire (`foundry:SEC-304`); executes in-process full-trust (`foundry:SEC-302`). |
| Foundry **Deploy** (compiled module + restart) | `FoundryDeploy` → `FoundryDeployApplyHandler` → `CodeDeployNeuron` | Through the rail *or* direct; **no capability gate at all** (`foundry:SEC-308`). |
| Pack install / embodiment | `GeneratedPackRuntime` / `PackAlcEmbodier` | **No signature/publisher verification** (`connectors:SEC-405`). |
| Kernel self-update | `AspireOrchestratorNeuron.PerformKernelSelfUpdate` | **Simulated** — logs only (`kernel-runtime:ARCH-050`). |
| `TrustedAutoApply` bypass | Config flag on `CodeFoundryClosedLoopNeuron` | Skips human approval entirely; emits a journaled `AuditBypass` (`foundry:SEC-303`). |

## Is there one authoritative rail?

**Structurally yes, substantively no.** `SelfEvolutionNeuron` is a single grain that stages proposals and applies only approved ones via an allowlisted, fail-closed `SelfEvolutionApplyRegistry` — a genuinely good spine. But the rail is **not the sole choke point** and it **does not enforce its own governance contract**.

## Governance gaps (the rail is a convention, not an enforced boundary)

1. **Unauthenticated approver** (`kernel-runtime:SEC-050`, Critical). `HandleAsync(SelfEvolutionDecision)` checks only that `DecidedBy` is a non-empty string. There is no verification that the decider is an authorized human principal. Anyone able to deliver a decision synapse approves any pending proposal.
2. **Approval not bound to content** (`core:PROD-001`, High). `SelfEvolutionDecision` references only `ProposalId` — there is no content hash of the approved change. Nothing guarantees the applied artifact is the one a human reviewed ("approve-what-you-saw" is unprovable at the type level).
3. **`RequiresHumanApproval` is never read** (`kernel-runtime:SEC-051`, High). The proposer sets the flag; the grain ignores it. There is no code path that treats "requires human approval" differently.
4. **Proposer-supplied risk is trusted** (`kernel-runtime:SEC-052`, High). The apply-risk classification comes from the proposer, not an independent classifier — a proposal can under-declare its own risk.
5. **Execution can bypass the rail** (`foundry:SEC-304`, Critical). `CodeRunNeuron`/`CodeDeployNeuron` are public grains; a cluster-internal caller can fire execution directly with no proposal.
6. **The validation gate is non-functional where it matters** — bypassable by reflection (`foundry:SEC-300`), a no-op for scripts against a zero-reference compilation (`foundry:SEC-301`), and absent on Deploy (`foundry:SEC-308`).
7. **No real rollback** (`kernel-runtime:ARCH-051/REL-103`). Checkpoint/restore appends rather than restores; a failed apply is terminal and non-retriable, potentially leaving partial side effects.
8. **No verify phase** (`dotnet-tests:TEST-608`). There is no production step that verifies an applied change did what the proposal claimed before marking success.
9. **Packs unsigned** (`connectors:SEC-405`). The pack path — a first-class self-evolution vector — has no integrity/trust enforcement.
10. **The rail is outside the TCB and has no tenant boundary** (`kernel-runtime:SEC-056`). The mutation authority is an ordinary grain.

**The decisive observation:** the INO effect rail (`DurableInoContracts`, `InoEffectPlanAuthority`) *already models the missing evidence correctly* — principal-bound, content-hashed, single-use, lease-fenced, outcome-unknown-aware. The fix is not invention; it is **making self-evolution reuse the INO evidence model**.

## Target evolution rail

```
intent
 → proposed change            [bounded, with the EXACT artifact]
 → bounded human-readable diff [what changes, in plain language]
 → risk classification        [INDEPENDENT classifier, not proposer-supplied]
 → deterministic validation   [gate that actually binds symbols; escape tests; per-tier checks]
 → human approval             [principal-bound, content-hashed, single-use decision — copy DurableInoContracts]
 → authenticated artifact     [signed pack / verified source hash matches approved hash]
 → isolated application        [out-of-process/WASM for code; TCB untouched]
 → durable journal            [bounded, tamper-evident, one record per state transition]
 → verification                [post-apply check the change did what was claimed]
 → rollback capability         [real state restore; retriable idempotent apply]
 → observable result           [surfaced to the user]
```

Every arrow that is currently missing or fake is cited above. The rail must also be the **only** way to reach code execution, pack embodiment, automation definition, and non-TCB neuron change — enforced by making those targets reachable *only* from the apply registry, not as public grains.

## Per-risk-tier policies (proposal)

The existing `SelfEvolutionRisk` enum (`None` / `InProcessCode` / `KernelRestart`) is the right idea but under-specified. Target tiers and their required controls:

| Tier | Examples | Validation | Approval | Isolation | Rollback |
|---|---|---|---|---|---|
| **T0 — Prompt/config** | prompt tweaks, non-security config defaults | schema/lint check; diff | 1 human approver (principal-bound) | none needed | revert value from journal |
| **T1 — Automation/workflow** | define/remove reaction, workflow edits | **working** capability gate + dry-run; no network/host by default | 1 human approver; content hash bound | script runs in restricted interpreter (no ambient capabilities) | remove reaction; state snapshot |
| **T2 — Connector action** | a new mutation via existing connector | connector capability manifest check; preview | rides the INO effect rail (already correct) | connector host wrapper | connector preview/verify/undo |
| **T3 — New interpreted behavior (pack)** | signed behavior pack | signature + publisher trust **enforced**; capability manifest | human approver; hash-bound to signed artifact | out-of-process/WASM | uninstall + state restore |
| **T4 — New compiled code (Foundry)** | generated C# run/deploy | binding gate + escape tests; **Deploy must gate too** | human approver; hash-bound; independent risk class | **out-of-process sandbox with timeout + resource caps** (never in-process) | checkpoint restore that actually restores |
| **T5 — Kernel/TCB change** | identity, crypto, rail, sandbox, host | **not self-modifiable** | out-of-band human change control (PR + deploy), never the rail | full deploy rollback |

**Key rules:** risk is classified by the kernel, not the proposer (fixes `SEC-052`); T4 never uses in-process execution (fixes `SEC-302`); T5 is outside the rail entirely (fixes the boundary inversion); `TrustedAutoApply` is removed or restricted to T0/T1 with mandatory journaling and a hard config guard.

## What self-evolution must never touch

Per [03](03-operating-system-assessment.md): identity/tenancy, cryptography, the decision verifier, the sandbox boundary, the apply registry, and the TCB's own code. These change only through the human-operated deploy pipeline, never through the rail. This is the line that separates "governed self-improvement" from "uncontrolled autonomous mutation."

## Immediate posture (until the rail is hardened)

Because the controls are absent rather than merely weak, the safe interim posture is: **Foundry Run/Deploy and pack embodiment disabled by default** (a single fail-closed config gate), automation scripts restricted to a no-ambient-capability interpreter, and `TrustedAutoApply` forced off. This makes the MLP ([01](01-product-north-star.md)) shippable on the strong INO substrate while the rail is lifted onto the INO evidence model.
