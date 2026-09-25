# C# Expert: a coding agent composed of behaviors

Ratified 2026-09-25 (all six decisions at their recommended values).

## Shape
One `CSharpExpert` module (depends on Coding, Roslyn, DotNet, AI, Behavior). A per-request
`ICodingRun` neuron (key = run id = correlation id) holds state and raises signals. Each step is a
compiled `IBehavior` (same style as `ElonBitcoin`): it listens with `brain.On<Signal>(run)`, calls
neurons, and asks the run to raise the next signal. Behaviors never call each other.

```
FeatureRequested -> Understand -> ContextReady -> Plan -> PlanDrafted
  -> human: PlanClarified (re-plan) | PlanApproved | RunStopped
PlanApproved/StepDone -> Implement -> StepDrafted -> Build -> BuildPassed|BuildFailed
BuildPassed -> Test -> TestsPassed|TestsFailed
BuildFailed|TestsFailed -> Fix (capped by profile) -> StepDrafted | NeedsHuman
TestsPassed -> Review -> StepDone | ReviewRejected(->Fix) ; last StepDone -> RunFinished
```

## Contracts (module Contracts project)
- `ICodingRun : INeuron` — `Request(FeatureRequest)`, `Clarify(string)`, `Approve()`, `Stop()`,
  `Read()` -> `CodingRunSnapshot` (status, plan, current step, attempts, last build/test/review,
  diff), plus behavior-facing `Record*` methods that update state and raise the next signal.
- `ICodingProfile : INeuron` (key = workspace) — `Read()`/`Write(CodingProfile)`: planner/implementer
  agent ids, `MaxFixAttempts` (default 3), `BuildAndTestEachStep` (true), `ReviewRules`
  (no empty `/// <summary>`, self-explanatory naming, minimal inline comments), `NuGetPolicy`
  (latest; preview policy reserved for later).
- Signals: records deriving from `Signal`, carrying the run id.

## Decisions
1. Separate `CSharpExpert` module.  2. Compiled `IBehavior` classes first; deployed scripts later.
3. Fixed signal chain; LLM only inside Plan and Implement (and Review judgement).
4. Each run edits in its own git worktree (created on `PlanApproved`, removed/kept on finish).
5. Models set in the profile, default provider from the AI module.
6. First demo: small sample solution under test assets.

## Visibility
All behavior→neuron calls are already observed in the activity graph; the run id groups one
request. Run window (Flutter) shows plan checklist, clarify box, Approve/Stop, current step,
diff, build/test/review results.

## Out of scope for this round
NuGet behavior, hot-deploy through `IBehaviorProgram`, packaging (behavior-packages branch).
