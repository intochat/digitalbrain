# Documentation consolidation

**Date:** 2026-07-22
**Status:** approved design, not yet implemented

Reduce DigitalBrain's documentation from 28 markdown files to 13, make the rendered site the single
source of truth, and replace two overlapping 900–1100 line planning records with one
module-organized architecture document.

---

## 1. Problem

Documentation is spread across four competing locations that disagree with each other.

`REFINED-ARCHITECTURE-AND-NEXT-STEPS.md` (1121 lines) and `APPROVED-ARCHITECTURE-DECISIONS.md`
(894 lines) state the same ratified architecture in two formats. A third, older copy of
`APPROVED-ARCHITECTURE-DECISIONS.md` sits outside the repository at the parent directory and
contradicts the live one on D4.6, D5.10, D5.15, and D7.2. `website/architecture.md` and
`website/status.md` restate the same claims a third and fourth time.

The claims have measurably drifted:

- `REFINED` §1 reports `DigitalBrain.Tests: 125 passed`; its own retained evidence in
  `APPROVED` D4.8 reports 143.
- `website/status.md` states "Google, Salesforce, Flutter, Memory modules — not built" while
  `modules/` contains `DigitalBrain.Modules.Google` and `DigitalBrain.Modules.Salesforce`.
- The gate commands appear verbatim in four files: `CLAUDE.md`, `README.md`,
  `website/contributing.md`, and `website/status.md`.

Most of the volume is not architecture. `REFINED` carries a completion-percentage table, a hard
deletion manifest whose every entry is done, forty implementation checkboxes all marked `[x]`, and a
duplicate of the acceptance gates. `APPROVED` carries an index mapping literal chat turns
(`> apptove`, `> lgtm`, `> do it`) to decision numbers. `CLAUDE.md` §6 already rules on this:
keep decision records and design rationale, delete session logs, progress reports, and task
checklists.

No code, test, project file, or CI configuration references a decision number or either planning
document. The coupling is prose-only.

## 2. Goal

- One architecture document, organized around modules, in the rendered documentation tree.
- Every status claim in exactly one place, adjacent to the rule it qualifies.
- The rendered site is the source of truth rather than a downstream copy.
- No content that only records how a past session proceeded.
- No loss of durable design rationale or honest limitation disclosure.

## 3. Non-goals

- No changes to `.cs`, `.csproj`, `.slnx`, or package versions.
- No rewrite of the quickstart sample or the generated specification page.
- No new documentation for modules that do not exist.
- No reopening of ratified architecture. This is a documentation move, not a design revision.

## 4. Target shape

**28 markdown files today; 13 after.**

Eleven are documentation: four at the repository root and seven rendered pages. The remaining two
are unrendered working files — the live implementation plan and this spec — excluded from the site.

### Repository root — 4 files

| File | Change |
|---|---|
| `CLAUDE.md` | §1 and §7 repoint from `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md` to `docs/architecture.md`; §5 website gate path `website` becomes `docs` |
| `README.md` | Plan-of-record link and status paragraph repoint to `docs/architecture.md`; gate path updated; `website/` row in the repository-shape block becomes `docs/` |
| `AGENTS.md` | Unchanged. Four lines pointing at `CLAUDE.md`; other harnesses require it at the root |
| `CHANGELOG.md` | Unchanged |

### `docs/` — renamed from `website/`, 7 pages

| Page | Change |
|---|---|
| `index.md` | Unchanged |
| `quickstart.md` | Unchanged |
| `concepts.md` | Absorbs all 116 lines of `CONTEXT.md`. Becomes the vocabulary page; the `_Avoid_:` lines are retained verbatim |
| `architecture.md` | Rewritten module-organized. Absorbs the distilled ratified architecture from both planning records and the open-debts section of `status.md` |
| `packages.md` | New. Replaces the twelve files under `packages/` with one table |
| `contributing.md` | Gate section replaced by a pointer to `CLAUDE.md`; `REFINED-…` reference repointed |
| `specification.md` | Generated from `tests/DigitalBrain.Simulations/*.feature`. Untouched |

