---
title: Contributing
---

# Contributing

DigitalBrain is rebuilt under a deliberately strict discipline. These are not style preferences; they
are the rules that keep the framework's promises checkable.

[Architecture](/architecture) describes what is being built and marks each part as built or designed.
`CLAUDE.md` in the repository is the canonical working discipline for agents and contributors alike,
and `docs/architecture.md` is the plan of record.

## The gate

`CLAUDE.md` §5 is the canonical gate, covering both the root command and the documentation site's
`node`-based gate; run it at every phase and before any completion claim.

Before a release, run the full suite across the solution in Release:

```powershell
dotnet test .\DigitalBrain.slnx -c Release
```

**Never narrow it with `--filter`** — a project-scoped run has already missed a failing contract
that the root run caught.

## Three tiers of test

**Tier 0 — contract (L0).** No cluster. Types, boundaries, public API surface, and the guarantees that
can be proven without hosting anything. Fast enough to run constantly.

**Tier 1 — in-process cluster (L1).** Method-scoped `TestBrain` against a real multi-silo Orleans
cluster and typed committed journals. This is where product behaviour is proven (Quickstart, Time,
optional module smoke). Write the failing proof first, then make it pass.

**Tier 2 — hosted proof (L2).** A real Aspire application: exclusive AppHost graph health and related
host proofs. Slow, few, and load-bearing.

## Writing a change

**Write the failing proof first.** Prefer a Tier 1 test on `TestBrain` / `TestNeuron` with journal
evidence. Then make it pass, then run the gate.

When the behaviour is not coming yet, keep the proof and exclude it rather than deleting it:
`[Fact(Explicit = true)]`. An excluded proof still reports as explicit rather than disappearing —
the same reasoning behind the [Architecture](/architecture) page's "Known limitations" section (§8).
The root gate is never red.

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
PowerShell, YAML, XML, or MSBuild file. No XML documentation comment that restates a signature.

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

Every version is pinned exactly in `Directory.Packages.props`. Central Package Management is on, so a
floating range or wildcard is a build error, not a convention.

## The security boundary

Provider SDKs live only in their owning runtime module. Today that is
`DigitalBrain.Modules.AI`; its Aspire provider integrations live in
`DigitalBrain.Modules.AI.Aspire.Hosting`. Kernel, AI Contracts, and every consumer-path package must
remain provider-free. A package-boundary contract test enforces this on project references.

An API key must never appear in the repository, in a test, in a sample, or in a publish manifest.

## Deleting

Delete before you add. Dead code, stale plans, superseded documents and flaky tests are all liabilities.
A flaky test is worse than a missing one: it teaches the team to ignore red. If a scenario cannot be
made deterministic, delete it and record why rather than retrying it in a loop.

## Honesty

Never tick a box that is not fully met, and never describe something as proven when it is merely
implemented. Where a guarantee is incomplete, say so plainly — the [Architecture](/architecture)
page's "Known limitations" section (§8) tracks the open debts, and a limitation a user discovers
themselves costs far more than one you wrote down.

## CI and CD

Two workflows, no NuGet publish until there is a real consumer and a versioning design.

| Workflow | When | What |
| --- | --- | --- |
| [`.github/workflows/ci.yml`](https://github.com/intochat/digitalbrain/blob/master/.github/workflows/ci.yml) | PR and push to `master` | **`framework`**: `dotnet test DigitalBrain.slnx -c Release` on every event. **`docs`**: `npm ci` / `npm test` / `npm run build` in `docs/` **on pull requests only**. |
| [`.github/workflows/docs-pages.yml`](https://github.com/intochat/digitalbrain/blob/master/.github/workflows/docs-pages.yml) | Every push to `master`, plus `workflow_dispatch` | Same docs gate, then upload `docs/.vitepress/dist` and deploy with `actions/deploy-pages`. |

Rules that matter:

- Website deploy does **not** wait on the framework job. A red framework check must not block Pages; it must still block merge when required checks are configured.
- Master runs the docs gate **once**, inside `docs-pages` (not again in `ci`), so the site is not double-built on every land.
- There is no framework CD workflow: nothing packs or pushes to NuGet.org.
- Action pins use major tags (`@v7`, …). [`.github/dependabot.yml`](https://github.com/intochat/digitalbrain/blob/master/.github/dependabot.yml) updates GitHub Actions, npm under `docs/`, and NuGet (grouped).
- Pull requests into `master` should require the **`framework`** and **`docs`** check names from `ci`.

## Documentation site on GitHub Pages

The VitePress site under `docs/` is published to **https://digitalbrain.tech** by `docs-pages.yml` as above.

Publishing source must be **GitHub Actions** (not a branch/`gh-pages` folder). Set that under
**Settings → Pages → Build and deployment → Source**.

### Custom domain

1. In **Settings → Pages → Custom domain**, set `digitalbrain.tech` and enable **Enforce HTTPS**
   once DNS has propagated (can take up to 24 hours).
2. Keep `docs/public/CNAME` as a single line `digitalbrain.tech` so the published artifact records
   the apex host. Domain ownership is still configured in the GitHub UI (or Pages API); the file
   alone does not bind the domain.
3. VitePress `base` stays `/` because the site is served at the apex root, not under
   `/digitalbrain/`.

### DNS records (at the domain registrar)

| Host | Type | Value |
| --- | --- | --- |
| `@` (apex `digitalbrain.tech`) | `A` | `185.199.108.153` |
| `@` | `A` | `185.199.109.153` |
| `@` | `A` | `185.199.110.153` |
| `@` | `A` | `185.199.111.153` |
| `@` | `AAAA` | `2606:50c0:8000::153` |
| `@` | `AAAA` | `2606:50c0:8001::153` |
| `@` | `AAAA` | `2606:50c0:8002::153` |
| `@` | `AAAA` | `2606:50c0:8003::153` |
| `www` | `CNAME` | `intochat.github.io` |

GitHub Pages will redirect `www.digitalbrain.tech` ↔ `digitalbrain.tech` when both are configured.
Prefer verifying the domain under organization settings before pointing DNS, to reduce takeover risk.
Do not put DNS verification tokens in this repository.
