## 2026-05-23T19:10:48Z

You are the Lead Implementation Worker (teamwork_preview_worker) at working directory e:\digitalbrain\.agents\worker_global_sweep_retry_gen6.
Your task is to execute the final test sweep on the fully fixed codebase and ensure 100% of the active unified tests in the solution pass cleanly.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Please execute the following steps:
1. Initialize your BRIEFING.md and progress.md in your working directory e:\digitalbrain\.agents\worker_global_sweep_retry_gen6.
2. Clean all running background processes to release file locks:
   Stop-Process -Name BrainOS*, DigitalBrain*, dotnet, testhost -ErrorAction SilentlyContinue -Force
   dotnet build-server shutdown
3. Copy the optimized and finalized `run_sweep.ps1` from `e:\digitalbrain\.agents\worker_global_sweep_retry_gen5\run_sweep.ps1` to your directory `e:\digitalbrain\.agents\worker_global_sweep_retry_gen6\run_sweep.ps1`.
4. Modify your copied `run_sweep.ps1` to:
   - Adjust the `$logDir` and the `progress.md` paths inside the script to point to your workspace folder `e:\digitalbrain\.agents\worker_global_sweep_retry_gen6\`.
5. Run the sequential test sweep script to execute all active test projects:
   powershell.exe -ExecutionPolicy Bypass -File e:\digitalbrain\.agents\worker_global_sweep_retry_gen6\run_sweep.ps1
6. Inspect the resulting test logs and `sweep_results.json`.
7. Ensure all active test projects pass cleanly (0 failures).
8. Create `changes.md` and `handoff.md` summarizing the final sequential test sweep outcomes and verification.
9. Send a message to the caller Project Orchestrator (conversation ID: 467782dd-0df6-400e-9cdd-0cae96263d7f) with links to your handoff report and test results.
