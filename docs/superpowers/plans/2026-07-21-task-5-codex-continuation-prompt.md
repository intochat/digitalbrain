# Codex continuation prompt — Task 5

Use the following prompt in a new Codex session.

---

Continue the DigitalBrain Foundation PoC on `master` from the repository root.

Read `AGENTS.md`, `CLAUDE.md`, `APPROVED-ARCHITECTURE-DECISIONS.md`,
`REFINED-ARCHITECTURE-AND-NEXT-STEPS.md`, and
`docs/superpowers/plans/2026-07-20-foundation-poc.md` completely before editing.

## Starting state

The baseline when this prompt was written was:

```text
HEAD 96742d5fa50ba7c1b4a869163ba4d6e4244ea9e4
```

Expected uncommitted files:

- `APPROVED-ARCHITECTURE-DECISIONS.md`
- `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md`
- `docs/superpowers/plans/2026-07-20-foundation-poc.md`
- `tests/DigitalBrain.Simulations/AIWorkerContracts.cs`
- this continuation prompt

Ignored evidence:

- `.superpowers/sdd/task-5-report.md`

Record `git rev-parse HEAD` and `git status --short` first. Do not reset, discard, or overwrite the
existing work. If HEAD or unrelated files changed, identify the change and stop rather than sweeping
it into a commit.

Work directly on `master`, as previously requested. Do not create a worktree. Use `apply_patch` for
file edits. Do not stage or commit until the current diff has been reconciled and the required gate
is green.

## Ratified architecture boundary

Decision D4.8 is approved and recorded in `APPROVED-ARCHITECTURE-DECISIONS.md`:

1. Permit one narrowly public, non-semantic Kernel runtime seam:
   `DigitalBrain.Kernel.CapabilityDelegation`.
2. The type is public only because `DigitalBrain.Modules.AI` must carry it across an assembly
   boundary. It must be sealed, opaque, non-constructible by consumers, hidden from IntelliSense,
   absent from `DigitalBrain.Abstractions` and every contracts package, and excluded from semantic
   discovery, registries, and behavior references.
3. Kernel exclusively mints, carries, validates, redeems, and records outcomes for it.
4. The delegation binds only generic causal and transport facts:
   - the already committed `CapabilityRequested` delivery;
   - causal caller neuron;
   - actual delegated runner `GrainId`;
   - owner;
   - exact target;
   - contract and method;
   - correlation and causation;
   - opaque one-use identity.
5. The delegation contains no `RunId`, `AttemptId`, `AttemptCursor`, Task revision, definition
   fingerprint, checkpoint identity, MAF state, AI state, approval state, or integration command
   state.
6. AI validates `ActiveRun`, `RunId`, Attempt revision, fingerprint, and checkpoint lineage before
   minting and when accepting results. Integration modules own approval, `CommandId`, and uncertain
   external-effect reconciliation.
7. The cross-grain consume/invoke boundary is not an exactly-once transaction. A crash may require a
   newly journaled request and fresh delegation.
8. This is one concrete infrastructure seam, not a public interface or service hierarchy.

Forbidden mechanisms remain:

- broad `InternalsVisibleTo` access;
- a public or forgeable raw `RequestContext` convention;
- a proxy neuron or semantic identity for the runner;
- a global delegation manager or registry;
- lease, generation, renewal, hierarchy, tier, routing, or balancing abstractions;
- Kernel dependencies on AI, Tasks, MAF, checkpoints, approvals, or integrations.

Always distinguish:

- `CausalCaller`: the GroupChat neuron whose outgoing journal owns the request.
- `DelegateSource`: the private runner GrainId actually observed by the Kernel filters.

Every off-turn typed participant or integration call requires its own exact precommitted request and
delegation. The initiating Task-to-worker request is not sufficient for later runner-to-`ILLM`,
runner-to-`IAgent`, or runner-to-integration calls.

## Required workflow

Use grilling before each slice and when reviewing each diff:

- State the recommendation.
- State the strongest argument against it.
- Defend it with a failing proof or fold.
- Reject additions with no current Foundation consumer.
- Keep the root gate green.

Use test-driven development. Write the desired proof first and observe it fail. If needed, retain it
temporarily as an explicit xUnit proof so the root gate is never red. Implement the smallest passing
change, refactor only after green, then run the exact unfiltered root gate.

## Slice 0 — reconcile the canonical documents

Do not reopen D4.8. Reconcile the remaining documents with it:

- Replace the impossible `internal CapabilityDelegation` requirement with the ratified opaque-public
  Kernel transport decision.
- Keep the type explicitly non-semantic.
- Remove `RunId` and Task revision from Kernel delegation language.
- Expand the Task 5 file list to name the actual Kernel filters, Neuron mechanics, token, API
  baseline, and security simulations.
- Remove the stale Task 6 `CapabilityInvocationLease.cs` item. Task 6 must reuse the Task 5 Kernel
  seam.
- Clarify that direct `RespondAsync` owns its outer `AgentSession`, while supervised work owns raw
  MAF checkpoint lineage.
- Clarify that each off-turn participant or integration call needs its own delegation.
- Move durable evidence from `.superpowers/sdd/task-5-report.md` into the canonical decision/plan,
  but do not commit the temporary report.
- Do not claim scripting exclusion has been executed if the behavior compiler does not exist yet;
  prove current contract and registry exclusion, and retain contract-only compilation as the future
  invariant.

