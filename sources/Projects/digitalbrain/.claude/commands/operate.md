---
description: Universal phase-aware operator — resume the DigitalBrain v3 roadmap from Project #8 and implement the next issue spec-first.
argument-hint: "[optional: epic code e.g. E-INO, or issue number, or 'status']"
---

# /operate — DigitalBrain v3 roadmap operator

You are operating the BrainOS/DigitalBrain repo (`E:\BrainOS`, Windows/PowerShell,
.NET Aspire + Orleans). This command is **self-contained and resumable**: every run
starts by re-reading ground truth, then advances the roadmap by exactly one
well-verified increment (or audits status). `$ARGUMENTS` may name an epic code
(e.g. `E-INO`), an issue number, or `status` (status-only, no implementation).

## 0. Hard rules (read every run — they override defaults)

- Obey `E:\BrainOS\CLAUDE.md` and `~/.claude/CLAUDE.md`. Spec-first is a **runtime
  invariant** (v3 L6): no unit ships without its green test/scenario.
- **No mass-rename.** BrainOS = the platform/runtime substrate; DigitalBrain = the
  one installed domain. v3 §Naming is superseded by
  `docs/superpowers/specs/2026-05-19-self-hosting-boot-design.md §2.1` (D-A).
- Verify every package/framework API via **Context7** before writing code; use
  latest NuGet. Never read anything under `C:\Users\`. Aspire only via
  `aspire start`/`aspire stop` or the Aspire MCP — never tasklist/taskkill.
- Tests run **high-severity** and must be green before "done"
  (`superpowers:verification-before-completion`). One test command: `dotnet test`.
  Never `flutter test`.
- Branch + PR per issue. **Never push master directly.** Run a code review
  (`/review` or the code-review skill) before reporting any result (user global rule).
- This command must never trigger a wholesale GitHub rewrite. It touches **one
  issue's** state per increment.

## 1. Load ground truth (every run, in order)

1. `E:\BrainOS\CLAUDE.md`; `docs/v3/VISION.md` (canonical; §4–§7 InoLang, §11 spine);
   `docs/superpowers/specs/2026-05-19-self-hosting-boot-design.md` (E-BOOT + the
   two-layer split — authoritative over v3 §Naming).
2. Auto-memory: `~/.claude/projects/E--BrainOS/memory/MEMORY.md` and the linked
   notes — especially `brainos-platform-digitalbrain-domain`, `v3-roadmap-decisions`,
   `github-board-coordinates`, `phase3-design-source`.
3. The in-tree InoLang foundation (`src/inolang/DigitalBrain.InoLang` — already
   golden-test-green for the deterministic front-end + L6 gate) vs the
   `examples/inolang-orleans-proto` runtime patterns to port.

## 2. Read Project #8 status (gh CLI; repo = `LeftTwixWand/BrainOS.Marketplace`)

- `gh project item-list 8 --owner LeftTwixWand --format json` and
  `gh issue list --repo LeftTwixWand/BrainOS.Marketplace --state open --json number,title,labels,milestone --limit 400`.
- Build the picture: per epic (label `epic:*` / milestone), which issues are
  Todo / In Progress / Done; which are blocked.
- If `$ARGUMENTS` == `status`: report a concise per-epic Todo/In-Progress/Done
  table + the recommended next issue, then STOP (no implementation).

## 3. Determine the phase from board state

- **Phase 1 — substrate (now until E-SDK Done):** the Creator cannot yet build
  the runtime that runs it. You (Claude Code) implement C# substrate issues
  yourself, spec-first, subagent-parallelized.
- **Phase 2 — dogfood (E-SDK Done):** for `surface:inolang` issues, drive the
  **Creator** via `aspire start` + the Aspire MCP / `BrainOSGateway` gRPC: send
  the intent, let it author the `.ino` red→green, verify the green scenario,
  then promote. Hand-write C# only for `surface:csharp-engineering` /
  `surface:brain-shell` (the L4 substrate).

## 4. Pick the next issue (dependency-ordered)

Critical-path spine order (do not skip ahead):
`E-ABI → E-INO → E-BOOT → E-RUN → E-SDK → E1 → E2 → E3`.
Parallel Brain-shell (`prio:parallel-shell`): `E4 / E-IDENT / E-SET / E-BRAND /
E5 / E6` — workable once their stated deps land. Research (`type:research`):
`E8 / E9` — spikes, output is a written finding.

Selection rule (unless `$ARGUMENTS` overrides):
1. Lowest-spine epic that is not Done.
2. Within it, the first `Todo`, dependency-unblocked issue (read the issue body's
   deps; E-BOOT needs E-INO + the E-SDK Aspire boot face; E5 needs E-IDENT; etc.).
3. If the critical path is fully blocked, take an unblocked `prio:parallel-shell`
   issue. Prefer enabling issues (skeletons/contracts) over leaf issues.
4. For 2+ independent unblocked issues, dispatch them as **parallel subagents**
   (`superpowers:dispatching-parallel-agents`), one issue each, isolated.

Set the chosen issue's Project #8 Status → **In Progress** and comment that
`/operate` started it.

## 5. Implement the issue (spec-first)

1. If the issue's design is non-obvious, run `superpowers:brainstorming` first;
   for any bug/failure use `superpowers:systematic-debugging`.
2. **TDD (`superpowers:test-driven-development`)**:
   - `surface:inolang` / `spec:scenario-required`: write/extend the `.ino`
     `scenario` (or the in-tree InoLang test), watch it **red**, implement, make
     it **green**. L6 must gate it.
   - `surface:csharp-engineering` / `surface:brain-shell` / `spec:gating-test`:
     write the failing gating test (Reqnroll triplet or xunit), red → implement →
     green. UI assertions are RFW-payload gRPC tests in `BrainOS.E2E.Tests`, not
     `flutter test`.
3. Respect repo invariants: triplet co-location, `.Contracts` for every synapse
   type + its constant, Aspire as the only composition root, no cross-domain silo
   refs, generated neurons only under `dynamic/.../Generated/`.
4. Verify (evidence before claims): `dotnet build`; `dotnet test` from the root; when the change
   affects a running resource, apply via Aspire MCP `rebuild` (not stop/start
   unless AppHost/kernel changed). Confirm the green output.
5. Code review (user global rule) before reporting: `/review` or code-review
   skill; address findings.

## 6. Close the loop

- Open a branch + PR (`gh pr create`), summary + test plan, link the issue
  (`Closes #N`). Never push master directly.
- Set the issue's Project #8 Status → **Done** only after the PR is green and
  reviewed; otherwise leave **In Progress** with a comment on what remains.
- Append a one-line progress note to the issue. Update auto-memory only for
  durable, non-obvious learnings (not task state).
- Report: issue done, PR URL, what's next on the spine. If autonomous continuous
  operation is desired, the user runs `/loop /operate` (self-paced) or schedules
  it; a single `/operate` advances exactly one increment.

## 7. Guardrails / stop conditions

- Stop and ask the user if: a decision contradicts a locked memory/CLAUDE.md;
  an action is destructive or a bulk external write beyond one issue; the
  critical path needs a scope cut; or Context7 cannot confirm an API.
- Never fabricate green tests or skip the L6 gate to "finish". A red scenario
  means not done.
- Keep `Program.cs` thin; emit failure synapses, don't throw across the cortex;
  structured logging only.
