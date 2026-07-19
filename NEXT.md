# NEXT — implementation handoff

Paste this whole file into a fresh session at the repository root, branch `master`.
Delete it once its plan is consumed. It is scaffolding, not documentation.

---

## The goal

**Build a brain that programs itself.**

Not a framework with a scripting feature attached. The framework exists to carry exactly one
capability, and every decision resolves in its favour:

> A person or a model writes ordinary C#, proposes it, a human approves it, and it becomes a living
> part of a running system — durably, observably, reversibly.

Neurons and synapses are the substrate that makes that safe. A neuron is durable and owner-scoped so
a behaviour survives restarts and cannot reach across owners. A synapse is a fact on a journaled rail
so nothing a behaviour does is invisible. The rail is not bookkeeping — **it is what makes letting a
model write live code an acceptable risk.**

### Done is this sequence, demonstrated end to end

1. A model receives a plain-English intent plus the vocabulary of installed modules.
2. It emits **one C# file** using only the client API — `Connect`, `Get<T>`, `On<T>`, `Emit`.
3. The file compiles against the contracts packages it is permitted to reference, and **fails loudly**
   if it names anything else.
4. Installing it runs a **recording pass**: `On<T>` and `Get<T>` record instead of acting. The output
   is a manifest — facts handled, capabilities requested, facts emitted.
5. A human approves that manifest. The approval is a journal entry.
6. The behaviour goes live with **no rebuild and no silo restart**.
7. A fact arrives, the behaviour runs, and its typed requests appear on the rail **without the
   behaviour cooperating**.
8. Reverting the journal entry rolls the install back and the behaviour stops.

When that runs, the product exists. Everything in the phase plan is a prerequisite for it.

---

## Read these first, in this order

1. **`CLAUDE.md`** — the way of working. Non-negotiable. Written to be followable with only a shell.
2. **`ARCHITECTURE-REVIEW.md` §3** — ratified decisions, DEC-1 through DEC-10.
3. **`ARCHITECTURE-REVIEW.md` §9** — the ordered plan. This is what you execute.
4. **`ARCHITECTURE-REVIEW.md` §12** — what is rejected and why. Do not rebuild any of it.
5. **`website/architecture.md`** — the same design in prose, marked built versus designed.

**Do not re-litigate §3.** Every decision there was argued against its alternatives with evidence
from six prior generations of this system. Do not silently discard it either: §1 exists *because* a
prior plan inherited conclusions without re-deriving them, and that is the failure this repository is
organised against. If you believe a decision is wrong, say so explicitly, argue it, and record the
reversal — never route around it.

---

## Four agents, four roles

The critical path is **serial** — Phase 2b's steps all touch `Neuron.cs`. Four parallel implementers
would conflict on every file. So this is one builder and three standing adversaries, and the three
exist because each maps to a failure this repository has actually suffered.

| Agent | Owns | Exists because |
|---|---|---|
| **Builder** | The critical path, one slice at a time, TDD | The work |
| **Refuter** | Attacks every claim the Builder makes | Claims have shipped here that no command ever checked |
| **Deleter** | Elon steps 2 and 3 on every diff | Six prior generations accreted abstractions with zero readers |
| **Guardian** | Fidelity to §3 and §12 | §1: silent drift from a prior plan is *the* documented failure mode |

### The loop, per slice

```
Builder    writes the failing proof, then the code, then runs the owning project
           ↓
Refuter + Deleter + Guardian run in PARALLEL on the diff
           ↓
Builder    resolves every finding, or argues it down in writing
           ↓
Root gate: dotnet test --logger "console;verbosity=minimal"   (never --filter)
           ↓
Commit, with the three diff-grill answers in the message
```

**Role prompts, in short:**

- **Refuter** — *"Try to prove this claim false. Default to refuted when uncertain. A test that passes
  is not evidence the behaviour is right; find the input that breaks it."*
- **Deleter** — *"What in this diff has no consumer today? What complexity moved rather than reduced?
  Propose the deletion, not a refactor."*
- **Guardian** — *"Does this contradict any of DEC-1 through DEC-10, or rebuild anything in §12? Quote
  the decision and the code side by side."*

Give any fanned-out agent **a scoring rule** for what counts as a finding. Without one they return
summaries; with one they return findings. "Changes a decision that is currently open" found four
factual errors in the plan of record where "find valuable content" would have found nothing.

### What actually parallelises

While the critical path is serial, these are independent and can run alongside it:

- **The generation benchmark** (Phase 4.8). Needs nothing from Phases 2 or 3. See the warning below.
- **Held-red proofs for later phases** — write the scenario, tag it, leave it excluded.
- **Documentation** for what has already landed.

---

## The plan, in execution order

Phases 0, 1, 2.1 and 2.2 are **done and committed**. Start at 2.3.

