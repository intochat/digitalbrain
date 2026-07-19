---
title: Contributing
---

# Contributing

DigitalBrain is rebuilt under a deliberately strict discipline. These are not style preferences; they
are the rules that keep the framework's promises checkable.

## The gate

One command decides whether the repository is healthy:

```powershell
dotnet test .\DigitalBrain.slnx -c Release
```

It runs all three tiers. Never narrow it with `--filter` — a green filtered run is not evidence.

The documentation site has its own gate:

```powershell
cd website
npm ci
npm test
npm run build
```

Both are enforced in CI, along with `eng/pack.ps1` and a consumer restore from an **empty** package
cache. A change is not done until every one of them is green.

## Three tiers of test

**Tier 0 — contract.** No cluster. Types, boundaries, public API surface, and the guarantees that can
be proven without hosting anything. Fast enough to run constantly.

**Tier 1 — simulations.** Gherkin scenarios fired into a real three-silo in-process Orleans cluster and
asserted against real journals. This is where behaviour is specified. A new guarantee belongs here, as
a scenario, before it exists as code.

**Tier 2 — hosted proof.** A real Aspire application: durable restart recovery, health, publish
manifest. Slow, few, and load-bearing.

## Writing a change

Write the failing test first. Prefer a Tier-1 scenario, because a scenario is simultaneously the test,
the specification, and the published documentation — every `.feature` file appears on the
[Specification](/specification) page automatically.

Then make it pass, then run the gate.

## Comments are forbidden

No line comments, block comments, XML documentation comments, commented-out code, or explanatory
annotations in any tracked C#, PowerShell, YAML, XML, MSBuild or `.feature` file.

This is not minimalism for its own sake. A comment is an assertion nothing checks, and it rots silently
while the code beside it changes. Put the meaning where it can be verified instead: in a name, a type,
a test, or a smaller function. If a piece of code needs a paragraph to explain, the paragraph is
evidence the code is wrong shape.

Prose belongs in `README.md`, `CLAUDE.md` and this website.

## Naming

Names carry the explanation. `committedOutgoing` beats `count` plus a comment saying what was counted.
Reviewers are expected to push back on a name that needs help.

## Dependencies

Every version is pinned exactly in `Directory.Packages.props`. No floating ranges, no wildcards —
`eng/verify-dependencies.ps1` fails the build on either, and on any vulnerable or deprecated package.

## The security boundary

Provider SDKs and credentials live **only** in `DigitalBrain.Kernel`. `DigitalBrain.Abstractions`,
`.Client`, `.Testing` and `.Aspire` must never reference them. `eng/pack.ps1` verifies this against the
produced `.nupkg` files, not the project files, because the artifact is what ships.

An API key must never appear in the repository, in a test, in a sample, or in a publish manifest.

## Deleting

Delete before you add. Dead code, stale plans, superseded documents and flaky tests are all liabilities.
A flaky test is worse than a missing one: it teaches the team to ignore red. If a scenario cannot be
made deterministic, delete it and record why rather than retrying it in a loop.

## Honesty

Never tick a box that is not fully met, and never describe something as proven when it is merely
implemented. Where a guarantee is incomplete, say so plainly — the [Status](/status) page tracks the
open debts, and a limitation a user discovers themselves costs far more than one you wrote down.
