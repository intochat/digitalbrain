## 2026-05-23T19:01:52Z
You are the Lead Implementation Worker (teamwork_preview_worker) at working directory e:\digitalbrain\.agents\worker_final_sweep_gen2.
Your caller conversation ID is: 467782dd-0df6-400e-9cdd-0cae96263d7f.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Please execute the following tasks:
1. Initialize your BRIEFING.md and progress.md in your working directory.
2. Clean all running background dotnet, BrainOS, or DigitalBrain processes:
   Stop-Process -Name BrainOS*, DigitalBrain*, dotnet, testhost -ErrorAction SilentlyContinue -Force
   dotnet build-server shutdown
3. Execute the global sequential test sweep using the script at:
   e:\digitalbrain\.agents\worker_final_sweep\run_sweep.ps1
4. Inspect the resulting `sweep_results.json` generated in `.agents/worker_final_sweep/sweep_results.json` (or as modified by the script).
5. Ensure all 422+ unified tests pass cleanly. If any failures are found, diagnose and propose stubs/mocks or configuration fixes as described in the parent's handoff files.
6. Create `changes.md` and `handoff.md` summarizing the outcomes and verification commands.
7. Send a message to the caller Project Orchestrator with links to your handoff report and test results.
