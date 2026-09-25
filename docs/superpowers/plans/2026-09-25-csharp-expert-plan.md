# C# Expert — phased plan

Spec: `docs/superpowers/specs/2026-09-25-csharp-expert-design.md`. Executed by OpenCode workers
(one sequential worktree `E:/intochat/wt/CSX`, branch `wt/CSX` from `codex/neuron-activity-graph`),
orchestrated and reviewed by Claude; each phase is merged back after the orchestrator re-runs tests.

- CSX1 Skeleton: module + contracts, `ICodingRun`, `ICodingProfile`, signals, Understand and Plan
  behaviors, scripted fake agent for tests, registration in the developer profile.
- CSX2 Human loop: clarify/approve/stop on the run, Plan re-drafts on `PlanClarified`, Flutter
  run window.
- CSX3 Implement: per-run git worktree, Implement/Build/Test/Fix behaviors, end-to-end on a sample
  solution with scripted agents.
- CSX4 Review: Review behavior applying profile rules to the diff, RunFinished, diff in the window.
