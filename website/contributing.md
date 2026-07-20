---
title: Contributing
---

# Contributing

DigitalBrain is rebuilt under a deliberately strict discipline. These are not style preferences; they
are the rules that keep the framework's promises checkable.

[Architecture](/architecture) describes what is being built and marks each part as built or designed.
`CLAUDE.md` in the repository is the canonical working discipline for agents and contributors alike,
and `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md` is the plan of record.

## The gate

One command decides whether the repository is healthy. While working, run it from the root:

```powershell
dotnet test --logger "console;verbosity=minimal"
```

Before a release, run it across the solution in Release:

```powershell
dotnet test .\DigitalBrain.slnx -c Release
```

Both run all three tiers. **Never narrow either with `--filter`** — a green filtered run is not
evidence, and a project-scoped run has already missed a failing contract that the root run caught.
Run it in the background and poll rather than waiting on it.

The documentation site has its own gate. Invoke `node` directly rather than through npm, because
npm's child processes lose the nodejs PATH on Windows here:

```powershell
cd website
node tools/render-specification.mjs
node --test tests/*.test.mjs
```

All of these are enforced in CI, along with `eng/pack.ps1` and a consumer restore from an **empty**
package cache. A change is not done until every one of them is green.

## Three tiers of test

**Tier 0 — contract.** No cluster. Types, boundaries, public API surface, and the guarantees that can
be proven without hosting anything. Fast enough to run constantly.

**Tier 1 — simulations.** Gherkin scenarios fired into a real three-silo in-process Orleans cluster and
asserted against real journals. This is where behaviour is specified. A new guarantee belongs here, as
a scenario, before it exists as code.

**Tier 2 — hosted proof.** A real Aspire application: durable restart recovery, health, publish
manifest. Slow, few, and load-bearing.

## Writing a change

**Write the failing proof first.** Prefer a Tier-1 scenario, because a scenario is simultaneously the
test, the specification, and the published documentation — every `.feature` file appears on the
[Specification](/specification) page automatically. Then make it pass, then run the gate.

When the behaviour is not coming yet, keep the proof and exclude it rather than deleting it:
`[Fact(Explicit = true)]` for xUnit, `@ignore` for Gherkin. Those proofs are listed publicly on the
[Status](/status) page, because a proof nobody runs is worth nothing unless its state is visible. The
root gate is never red.

**Grill your own diff before you commit.** Three questions, answered in the commit message:

- What did I add that has no consumer today?
- What did I claim without running a command to check?
- What changed that I did not change?

The third matters more than it sounds. This repository has been modified mid-session by other tools;
if something moved that you did not move, say so and stop rather than sweeping it into your commit.

**Evidence precedes assertion.** "The tests pass" is not a claim you may make without the output in
front of you. If a step was skipped, say so. If something failed, say so with the failure.

## Comments are forbidden

Comments are forbidden as narrative, boilerplate, or commented-out code, in any tracked C#,
PowerShell, YAML, XML, MSBuild or `.feature` file. No XML documentation comment that restates a
signature.

This is not minimalism for its own sake. A comment is an assertion nothing checks, and it rots silently
while the code beside it changes. Put the meaning where it can be verified instead: in a name, a type,
a test, or a smaller function — `[Fact(DisplayName = "...")]` is the supported way to make a test
self-describing. If a piece of code needs a paragraph to explain, the paragraph is evidence the code is
the wrong shape.

The rule exists to stop narration and rot. It is not an instruction to withhold information that
genuinely has nowhere else to live; if you reach for that exception, expect a reviewer to ask why a
name, a type, or a test could not carry it.

Prose belongs in `README.md`, `CLAUDE.md` and this website.

## Naming

Names carry the explanation. `committedOutgoing` beats `count` plus a comment saying what was counted.
Reviewers are expected to push back on a name that needs help.

## Dependencies

Every version is pinned exactly in `Directory.Packages.props`. No floating ranges, no wildcards —
`eng/verify-dependencies.ps1` fails the build on either, and on any vulnerable or deprecated package.

## The security boundary

Provider SDKs live only in their owning runtime module. Today that is
`DigitalBrain.Modules.AI`; its Aspire provider integrations live in
`DigitalBrain.Modules.AI.Aspire.Hosting`. Kernel, AI Contracts, and every consumer-path package must
remain provider-free. `eng/pack.ps1` verifies the produced artifacts, not only project files.

An API key must never appear in the repository, in a test, in a sample, or in a publish manifest.

## Deleting

Delete before you add. Dead code, stale plans, superseded documents and flaky tests are all liabilities.
A flaky test is worse than a missing one: it teaches the team to ignore red. If a scenario cannot be
made deterministic, delete it and record why rather than retrying it in a loop.

## Honesty

Never tick a box that is not fully met, and never describe something as proven when it is merely
implemented. Where a guarantee is incomplete, say so plainly — the [Status](/status) page tracks the
open debts, and a limitation a user discovers themselves costs far more than one you wrote down.