Run `git diff --check` and grill the documentation diff before committing the documentation boundary.

## Slice 1 — close the default-allow Kernel hole

The existing characterization proves a real Kernel defect:

- `OutgoingReificationFilter` invokes directly when the source is not a `Neuron`.
- `IncomingReificationFilter` invokes directly when no capability context exists.
- A same-owner non-neuron grain can therefore enter semantic capability code without a committed
  incoming request.

The current test detects that defect inside `Echo.PokeAsync`; that is insufficient. Change the proof
so Kernel rejects the raw call before the target method body executes. Instrument the target and
assert zero semantic entry or side effect. Do not use the target's self-check exception as the
security boundary.

Harden capability invocation to be default-deny unless:

1. the actual caller is a `Neuron` with a normal reified request; or
2. the invocation carries a valid Kernel delegation.

Preserve legitimate `[ClientEntryPoint]` behavior deliberately and prove the intended exemption.

## Slice 2 — prove and implement the minimal delegation rail

Write executable desired-behavior proofs for:

- a valid delegated call;
- caller outgoing `CapabilityRequested` committed before execution;
- target incoming journal containing the same `SynapseId`, correlation, and causation before method
  entry;
- target execution under the original causal delivery;
- successful invocation recording exactly one `CapabilityCompleted`;
- legitimate target failure recording exactly one `CapabilityFailed`;
- wrong actual runner source rejected before method entry;
- wrong owner rejected;
- wrong target rejected;
- wrong contract or method rejected;
- forged raw `RequestContext` rejected;
- replay rejected;
- replay still rejected after deactivation and restart;
- delegation consumption durably committed before semantic invocation.

Unauthorized attempts must not consume a valid token or create contradictory terminal outcomes.

Implement only enough Kernel machinery to satisfy those proofs. Use this shape unless a compiler- or
test-backed fact demonstrates a smaller correct one:

- one sealed opaque `CapabilityDelegation` transport type;
- protected minting from `Neuron` that commits the outgoing request and issued-delegation state;
- private `CapabilityRequestContext` carrying;
- existing `DigitalBrainRuntime` only if a cross-assembly invocation helper is required;
- private/internal Kernel callback to the causal caller for durable redemption and outcome recording;
- exact actual-source, target, contract, and method validation in both filters;
- caller-neuron-owned issued/consumed state, not a global manager.

Do not claim atomicity across the causal caller and target grains. Explicitly prove and document the
consume-before-target-commit crash behavior. A fresh request/delegation is recovery; it is not
exactly-once retry.

Add API and architecture proofs that:

- no contracts package exposes or references `CapabilityDelegation`;
- generated semantic registries ignore it;
- it has no public constructor or readable payload;
- the public API baseline contains only the deliberately approved Kernel surface;
- no friend assembly, public raw context, proxy neuron, or public service hierarchy was added.

Run focused tests during red-green work. Before claiming the slice complete, run:

```powershell
dotnet test --logger "console;verbosity=minimal"
git diff --check
git status --short
```

Commit only at a green boundary and include the three `CLAUDE.md` diff-grill answers in the commit
message.

## Slice 3 onward — resume Task 5 in small green commits

Implement in this order:

1. deterministic `CreateMessages(Goal)` and `CreateResult(messages)` hooks;
2. `AIWorkerState` with at most one `ActiveRun`;
3. private AI `WorkflowRun` containing `RunId`, full `AttemptCursor`, definition fingerprint, input
   checkpoint, and `RecoverAfter`;
4. durable Orleans-backed MAF checkpoint storage with stable Worker + Task + Attempt identity;
5. private non-neuron `WorkflowRunner`;
6. exactly one Lockstep superstep per run;
7. delegated typed participant calls through the proven Kernel seam;
8. checkpoint adoption and stale, duplicate, cancelled, and late-result fencing;
9. cancellation and recovery with a fresh `RunId` but the same checkpoint lineage;
10. typed waiting and terminal Task fact mapping;
11. exclusion between direct-session and supervised-run entry paths;
12. hosted restart proof.

For MAF APIs, follow `AGENTS.md`: use Context7 when available, then verify the pinned package surface
with the compiler. The compiler and executable tests are the final oracles.

At every green boundary:

1. grill the requirement and diff;
2. run the owning tests;
3. run the exact unfiltered root gate;
4. inspect `git diff --check` and `git status --short`;
5. verify HEAD and unrelated files did not move;
6. commit the coherent slice;
7. continue autonomously unless a documented stop condition is reached.

## Hard stops

Stop with a green root and report the exact failing proof if:

- Kernel learns AI, Tasks, MAF, `RunId`, checkpoints, approvals, or integration semantics;
- `CapabilityDelegation` enters Abstractions, contracts, semantic discovery, registry, or behavior
  references;
- raw `RequestContext` becomes public or forgeable;
- broad friend access is added;
- the runner becomes a `Neuron`;
- a lease, manager, registry, renewal, or generation framework appears;
- a new abstraction has no current consumer;
- semantic capability code can execute without its incoming request already committed;
- MAF cannot resume one Lockstep superstep without repeating a completed executor;
- the implementation begins expanding beyond Tasks, AI, Google, Salesforce, Time, and the hosted
  Foundation PoC.

When stopped, preserve all evidence, keep the root gate green, and request only the smallest
architecture decision needed to continue.