Plus `docs/superpowers/plans/2026-07-20-foundation-poc.md` and `docs/superpowers/specs/`, both
excluded from rendering.

### Deleted

| Path | Reason |
|---|---|
| `../APPROVED-ARCHITECTURE-DECISIONS.md` (outside the repository) | Stale pre-repository copy contradicting the live record. Not git-recoverable; superseded in full by the in-repository version |
| `APPROVED-ARCHITECTURE-DECISIONS.md` | Ratified rules distilled into `docs/architecture.md`; provenance tables and approval-turn index are session log |
| `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md` | Architecture distilled into `docs/architecture.md`; status table, deletion manifest, completed checklists, and duplicated gates are rot |
| `CONTEXT.md` | Merged into `docs/concepts.md` |
| `website/status.md` | Completion table and forward-looking prose are progress reporting and already false. Open debts are preserved — see §6 |
| `website/packages/` (12 files) | Collapsed into `docs/packages.md` |
| `docs/superpowers/plans/2026-07-21-task-5-codex-continuation-prompt.md` | Session handoff for Task 5, which landed across five commits |

## 5. `docs/architecture.md` structure

```
1   The vision                        a brain you program in C#, that can program itself
2   The kernel                        neurons, synapses, capability requests, delegation,
                                      journals, and what the kernel must never contain
3   The module model                  Contracts / Runtime / Aspire.Hosting triple,
                                      AppHost selects once, generated catalog,
                                      namespace and type name as vocabulary
4   The modules
    4.1  AI            Built          ILLM, IAgent, orchestration by base type, the MAF seam,
                                      the MEAI wire, session versus checkpoint, compaction
    4.2  Tasks         Built          Task, Attempt, Worker, lifecycle, typed blockers,
                                      one Lockstep superstep per run
    4.3  Google        Built          IGmail as semantic capability root, MCP stays private,
                                      progressive exact tools
    4.4  Salesforce    Built          mutation ledger, CommandId, human approval,
                                      reconciliation, no exactly-once claim
    4.5  Time          Designed       ICountdown, IReminder, interval versus calendar,
                                      DST resolution, overdue coalescing
    4.6  Flutter       Designed       Flutter neurons and the contract drift guard
    4.7  Memory        Out of scope
5   Behaviors and scripting           the self-programming rail
6   Registry and discovery            generated catalog; vector search ranks, never resolves
7   Hosting and durability            AppHost, silo, the Azure Storage profile, observability
8   Known limitations                 rescued from status.md open debts
9   Ratified rules                    the compact checklist
10  Open, and explicitly rejected     Ical.Net and Noda Time, the MAF Durable Extension,
                                      model tiers, Memory, the raw invoke escape hatch
11  Build order                       what comes next; no checkboxes, no percentages
```

Each module subsection opens with a literal `Status: Built` or `Status: Designed` line. This is the
only status claim in the repository and it sits adjacent to the rule it qualifies. It also satisfies
the existing `site.test.mjs` assertion that architecture sections carry an explicit status line.

## 6. Preserved limitation disclosure

`website/status.md` fuses progress reporting with honest limitation disclosure. The progress
reporting is deleted. The disclosure moves to `docs/architecture.md` §8 and keeps its test guard.

Seven disclosures are carried across, each concerning the system that is built rather than the one
that is planned:

- An Orleans client is a trusted cluster peer; owner identity is a correctness boundary, not
  authentication.
- Journal history is bounded; compaction retains a summary and a recent window. Effectively-once
  processing is windowed by the durable dedupe set.
- Delivery ordering is local: FIFO per target and at least once, with no cross-target ordering.
- Broadcast targets handler types and creates correlation-derived instances.
- Client observation is not the final timeline stream; a durable per-owner timeline and reconnect
  lifecycle are not built.
