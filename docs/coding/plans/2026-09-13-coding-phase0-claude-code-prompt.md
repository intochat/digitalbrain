# Claude Code prompt: implement coding phase 0

Paste the block below as the first message of a fresh Claude Code session opened in `E:\intochat\digitalbrain`
on `master` (the docs are on master).

```text
Implement phase 0 of the Roslyn coding agent in this repository, exactly as planned.

Read these three files first, in this order, before touching anything:
1. docs/coding/coding-agent-design.md (ratified with every default in section 5; sections 4.1 to 4.3, 4.8, 4.10, 7 and 8 apply to phase 0)
2. docs/coding/plans/2026-09-13-coding-phase0-workspace.md (the plan: 9 tasks, each with its tests, code, commands and expected output)
3. docs/coding/coding-agent-research.md (cite it when you need a fact; do not re-research what it already settles)

How to work:
- Use the superpowers:subagent-driven-development skill: one fresh subagent per task in plan order, a review between tasks, and keep the ledger it prescribes. Do not skip tasks or reorder them; Task 7 (Flutter) can run in parallel with Task 6 only after Task 2 is merged into the branch.
- Branch: create feature/coding-phase0-workspace from master. Commit after every task with the commit message the plan gives, prefixed "coding:". Do not push and do not open the PR until Task 9 says so.
- Before writing any code that calls a package API, look the API up with Context7 (Roslyn: /dotnet/roslyn; Agent Framework: /microsoft/agent-framework; Aspire: /microsoft/aspire.dev). Two APIs the plan flags as unverified in 5.9.0: the return type of Workspace.RegisterWorkspaceFailedHandler and the exact overload of SymbolFinder.FindSourceDeclarationsAsync; confirm both before Task 3 and Task 4.
- Never read or write anything under C:\Users. The local NuGet cache is not documentation.
- Package versions are pinned in the plan (Microsoft.CodeAnalysis.CSharp.Workspaces 5.9.0, Microsoft.CodeAnalysis.Workspaces.MSBuild 5.9.0, Microsoft.Build.Locator 1.11.2); they are the latest on nuget.org as of 2026-09-13. If nuget.org has a newer stable version, use it and record the bump in docs/coding/NOTES.md.

Rules the repo enforces (the build is warning-free or it fails):
- TreatWarningsAsErrors with AnalysisLevel preview-all. Services use ConfigureAwait(false); grain code uses ConfigureAwait(true) like the kernel.
- No /// <summary> comments that restate a signature. Names are the documentation. A short inline comment only where the reason is not visible in the code.
- Every contract DTO: [GenerateSerializer], [Alias("coding.<kebab-name>")], [property: Id(n)], and an entry in CodingJson. Every neuron method: [Alias]; queries [ReadOnly] with one DTO; mutators take exactly one DTO deriving from Command. The silo validates these rules at start; read the error, it names the rule.
- The silo project must reference the module project or the kernel cannot load it (Type.GetType).
- The DisableMSBuildAssemblyCopyCheck property goes on the module, silo and test projects only, never in Directory.Build.props.
- MSBuildLocator.RegisterDefaults() is the first statement of src/Kernel/DigitalBrain.Silo/Program.cs.

Verification, every task:
- Run the task's own test class first: dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.<Class>
- Before each commit run the full local gate: dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build (expect the 4 Docker-gated skips and, from Task 4 on, 1 coding self-test skip).
- Task 4 also runs the gated self-test once with DIGITALBRAIN_CODING_SELF_TESTS=1 against the real DigitalBrain.slnx and requires zero load failures; if a failure appears, fix the cause or record it in NOTES.md with the exact message, do not relax the assertion.
- Task 7 runs the Flutter gate from src/Modules/UI/Flutter: dart format --set-exit-if-changed core ui shell, then flutter analyze and flutter test in ui and in shell.
- Task 8 uses the Aspire MCP tools (list_resources, list_console_logs, list_traces) to prove the running kernel: the warmup opened the solution, "Where is ITimer used?" answers from code_find_symbols plus code_references with TimerNeuron.cs and a line number, and "Map the solution" opens a graph in the shell. Record both transcripts and the tool durations in docs/coding/NOTES.md. Also record whether the untrusted-content screen changed any code_* result.
- Run a code review (the code-review skill) on the branch before Task 9's final gate, focused on naming, dead code, comment noise and analyzer cleanliness; fix what it finds.

Done means: all nine tasks committed, the local gate green, the self-test green, the Flutter gate green, docs/coding/README.md and docs/coding/NOTES.md written, and the PR opened with the phase 0 exit criteria from the design (section 6, phase 0) in its body. Report what was verified and what was skipped, with the output that proves it.
```
