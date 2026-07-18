## 2026-05-23T02:55:07Z

**Context**: We are performing the Global Test Sweep Retry for DigitalBrain. The previous worker encountered a hang while running the integration tests for `DigitalBrain.SDK.Google.Tests`. We are replacing them to execute the sweep in a clean, isolated environment.

**Interruption Point**:
- `BrainOS.Kernel.Tests` has been successfully verified (203/203 tests passed in isolation!).
- `DigitalBrain.SDK.Google.Tests` needs to be cleaned, built, and executed in isolation.

**Identity**:
- Role: Global Test Sweep Retry Worker (Gen 2)
- Working directory: e:\digitalbrain\.agents\worker_global_sweep_retry_gen2\

**Tasks**:
1. Create and initialize your own `progress.md` inside `e:\digitalbrain\.agents\worker_global_sweep_retry_gen2\` with `Last visited: [timestamp]` as your liveness heartbeat.
2. Clean up any active Docker containers and conflicting port bindings:
   - Run commands to check if there are running container instances named `orleans-redis` or bound to port `6380`.
   - Force-remove/stop any conflicting `orleans-redis` Docker containers if they exist (e.g. using `docker stop orleans-redis` or `docker rm -f orleans-redis`) to ensure a completely clean start.
   - Terminate any dangling dotnet test or Orleans processes that might still be running.
3. Clean and build the Google SDK tests:
   ```powershell
   dotnet clean sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj
   dotnet build sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj
   ```
4. Run the Google SDK tests in isolation:
   ```powershell
   dotnet test sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj
   ```
5. If the tests succeed or if they fail, capture the full console and error outputs.
   - Note: If there are failures in `DigitalBrain.SDK.Google.Tests`, analyze them closely. If they are genuine timing or assertion issues (like the null-reference signature rejection or the synapse timeout), detail them in your report so they can be fixed by a dedicated worker.
6. Write a comprehensive `handoff.md` in your working directory.
7. Send a message to your parent orchestrator (conversation ID: fbd28d5f-aa1c-49e1-ad34-d744ad0d92fd) with the absolute path to your `handoff.md`.

**MANDATORY INTEGRITY WARNING**:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.
