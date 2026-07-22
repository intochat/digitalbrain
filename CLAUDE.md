# CLAUDE.md — how to work in this repository

Canonical for every agent and contributor. `AGENTS.md` points here. Written to be followable by
any harness — Claude Code, Codex, Grok — with nothing but a shell and this repository.

---

## 1. What is being built

DigitalBrain is a .NET framework for durable agents on Orleans and Aspire. The paradigm is
**neurons, synapses, and simulations**. The thing that makes it worth building is the last part:

> **A brain you program by writing ordinary C#, and that can program itself.**

The architecture in six lines:

- **The typed interface is the surface, the synapse is the substrate, the generator is the bridge.**
- **A synapse is a fact** — a thin record, broadcast, no reply. **An interface method is a request** —
  directed at a capability, replies. Both are journaled; neither is privileged.
- **Modules own vocabulary** — synapse records and neuron interfaces. Compile-time, needs a rebuild.
- **Behaviors own logic** — single-file C# scripts. Runtime, needs only approval.
- **The client API is the programming model.** The same file runs outside the cluster as a script and
  installs inside it as a behavior.
- **Every install is a human-approved proposal**, journaled and reversible.

`docs/architecture.md` is the plan of record. Read its ratified architecture before changing
framework code. Do not silently reverse its decisions. If evidence invalidates one, record the
reversal in that file.

---

## 2. The loop

Apply in order. Order matters — jumping to optimise or automate locks in waste.

1. **Question the requirement.** Trace it to a person or a consumer that exists *today*. "The plan
   says so" is not a reason. If nothing consumes it, it is a guess — say so out loud.
2. **Delete.** Prefer deleting a thing to simplifying it. Target a net reduction. If you are not
   adding things back occasionally, you are not deleting enough.
3. **Simplify what remains.** Then check you have not just moved the complexity somewhere else.
4. **Accelerate the feedback loop.**
5. **Automate.** Last. Never automate a process you have not first deleted and simplified.

---

## 3. Grilling

Grilling is the discipline that makes step 1 real. It applies before building **and during it**.

### Before building

State a recommendation, state the strongest argument against it, and defend or fold. Present
evidence, not opinion. When a decision belongs to a person, put it to them with your recommendation
attached — never a neutral menu.

### During implementation — three moves

**Before the step — write the proof that fails.** Assert the behaviour the system *should* have and
watch it fail before writing the code that satisfies it. When the behaviour is not coming yet, keep
the proof and exclude it rather than deleting it. **Never a red root gate.**

The two exclusion mechanisms behave differently on demand, and the difference matters:

| Kind | Marked | Prove it red with | What you get |
|---|---|---|---|
| xUnit | `[Fact(Explicit = true, DisplayName = "…")]` | `./tests/<proj>/bin/Debug/net10.0/<proj>.exe -explicit only` | The test **runs** and fails |
| Gherkin | `@ignore @red-until-<reason>` | `./tests/<proj>/bin/Debug/net10.0/<proj>.exe -failSkips` | The scenario is **reported failed because it was skipped** — its body never ran |

`-explicit only` does not reach `@ignore` scenarios; it reports them as not run. To actually execute
an ignored scenario you must remove the tag locally. Prefer the xUnit form when you want a proof that
genuinely executes on demand, and always tag the Gherkin form with the reason it is held.

**Before the commit — grill the diff.** Three questions, answered in the commit message:

- What did I add that has no consumer today?
- What did I claim without running a command to check?
- What changed that I did not change?

**Before the claim — run it and quote it.** Evidence precedes assertion, always. "Tests pass" is not
a claim you may make without the output in front of you. If a step was skipped, say so. If something
failed, say so with the failure.

### Per phase

A real adversarial review at every phase boundary, and **verify its findings yourself**. Reviews are
worth their cost — a prior phase raised six findings and all six were real — but a review is a claim
like any other. Check its method, not only its conclusions.

---

## 4. Oracles and tools

**The mandatory path uses only the compiler, the test suite, and git.** These exist in every harness.

| Question | Oracle |
|---|---|
| Does this API exist? Is this signature right? | **The compiler.** Write a throwaway file referencing it and build. No `CS0246` proves the type exists |
| Does the system behave this way? | **The test suite.** Not the docs — several docs have been wrong |
| What was here before? Is this recoverable? | **git.** Retired trees live at `git show <sha>^:<path>` |

