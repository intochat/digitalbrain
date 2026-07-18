## 2026-05-23T20:21:57Z
You are the Lead Implementation Worker (teamwork_preview_worker) at working directory e:\digitalbrain\.agents\worker_global_sweep_retry_gen5.
Your task is to execute the final test sweep and ensure 100% of the active unified tests in the solution pass cleanly.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Please execute the following steps:
1. Initialize your BRIEFING.md and progress.md in e:\digitalbrain\.agents\worker_global_sweep_retry_gen5.
2. Clean all running background processes to release file locks:
   Stop-Process -Name BrainOS*, DigitalBrain*, dotnet, testhost -ErrorAction SilentlyContinue -Force
   dotnet build-server shutdown
3. Copy the high-reliability sweep script from `e:\digitalbrain\.agents\worker_global_sweep_retry\run_sweep.ps1` to your directory `e:\digitalbrain\.agents\worker_global_sweep_retry_gen5\run_sweep.ps1`.
4. Modify your copied `run_sweep.ps1` to:
   - Adjust the `$logDir` and the `progressContent` paths inside the script to point to your workspace folder `e:\digitalbrain\.agents\worker_global_sweep_retry_gen5\`.
   - Ensure the process cleanup is run at the start of the script:
     Stop-Process -Name BrainOS*, DigitalBrain*, dotnet, testhost -ErrorAction SilentlyContinue -Force
5. Run your modified sequential test sweep script to execute all active test projects. Make sure that it runs the build-first and dynamic test runner detection correctly.
6. Inspect the resulting test logs and `sweep_results.json`.
7. Ensure all active test projects pass cleanly.
8. Create `changes.md` and `handoff.md` summarizing the outcomes and verification.
9. Send a message to the caller Project Orchestrator (conversation ID: 467782dd-0df6-400e-9cdd-0cae96263d7f) with links to your handoff report and test results.
