## 2026-05-23T03:07:45Z
We are performing the Global Test Sweep Retry for DigitalBrain. The Gen 2 worker has encountered a hang while running the integration tests for `DigitalBrain.SDK.Google.Tests` in a single run. We are replacing them with a Gen 3 worker armed with a highly robust, partitioned, and timed test-execution strategy.

**Interruption Point**:
- `BrainOS.Kernel.Tests` has been successfully verified (203/203 tests passed in isolation!).
- `DigitalBrain.SDK.Google.Tests` needs to be run in a partitioned manner to prevent deadlocks and isolate hangs.

**Identity**:
- Role: Global Test Sweep Retry Worker (Gen 3)
- Working directory: e:\digitalbrain\.agents\worker_global_sweep_retry_gen3\

**Tasks**:
1. Create and initialize your own `progress.md` inside `e:\digitalbrain\.agents\worker_global_sweep_retry_gen3\` with `Last visited: [timestamp]` as your liveness heartbeat.
2. Clean up any active Docker containers and conflicting port bindings:
   - Stop and remove any conflicting container instances named `orleans-redis` or bound to port `6380` (e.g. `docker stop orleans-redis` and `docker rm -f orleans-redis`).
   - Kill any lingering `dotnet` test runners, Orleans silo processes, or redis processes.
3. Clean and build the Google SDK tests:
   ```powershell
   dotnet clean sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj
   dotnet build sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj
   ```
4. Execute the tests in a **PARTITIONED** manner by running each of the three feature groups independently with a strict timeout:
   - For each partition, run the command and set a time limit (e.g. force-terminate the process if it runs for more than 2 minutes).
   - **Partition A (Stripe)**:
     ```powershell
     dotnet test sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj --filter "FullyQualifiedName~Stripe"
     ```
   - **Partition B (Telegram)**:
     ```powershell
     dotnet test sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj --filter "FullyQualifiedName~Telegram"
     ```
   - **Partition C (Digest)**:
     ```powershell
     dotnet test sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj --filter "FullyQualifiedName~Digest"
     ```
5. Capture and analyze the console and error outputs for each partition.
   - If a partition hangs or times out, document which test case was running and what error was encountered.
   - If there are genuine failures, detail the stack traces and assertion messages in your report.
6. Write a comprehensive `handoff.md` in your working directory summarizing the partition execution results.
7. Send a message to your parent orchestrator (conversation ID: fbd28d5f-aa1c-49e1-ad34-d744ad0d92fd) with the absolute path to your `handoff.md`.

**MANDATORY INTEGRITY WARNING**:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

## 2026-05-23T03:08:26Z
**Context**: Abort/Cancel Gen 3 Retry Worker
**Content**: Hello! The Gen 2 retry worker has successfully completed its test execution and delivered a comprehensive handoff report. Because we now have the full findings and exact diagnostic traces, we are cancelling your Gen 3 run.
**Action**: Please abort your execution, clean up your workspace directory, and go idle. Thank you for your readiness!