- `AsClient()` needs a production credential audit; the client projection must never inherit
  silo-only storage or module secrets.
- DevUI is not part of the current architecture.

The `site.test.mjs` test named "the open debts are disclosed rather than buried" is repointed from
`status.md` to `architecture.md` rather than deleted. The enforcement survives; only its target
moves.

## 7. Mechanical changes outside markdown

| File | Change |
|---|---|
| `website/` → `docs/` | `git mv`, preserving history |
| `.github/workflows/ci.yml` | The `website` job's `working-directory: website` becomes `docs` |
| `docs/.vitepress/config.mts` | Remove Status from nav and sidebar; replace the twelve `/packages/*` sidebar entries with one `/packages` link; add `srcExclude: ['superpowers/**']` |
| `docs/tests/site.test.mjs` | `websiteRoot` and every `read('website', …)` retargeted; `contentPages` and `packagePages` replaced by the new page set; package-page assertions replaced by `packages.md` table assertions; open-debts and status assertions repointed at `architecture.md`; retired-section list gains `packages` and `status.md` |
| `docs/tools/render-specification.mjs` | `websiteRoot` binding renamed; the `'..'` hop to `tests/DigitalBrain.Simulations` re-verified after the move |

`srcExclude` keeps `docs/superpowers/**` out of the rendered site, so the live implementation plan
and this spec stay working files rather than published pages, while remaining at the path the
superpowers skills and `CLAUDE.md` already assume.

## 8. Foundation PoC plan

`docs/superpowers/plans/2026-07-20-foundation-poc.md` stays where it is and stays live. Tasks 1
through 8 are built: `modules/` contains AI, Google, Salesforce, and Tasks. Tasks 9 through 12 are
outstanding: there is no Time module, no `ICountdown` in any `.cs` file, and no `WithAzureStorage`
in any `.cs` file.

The plan is trimmed to the outstanding Tasks 9 through 12, plus the scope lock, fixed dependency
direction, frozen PoC contracts, capability-tool boundary, TDD and commit protocol,
requirement-to-proof map, and stop conditions. The completed Task 1 through 8 sections are deleted;
git history is their archive.

Task 12 of that plan is this work. Its file list is updated to the consolidated set, and its website
gate block is repointed at `docs/`.

## 9. Verification

From `docs/`:

```powershell
node tools/render-specification.mjs
node --test tests/*.test.mjs
```

From the repository root:

```powershell
dotnet test --logger "console;verbosity=minimal"
git diff --check
git status --short
```

No `.cs` file changes, so the root gate result must be unchanged from its pre-work value. Record
`git rev-parse HEAD` and `git status --porcelain` before starting and compare before staging;
surface any change not made by this work rather than absorbing it.

Exact pass, fail, and skip counts are quoted rather than summarized. The completion claim requires
zero failures and zero skips.

Two additional checks specific to this work:

```powershell
rg -n "REFINED-ARCHITECTURE|APPROVED-ARCHITECTURE|CONTEXT\.md|website/" --glob '!.git' .
```

Expected: no matches outside `CHANGELOG.md` history entries.

```powershell
rg -n "Status: (Built|Designed)" docs/architecture.md
```

Expected: seven matches, one per module subsection.

## 10. Risks

**The architecture doc is a rewrite, not a move.** Distilling roughly 2000 lines into one
module-organized document risks silently dropping a ratified rule. Mitigation: the compact ratified
rule list in `APPROVED` §9 is 47 numbered rules and is carried across as §9 of the new document
before prose is written, so every rule has a destination before any source file is deleted.

**Deleting the out-of-repository stale copy is not git-recoverable.** It is superseded in full by
the in-repository version, which is itself preserved in git history after deletion. Accepted.

**`site.test.mjs` is the enforcement mechanism for site truthfulness.** Weakening it while
retargeting it would remove the guard that makes "the site is the source of truth" real. Every
assertion is retargeted or replaced; none is deleted without a replacement covering the same claim.
