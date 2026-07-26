# Repo cleanup to framework shape — ratified design

**Date:** 2026-07-27
**Baseline HEAD:** `8623dea5d5928daa841b2bd818d707be3411868e`
**Branch:** `agent/digitalbrain-hosting-testing`

This document is campaign scaffolding. Slice 3 deletes `docs/superpowers/` wholesale; the durable
rules it establishes are folded into `docs/contributing.md` and `docs/architecture.md` first. A plan
that outlives its campaign becomes the sprawl this campaign exists to remove.

---

## 1. The organizing rule

> A test earns its place by failing when **product behavior** breaks.
> It does not earn its place by failing when the **build graph** changes.

Every deletion below is justified by that rule, and every keeper survives it. The corollary for
documentation:

> Docs carry **vision**. Code carries **detail**. When they disagree, code is the source of truth.

## 2. What the measurements actually showed

Numbers taken firsthand at the baseline commit, not inherited from prior artifacts.

| Claim in prior artifacts | Measured reality |
| --- | --- |
| "202 markdown files" | **67 tracked**, 10,893 LOC in `docs/`. The 202 counted `node_modules`. |
| Test tier is inverted | Confirmed: tests 9,531 LOC vs src 9,362. |
| `DigitalBrain.Testing` unexamined | **3,178 LOC — the largest project in the repo, larger than the Kernel (2,611).** |
| Docs are sprawling | **78% of doc LOC describes a Behavior rail with zero lines of code.** |

Two findings that appear in no prior artifact:

**The docs test suite is the cause of the doc sprawl, not a guard against it.**
`docs/architecture.md:112` carries a single unreadable ~300-character status line because
`docs/tests/architecture-honesty.test.mjs:46` contains a ~300-character regex demanding that exact
string. `docs/concepts.md` can only grow because `reader-content.test.mjs:36` asserts `_Avoid_` lines
`>= 26`. Clean prose is *unreachable* while those regexes stand. Docs and docs-proofs are one job.

**The C# theater is load-bearing for the JavaScript proofs.**
`architecture-honesty.test.mjs:83` reads `tests/DigitalBrain.Tests/Packages/PackageInventory.cs` and
parses its C# constants with a regex to cross-check `docs/packages.md`. Deleting the C# theater
breaks `npm --prefix docs test` unless the JS theater goes first.

## 3. Purest specimens of the anti-pattern

Recorded because they are the clearest statements of what is being removed:

- `Boundary/BehaviorPackageBoundaries.cs:34` asserts `DoesNotContain("DigitalBrain.BehaviorBuilder")`
  — **a test guarding the absence of a project that was already deleted.** It is that ghost's only
  remaining reference.
- `Hosting/ProductModuleSet.cs` — `[Fact(DisplayName = "evaluated repository graph is 52 projects,
  157 references, 74 packages, 32 IDs, and 32 pins")]`. Five magic numbers, zero product behavior.
- `docs/tools/render-specification.mjs` — a "generator" that reads a file, asserts a heading, and
  rewrites it with a trailing newline. Wired into both `prebuild` and `pretest`, and
  `site-structure.test.mjs:19` asserts the generator exists. Ceremony guarded by theater.

## 4. Not touched, on evidence

- **`src/DigitalBrain.Kernel`** — codegraph-verified Orleans-grade durable mechanics: turn
  checkpointing, delegation eviction with protected retention, outbox drain with per-target blocking
  and depth limits. The `Neuron.*.cs` partial split is correct. It needs *tests*, not edits.
- **`src/DigitalBrain.Testing`** — despite being the largest project, its heavy files are all
  `internal` behind a six-type public surface (`TestBrain`, `TestClock`, `TestNeuron`, `TestOwner`,
  `DigitalBrainFixture`, `DigitalBrainTestBuilder`). Zero external consumers of the internals is
  *correct encapsulation*, not dead code. 3,178 LOC is the honest price of a real three-silo fixture
  with deterministic time, reminder driving, journal evidence, and fault injection. Naming polish
  only.
- **Code honesty.** No Behavior rail types (compiler, worker, broker, installer) exist. The rail is
  honestly Designed-only in code. The dishonesty was in docs.

## 5. The slices

Order is a dependency order, not a preference. Each ends green at the root gate and lands one commit
carrying the diff-grill answers.

### Slice 2 — docs proofs, 512 → ~100 LOC

Delete `architecture-honesty.test.mjs` (249) entirely. Delete `render-specification.mjs` and its
`prebuild`/`pretest` hooks. Reduce the rest to three invariants that can actually fail for a real
reason:

1. every nav and sidebar link resolves to a page that exists;
2. the quickstart matches the sample CI actually runs;
3. the architecture diagram composes only vocabulary a module or the kernel ships.

### Slice 3 — docs, 10,893 → ~625 LOC (94%)

