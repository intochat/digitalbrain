# S1.6b — Gmail grill micro-fix report

## What changed
- `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_demo_fixtures.dart` — embedded C# `CallMcpTool` JSON arg rewritten from C# raw `"""…"""` to escaped `"{\"query\":\"in:inbox\"}"` so the outer Dart `r"""` no longer terminates early at line 72.
- `src/Tests/DigitalBrain.Tests/Harness/FakeMcpTransport.cs` — Gmail fake catalog tool `get_thread_messages` renamed to `get_thread` (official Gmail MCP shape). No test asserted the old name.

## Tests
- No new tests; no pins flipped. Existing suite + shell analyze only.

## Gate

```
cd src/Modules/UI/Flutter/shell; flutter analyze
  Analyzing shell...
  No issues found! (ran in 1.9s)

dotnet build DigitalBrain.slnx
  Build succeeded.
  2 Warning(s) — AppHost node NO_COLOR/FORCE_COLOR noise only (not C# / TreatWarningsAsErrors)
  0 Error(s)
  Time Elapsed 00:00:02.99

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
  === TEST EXECUTION SUMMARY ===
  DigitalBrain.Tests  Total: 165, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 179.920s
```

First test run hit known flake `RunningTurnSurvivesSiloRestartAndCompletes` (TimeoutException 45s); re-run green 165/165. Unrelated to fixture/string rename.

## Conflicts & risks
- None. Grill MAJOR (shell parse break) is fixed; MINOR tool name aligned.

## Out of scope
- Core `activateControl` pre-existing analyze error (Claude.md ticketed).
- Fake `CallToolAsync` always returns threads payload (grill MINOR 3).
- OAuth expiry Theory residual (grill MINOR 4).