| Step | What |
|---|---|
| 2.3 | Cursor-based read replacing `ReadJournalAsync` across every consumer |
| 2.4 | **DEC-10** — synapse becomes a thin record; metadata moves to a kernel-owned envelope. Absorbs D-3. **Phase 2a ends green here — this is the rollback point** |
| 2.5 | **R-3, the owner boundary.** Blocks everything client-facing |
| 2.6 | `WatchAsync` on `INeuron`, and `FeedNeuron` as an ordinary neuron |
| 2.7 | Call-filter reification — the bridge that puts typed requests on the rail |
| 2.8 | R-4 and broadcast addressing as **one** change: composition-time, type-level, instance from correlation |
| 2.9 | R-2, per-receiver outbox progress. **State the ordering guarantee first** |
| 2.10 | D-6 — delete the sample's activation workaround and polling loop |

Then Phase 3 (modules), Phase 4 (the scripting rail plus its approval gate), Phase 5 (UI).

**2.5 is the one to respect.** An external script is exactly the unattributed Orleans client of §2.6,
which is the configuration this lineage records as its own failure mode. Nothing client-facing ships
before it.

---

## Verified true — do not re-derive

- **Root gate baseline: 119 passed, 2 held red.** `dotnet test --logger "console;verbosity=minimal"`.
- **`INeuron` has three methods**, none behaviour-specific. Every neuron is already wire-identical,
  which is why `IBehavior` needs no new wire contract.
- **`NeuronId.ToGrainId()` is `GrainId.Create(Type, GrainKey)`** where `Type` is a string Orleans
  resolves against a manifest fixed at silo startup. This is why behaviours add no grain type.
- **Observation already exists in-process.** `src/DigitalBrain.Testing/SynapseObserver.cs` is an
  `ActivityListener` over `SynapseTelemetry`, push-based, no polling. What is missing is a durable,
  per-identity, catch-up-after-disconnect feed.
- **R-1's blast radius is ~20 call sites**, not 26 scenarios. Scenarios reach the journal through a
  shared step layer: `NeuronSteps` has 5 call sites, `Simulation` has 6.
- **Held-red proofs behave differently by kind.** `<proj>.exe -explicit only` **runs** xUnit
  `[Fact(Explicit = true)]` proofs and they fail. It reports `@ignore` Gherkin scenarios as *not run*
  and never executes their bodies — those need `-failSkips`, which reports them failed for being
  skipped. Verified by running both.
- **The source generator string-matches interface names.** `DispatchManifestGenerator` holds
  `DigitalBrain.Abstractions.IHandle<TSynapse>` as a constant. Rename or move those interfaces and it
  silently emits an empty manifest while reflection still finds the handlers.
  `DispatchManifestContracts` is what catches that divergence. Do not delete it.

## Environment realities

- **Context7 and codegraph have both been unavailable here** — quota exhausted, and `npx` exiting
  9009. Say so and fall back; do not skip silently. `CLAUDE.md` §4 lists the oracles.
- **Use the compiler as the API oracle.** Write a throwaway file referencing the API and build it. No
  `CS0246` proves the type exists. This is how `IDurableValue<T>` was confirmed in the undocumented
  `Microsoft.Orleans.Journaling`.
- **Microsoft Learn returns the older `Orleans.EventSourcing.JournaledGrain`** for journaling queries.
  Different API. Do not conflate.
- **Website gates run `node` directly, not npm** — npm's children lose the nodejs PATH here.
- **This repository has been modified mid-session by other tools, twice.** Record
  `git rev-parse HEAD` and `git status --porcelain` at start and check both before staging. If
  something moved that you did not move, surface it and stop.

---

## The one assumption that can invalidate the goal

**Nobody has measured whether a model can reliably emit these behaviour scripts.** The entire
scripting rail rests on it.

A prior generation in this lineage gated a language on a twenty-prompt benchmark, scored it at 60%
against an 80% bar, and formally demoted the language. Its own documents record that the score came
from **a deterministic stub rather than a model**, and that the interpreter shipped afterward anyway.

So, before the rail is load-bearing:

1. Write ~20 realistic intents and the vocabulary they may use.
2. **Commit the pass threshold in writing before looking at any result.**
3. Run them against a real model. A stub does not count.
4. Score: does it compile against only the permitted contracts, and does it do what the intent asked?
5. If it fails the bar, **enforce that against the codebase**, not just the specification.

This costs about a day, needs nothing from Phases 2 or 3, and tests the assumption the whole product
depends on. Consider doing it first.

---

## Rules of engagement

- **Root gate every phase, never `--filter`.** Background it and poll. A project-scoped run has
  already missed a failing contract the root run caught.
- **Never a red root gate.** Held-red proofs are excluded and listed on `website/status.md`.
- Adding a public type means updating `PublicAPI.Unshipped.txt`. Adding an `[Alias]` means updating
  the pinned-alias contract. Both fail the build if you forget, which is correct.
- **No comments** as narrative, boilerplate, or commented-out code. Names, types and tests carry
  meaning. `[Fact(DisplayName = "...")]` makes a test self-describing.
- **Report at phase boundaries**, not per slice. Say what failed, with the output.
- **Evidence precedes assertion.** "Tests pass" is not a claim you may make without the output in
  front of you.