Optional accelerators, in `.mcp.json`: `codegraph` for architecture, `context7` for package docs,
`aspire` for resource control, `microsoft-learn` for .NET docs.

**If an accelerator is unavailable, say so and fall back to the oracles. Do not skip silently.** Both
`context7` and `codegraph` have been unavailable in this repository — quota exhausted and `npx`
exiting 9009 — and a ritual that mandates an unavailable tool gets worked around instead of followed.
Note: Microsoft Learn returns the older `Orleans.EventSourcing.JournaledGrain` for journaling queries.
That is a different API from `Microsoft.Orleans.Journaling`. Do not conflate them.

**Check whether the ground moved.** Record `git rev-parse HEAD` and `git status --porcelain` at the
start of a session, and check both again before staging. This repository has been modified mid-session
by other tools. If something changed that you did not change, **surface it and stop** — do not revert
it and do not sweep it into your commit.

**Fan-out needs a scoring rule.** When dispatching parallel agents, give them the rule by which a
finding counts — for example "changes a decision that is currently open", not "find valuable
content". Without a rule they return summaries; with one they return findings.

**Agent harness (Claude / Grok / Codex).** Capability inventory is `tools/harness/inventory.json`
(IAW-aligned plugins + mattpocock). Portable skills live in `.agents/skills/`. Per-harness adapters:
`.claude/settings.json`, `.grok/config.toml`, `.codex/config.toml`. Install/sync with
`pwsh tools/harness/setup.ps1`; check declared capability presence with
`pwsh tools/harness/verify.ps1`. This is an installation check, not a behavioural conformance
suite. Codex will never get Claude LSP/hooks; those rows are explicit `unsupported`.

---

## 5. Gates

**The root gate, every phase, no exceptions:**

```
dotnet test --logger "console;verbosity=minimal"
```

**Never `--filter`.** Run it in the background and poll. A project-scoped run has already missed a
failing contract that the root run caught. During TDD you may run the smallest owning project in the
foreground, but the root gate is what permits a completion claim.

**The website gate** runs `node` directly, not `npm` — npm's cmd children lose the nodejs PATH here:

```
node tools/render-specification.mjs
node --test tests/*.test.mjs
```

Two guards fail the build by design, and that is correct:

- Adding a public type means updating `PublicAPI.Unshipped.txt`.
- Adding an `[Alias]` means updating the pinned-alias contract.

---

## 6. Rules

- **No comments as narrative, boilerplate, or commented-out code.** No `/// <summary>` restating a
  signature. Carry meaning in names, types, and tests instead — `[Fact(DisplayName = "...")]` is the
  supported way to make a test self-describing. The rule exists to stop narration and rot, not to
  forbid the rare case where a name genuinely cannot carry the information. Markdown prose is
  documentation, not a comment.
- **Keep decision records and design rationale. Delete session logs, progress reports, and task
  checklists.** The earlier form of this rule said to kill 99% of plans as noise; applied literally
  it destroys the best artifacts a repository produces. The distinction is durability, not age.
- **Relative paths only.** Never reference anything under a user profile directory.
- **Latest deliberate package versions**, centrally in `Directory.Packages.props`.
- **Small slices, green at each boundary.** Build, run the owning project, run the root gate before
  claiming the slice is done.
- **Commit at green boundaries** with the diff-grill answers in the message.
- **Self-evolution is the product.** The only path to a live behaviour is a human-approved proposal
  through the journaled rail. That rail is not built yet — until it is, changes arrive the ordinary
  way.

---

## 7. Where things stand

The durable neuron and synapse foundation, generated module activation, typed AI neurons, and
AI-owned Aspire integration are proven. The Foundation PoC architecture is frozen through the
ratified rules in `docs/architecture.md` (§9). Its proposed public CLR seams, red-green order, and stop conditions are in
`docs/superpowers/plans/2026-07-20-foundation-poc.md`. That plan is approved and its Tasks 1 through 8
are complete, as it records at its own line 11; the work remaining in it proceeds one green slice at a
time.

One assumption is load-bearing and unmeasured: **that a model can reliably emit behaviour scripts.**
That benchmark and the behavior proposal/install rail remain deliberately outside the Foundation
PoC. Do not pull them forward while Tasks, AI/MAF, Google, Salesforce, Time, and the hosted restart
story are being proven.

Update this file through the same rail as everything else, and only when the loop actually improves.
