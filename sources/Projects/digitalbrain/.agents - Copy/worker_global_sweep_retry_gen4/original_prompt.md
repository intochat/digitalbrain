## 2026-05-23T19:54:37Z
You are the Lead Implementation Worker (teamwork_preview_worker) at working directory e:\digitalbrain\.agents\worker_global_sweep_retry_gen4.
Your task is to execute the final test sweep and ensure 100% of the active unified tests in the solution pass cleanly.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Please execute the following steps:
1. Initialize your BRIEFING.md and progress.md in e:\digitalbrain\.agents\worker_global_sweep_retry_gen4.
2. Clean all running background processes to release file locks:
   Stop-Process -Name BrainOS*, DigitalBrain*, dotnet, testhost -ErrorAction SilentlyContinue -Force
   dotnet build-server shutdown
3. Copy the modified `run_sweep.ps1` from `e:\digitalbrain\.agents\worker_global_sweep_retry\run_sweep.ps1` to your directory `e:\digitalbrain\.agents\worker_global_sweep_retry_gen4\run_sweep.ps1`.
4. Modify your copied `run_sweep.ps1` to:
   - Perform process cleanup inside the loop before executing each test:
     Stop-Process -Name BrainOS*, DigitalBrain*, dotnet, testhost -ErrorAction SilentlyContinue -Force
   - Adjust the `logDir` and `progress.md` paths inside the script to point to your workspace folder `e:\digitalbrain\.agents\worker_global_sweep_retry_gen4\`.
5. Run a global clean build of the entire solution to ensure everything is compiled cleanly:
   dotnet build DigitalBrain.slnx --configuration Debug /nodeReuse:false
6. Run your modified sequential test sweep script to execute all active test projects.
7. Inspect the resulting test logs and `sweep_results.json`.
8. Ensure all active test projects pass cleanly.
9. Create `changes.md` and `handoff.md` summarizing the outcomes and verification.
10. Send a message to the caller Project Orchestrator (conversation ID: 467782dd-0df6-400e-9cdd-0cae96263d7f) with links to your handoff report and test results.
