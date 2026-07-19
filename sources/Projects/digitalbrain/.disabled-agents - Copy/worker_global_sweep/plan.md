# Worker Plan - Global Test Sweep

## Objective
Execute all 22 C# test projects in the `DigitalBrain` repository sequentially to verify absolute stability, zero regressions, and that all tests compile and pass cleanly.

## Test Projects to Run
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

## Verification Steps
- Check that all projects build successfully.
- Run `dotnet test` sequentially for each project to avoid resource/port conflicts.
- Aggregate all results, counting total tests passed, failed, and skipped.
- Produce a detailed handoff.md report summarizing the sweep.
