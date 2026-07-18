# Worker Plan - Global Test Sweep Retry

## Objective
Re-run the two test projects that failed or timed out during the initial global sweep to verify if those failures were flaky/port-conflict issues and confirm absolute stability:
1. `kernel/BrainOS.Kernel.Tests/BrainOS.Kernel.Tests.csproj` (failed 1 of 203 tests)
2. `sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj` (failed 4 of 11 tests)

## Verification Steps
- Make sure no other dotnet test processes or Aspire AppHost containers/processes are running.
- Run `dotnet test kernel/BrainOS.Kernel.Tests/BrainOS.Kernel.Tests.csproj` sequentially.
- Run `dotnet test sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj` sequentially.
- Verify whether the tests pass cleanly in isolation.
- Write a final `handoff.md` report summarizing these results.
