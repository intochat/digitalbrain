# Task: Gen7 Implementation and Verification Sweep

## Objective
Apply robust disk-scanning BDD mock priming to support Orleans out-of-process silo execution, adjust the sequential test runner's status evaluation to correctly handle skipped/ignored test runs, and execute the final global test sweep to verify that 100% of active test projects pass cleanly.

## Key Instructions
1. Initialize `BRIEFING.md` and `progress.md` in `.agents/worker_global_sweep_retry_gen7/`.
2. Stop lingering processes to release file/lock compilation clashes:
   `Stop-Process -Name BrainOS*, DigitalBrain*, dotnet, testhost -ErrorAction SilentlyContinue -Force`
   `dotnet build-server shutdown`
3. Modify `e:\digitalbrain\sdk\DigitalBrain.SDK.Ai\DigitalBrain.SDK.Ai\Llm\BddMockChatClient.cs` to dynamically scan physical project directories for `*.feature` files at runtime.
4. Copy and optimize `.agents/worker_global_sweep_retry_gen6/run_sweep.ps1` to `.agents/worker_global_sweep_retry_gen7/run_sweep.ps1` with corrected exit code status determination.
5. Run the sequential sweep to verify 100% PASS on all active projects.
6. Write `changes.md` and `handoff.md` with complete logs and summaries.
