# Final Global Test Sweep Plan

## Objective
Execute all 22 C# test projects in the `DigitalBrain` repository sequentially to verify absolute stability, zero regressions, and that all 717 tests compile and pass with a 100% success rate.

## Target Projects
1. UI/BrainOS.E2E.Tests/BrainOS.E2E.Tests.csproj
2. examples/inolang-orleans-proto/tests/InoLang.Orleans.Tests/InoLang.Orleans.Tests.csproj
3. examples/inolang-orleans-proto/tests/InoLang.Tests/InoLang.Tests.csproj
4. inolang/DigitalBrain.InoLang.TestRunner.Tests/DigitalBrain.InoLang.TestRunner.Tests.csproj
5. inolang/DigitalBrain.InoLang.Tests/DigitalBrain.InoLang.Tests.csproj
6. kernel/BrainOS.Boot.Tests/BrainOS.Boot.Tests.csproj
7. kernel/BrainOS.Core.Hosting.Tests/BrainOS.Core.Hosting.Tests.csproj
8. kernel/BrainOS.Core.Tests/BrainOS.Core.Tests.csproj
9. kernel/BrainOS.Domains.Dynamic/BrainOS.Domains.Dynamic.Tests/BrainOS.Domains.Dynamic.Tests.csproj
10. kernel/BrainOS.Kernel.Tests/BrainOS.Kernel.Tests.csproj
11. samples/BrainOS.Domains.Engineering/BrainOS.Domains.Engineering.Tests/BrainOS.Domains.Engineering.Tests.csproj
12. samples/BrainOS.Domains.Onboarding/BrainOS.Domains.Onboarding.Tests/BrainOS.Domains.Onboarding.Tests.csproj
13. samples/BrainOS.Domains.Travel/BrainOS.Domains.Travel.Tests/BrainOS.Domains.Travel.Tests.csproj
14. sdk/DigitalBrain.SDK.Ai/DigitalBrain.SDK.Ai.Tests/DigitalBrain.SDK.Ai.Tests.csproj
15. sdk/DigitalBrain.SDK.Aspire.Tests/DigitalBrain.SDK.Aspire.Tests.csproj
16. sdk/DigitalBrain.SDK.Canvas/DigitalBrain.SDK.Canvas.Tests/DigitalBrain.SDK.Canvas.Tests.csproj
17. sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj
18. sdk/DigitalBrain.SDK.Identity/DigitalBrain.SDK.Identity.Tests/DigitalBrain.SDK.Identity.Tests.csproj
19. sdk/DigitalBrain.SDK.Mcp/DigitalBrain.SDK.Mcp.Tests/DigitalBrain.SDK.Mcp.Tests.csproj
20. sdk/DigitalBrain.SDK.Sqlite/DigitalBrain.SDK.Sqlite.Tests/DigitalBrain.SDK.Sqlite.Tests.csproj
21. sdk/DigitalBrain.SDK.Visuals/DigitalBrain.SDK.Visuals.Tests/DigitalBrain.SDK.Visuals.Tests.csproj
22. sdk/DigitalBrain.SDK.Windows.Tests/DigitalBrain.SDK.Windows.Tests.csproj

## Step-by-Step Instructions
1. Perform dynamic process sweeps to terminate any locked dotnet/silo processes on Windows.
2. Shutdown all build servers and MSBuild compiler nodes (`dotnet build-server shutdown`).
3. Sequentially build and test all 22 projects. (You can leverage/adapt the existing `e:\digitalbrain\.agents\worker_global_sweep\run_sweep.ps1` script to run them, but ensure you redirect its outputs to your own workspace folder `e:\digitalbrain\.agents\worker_final_sweep\logs\`).
4. Double check that the Google SDK integration tests (`sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj`) compile and pass 11/11 cleanly!
5. Verify that all 717 tests pass with a 100% success rate.
6. Write a comprehensive `handoff.md` report summarizing the final sweep with command snippets and count tables.
7. Send a completion message to the parent orchestrator.
