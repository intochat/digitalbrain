# Claude Code prompt: implement the whole coding agent programme

Paste the block below as the first message of a fresh Claude Code session opened in `E:\intochat\digitalbrain`
on `master`. It drives phases 0 to 5 in order, one PR per phase. The session will be long; the prompt tells
the agent how to record progress so a context reset or a new session resumes without loss.

```text
Implement the Roslyn coding agent programme in this repository, all phases, in order, as designed.

Read first, in this order, before touching anything:
1. docs/coding/coding-agent-design.md (ratified with every default in section 5). Sections 4.x are the architecture, section 7 the traps, section 8 the review findings already folded in, section 9 the per-phase specifications you will write plans from.
2. docs/coding/plans/2026-09-13-coding-phase0-workspace.md (the phase 0 plan, complete with code).
3. docs/coding/coding-agent-research.md (cite it for facts; do not re-research what it settles).

The programme:
- Phase 0 (workspace and map): execute the existing plan task by task.
- Phases 1 to 5 (edits as transactions, slots, swarm, learning, pretty map): for each phase, in this order:
  a. Run the spikes listed for the phase in design section 9 (phase 2 has four); write each result into docs/coding/NOTES.md under a "Phase N spikes" heading with the exact commands and observed behaviour. A spike that contradicts the design changes the design: edit the affected section, say why in NOTES, then continue.
  b. Write the phase plan with the superpowers:writing-plans skill from design section 9.N and the sections it cites, saved as docs/coding/plans/<date>-coding-phaseN-<name>.md, in the same shape as the phase 0 plan: files, interfaces, bite-sized TDD steps with the actual code, commands and expected output, a self-review. No placeholders.
  c. Implement the plan with the superpowers:subagent-driven-development skill: a fresh subagent per task, a review between tasks, the ledger it prescribes.
  d. Extend docs/coding/README.md (the module doc) and docs/coding/NOTES.md (deviations, live observations, transcripts), run the gates, open the PR.
- Branches and PRs: phase 0 on feature/coding-phase0-workspace from master; every later phase on feature/coding-phaseN-<name> created from the previous phase's branch, so the stack keeps building while PRs wait for review. Commits are prefixed "coding:" with the messages the plans give. Do not push or open a PR before the phase's final gate is green. When a phase PR is merged, rebase the open stack onto master before continuing. Never merge a PR yourself and never enable auto-merge.
- Progress record: keep docs/coding/STATUS.md (committed) with the current phase, the current task, the last green gate, the last commit, and the open PR numbers; update it at every task boundary. After a context reset or in a new session, read STATUS.md and NOTES.md first and resume from there.

Verification rules (a task is not done until its evidence exists):
- Every task ends with its own test class green: dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.<Class>
- Every commit is preceded by the full local gate: dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build (expect the 4 Docker-gated skips plus the coding gated skips).
- Gated facts (DIGITALBRAIN_CODING_SELF_TESTS=1: phase 0 opens the real solution and requires zero load failures; phase 1 runs the build and test runner on it; phase 2 builds a slot into artifacts/slot-b) run once per phase before the PR; never relax an assertion to pass, fix the cause or record the exact message in NOTES with the reason.
- Flutter changes (phases 0 and 5) run the Flutter gate from src/Modules/UI/Flutter: dart format --set-exit-if-changed core ui shell, then flutter analyze and flutter test in ui and in shell.
- Every phase ends with a live check of its exit criterion from design section 6 and 9 through aspire run and the Aspire MCP tools (list_resources, list_console_logs, list_traces): phase 0 "Where is ITimer used?" and "Map the solution"; phase 1 the rename landing as a commit with the suite green; phase 2 a promotion with at most one reconnect and a rollback after a deliberately broken build; phase 3 "add a Pause command to timers" through plan card, approval, green suite and landing; phase 4 a convention stored in one task and cited in the next; phase 5 the layered map with blast-radius highlighting. Record each transcript and the tool durations in NOTES.
- Run a code review (the code-review skill) on every phase branch before its final gate, focused on naming, dead code, comment noise, analyzer cleanliness and silent failures; fix what it finds before opening the PR.

Rules the repo enforces (a warning fails the build):
- TreatWarningsAsErrors with AnalysisLevel preview-all. Services use ConfigureAwait(false); grain code uses ConfigureAwait(true) like the kernel.
- No /// <summary> comments that restate a signature; names are the documentation; a short inline comment only where the reason is not visible in the code.
- Contract DTOs: [GenerateSerializer], [Alias("coding.<kebab-name>")], [property: Id(n)], listed in the module's JSON context. Neuron methods: [Alias]; queries [ReadOnly] with one DTO; mutators take exactly one DTO deriving from Command. The silo validates these at start and the error names the rule.
- Neurons never await another neuron's answer inside a reaction (design section 2); every wait is a saved phase completed by a later correlated reaction.
- A silo without the active-slot lease reacts to nothing (design 4.4); the swap is a durable follow-up after the turn that decided it.
- The silo project references every module project (Type.GetType loads modules). MSBuildLocator.RegisterDefaults() stays the first statement of src/Kernel/DigitalBrain.Silo/Program.cs. DisableMSBuildAssemblyCopyCheck lives on the module, silo and test projects only.
- Before writing code against any package API, look it up with Context7 (Roslyn /dotnet/roslyn, Agent Framework /microsoft/agent-framework, Aspire /microsoft/aspire.dev, Orleans /dotnet/orleans, YARP /dotnet/yarp). Package versions: the latest stable on nuget.org at the time you add them; the plans pin what was current on 2026-09-13 (Roslyn 5.9.0, Build.Locator 1.11.2, Agent Framework 1.21.0); record any bump in NOTES.
- Never read or write under C:\Users; the local NuGet cache is not documentation.
- The owner approves plans and landings inside the product through cards; you never bypass a sign-off gate in code or in tests, you script the approval.

When something the design assumes turns out false (an API missing, an Orleans behaviour different from research R5, an Aspire command absent), stop the task, write the finding in NOTES with the evidence, change the smallest possible part of the design and its plan, and continue. Do not silently widen or narrow scope.

Done means: six PRs open or merged (phases 0 to 5), every gate green on every branch, STATUS.md showing phase 5 complete, README.md documenting every neuron, tool and configuration key, and NOTES.md holding the live transcripts that prove each phase's exit criterion. Report per phase what was verified and what was skipped, with the output that proves it.
```