Collapse `docs/architecture/*` (8 files, 1,615 LOC) and `architecture.md` into **one** ~250 LOC
document: the vision, the architectural laws, the honest Built/Designed line, live ratified rules,
known debts. Drop status ledgers, package topology, and the anchor-map table — all derivable from
code. Trim `concepts.md` to the primitives; trim `quickstart.md` and `contributing.md`; rewrite
`specification.md` to describe the post-theater tiers.

Delete `packages.md` (derivable from code), `docs/superpowers/` (34 files), `docs/research/`
(11 files), and `prompt-200-behavior-os.md` (643 LOC).

### Slice 4 — dead weight

`hosts/DigitalBrain.BehaviorBuilder/` (git-tracked files: 0; `bin`/`obj` ghost) and `artifacts/*`
(6 untracked probe projects from a prior campaign).

### Slice 5 — test theater, `DigitalBrain.Tests` 3,604 → ~1,700 LOC

Compiler-guided, because the cluster is tightly coupled: `PackageInventory` has 15 dependents,
`PackageBoundarySupport` 14, `RepositoryLayout` 8. Delete the theater, build, and extract only the
constants surviving keepers genuinely need.

**Delete (~1,840 LOC):** `PackageInventory`, `AspireContracts`, `ResidualPackageGraphContracts`,
`PackableProjects`, `PackageBoundarySupport`, `RepositoryLayout`, all seven `Boundary/*PackageBoundar*`
files, `AssemblyBoundaryContracts`, `Hosting/ProductModuleSet`, `AccountEnrichmentSampleContracts`.

**Keep, re-homed into behavior-named folders (~1,760 LOC), decoupled from the deleted helpers:**

| Survivor | New home | Why it survives |
| --- | --- | --- |
| `AiContractBoundaries` | `Contracts/AiSurface` | MAF `IAgent` isolation, LLM DI-key off the public surface, `IChatClient` confined to LLM neurons |
| `ClientApiContracts` | `Contracts/ClientSurface` | public client surface; replaces the removed PublicApiAnalyzers baseline |
| `ClientSendOrdering` | `Client/SendOrdering` | real call ordering — activate once, then fire once |
| `IdentityContracts` | `Identity/GrainKeyEncoding` | `OwnerId`/`NeuronId` reject values that break grain-key encoding |
| `TasksContracts`, `TimeContracts` | `Contracts/*Vocabulary` | module vocabulary and alias rules; graph assertions dropped |
| `FlutterContracts` | `Wire/FlutterWireGolden` | cross-language wire golden with two consumers, C# and Dart |
| `Hosting/Flutter*`, `HostingProjectionContracts` | `Hosting/` | real Aspire projection behavior, not graph shape |

One deliberate exception: `AssemblyBoundaryContracts` is an assembly-reference pin and goes, but its
assertion *"public packable types do not export MAF implementation types"* is a genuine invariant of
the same class as the AI boundary. That single assertion is **folded into `Contracts/AiSurface`**;
the other ~200 LOC is deleted.

### Slice 6 — coverage, red-first

Replace theater with proofs on the paths codegraph reports as having no covering tests. Write the
failing assertion first, watch it fail, then satisfy it.

- delegation eviction — `MakeRoomForDelegation` throws at `MaximumRememberedDelegations` when no
  terminal or consumed delegation is safely evictable; `TryEvictOldest` respects the protected floor;
- turn rollback — `RollbackTurnState` and `StageInboundCause` on a journal fault mid-turn;
- outbox drain across restart — blocked-target handling and redelivery.

Fixture pattern mirrors `tests/DigitalBrain.Time.Tests/CountdownRecovery.cs`.

### Slice 7 — polish and gate

Naming and explicit-flow polish on survivors. Full root gate, adversarial code review, findings
verified personally rather than accepted on the reviewer's word.

## 6. Gates

```
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test && npm --prefix docs run build
aspire build / run / test
```

Never `--filter` for a completion claim.

## 7. Success criteria

- Root gate, docs proofs, and Aspire integration green.
- Source exceeds tests again; zero graph, csproj, assembly-reference, or filesystem-layout pins.
- The hard kernel paths gain real coverage: fewer test LOC, more behavior proven.
- Docs are vision and decision records only — six pages, no campaign vocabulary.
- No orphan projects and no ghost-guarding tests.

## 8. Baseline discipline

Recorded at start: HEAD `8623dea5`, working tree clean. Re-checked before every stage.

Known tooling note: the first `dotnet build` after the baseline commit rewrote `DigitalBrain.slnx`,
dropping the `src/DigitalBrain` metapackage entry. It was restored; a second build left the file
untouched, so this was one-time SDK normalization rather than a recurring rewrite. Watch for
recurrence at each gate and never sweep it into a commit.
