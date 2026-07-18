# BrainOS E2E Test Suite Execution Report

**Date/Time**: 2026-05-22T23:45:00Z
**Environment**: Windows (dotnet 11.0.100-preview.3)

---

## 1. Build Verification

### Build Command
```powershell
dotnet build UI\BrainOS.E2E.Tests\BrainOS.E2E.Tests.csproj
```

### Build Output Summary
```text
  DigitalBrain.InoLang -> E:\digitalbrain\inolang\DigitalBrain.InoLang\bin\Debug\net11.0\DigitalBrain.InoLang.dll
  BrainOS.ServiceDefaults -> E:\digitalbrain\kernel\BrainOS.ServiceDefaults\bin\Debug\net11.0\BrainOS.ServiceDefaults.dll
  BrainOS.Core -> E:\digitalbrain\kernel\BrainOS.Core\bin\Debug\net11.0\BrainOS.Core.dll
  DigitalBrain.SDK.Canvas.Contracts -> E:\digitalbrain\sdk\DigitalBrain.SDK.Canvas\DigitalBrain.SDK.Canvas.Contracts\bin\Debug\net11.0\DigitalBrain.SDK.Canvas.Contracts.dll
  DigitalBrain.SDK.Visuals.Contracts -> E:\digitalbrain\sdk\DigitalBrain.SDK.Visuals\DigitalBrain.SDK.Visuals.Contracts\bin\Debug\net11.0\DigitalBrain.SDK.Visuals.Contracts.dll
  BrainOS.Domains.Dynamic.Contracts -> E:\digitalbrain\kernel\BrainOS.Domains.Dynamic\BrainOS.Domains.Dynamic.Contracts\bin\Debug\net11.0\BrainOS.Domains.Dynamic.Contracts.dll
  BrainOS.Domains.Engineering.Contracts -> E:\digitalbrain\samples\BrainOS.Domains.Engineering\BrainOS.Domains.Engineering.Contracts\bin\Debug\net11.0\BrainOS.Domains.Engineering.Contracts.dll
  DigitalBrain.SDK.Ai.Contracts -> E:\digitalbrain\sdk\DigitalBrain.SDK.Ai\DigitalBrain.SDK.Ai.Contracts\bin\Debug\net11.0\DigitalBrain.SDK.Ai.Contracts.csproj.dll
  DigitalBrain.SDK.Google.Contracts -> E:\digitalbrain\sdk\DigitalBrain.SDK.Google\DigitalBrain.SDK.Google.Contracts\bin\Debug\net11.0\DigitalBrain.SDK.Google.Contracts.dll
  DigitalBrain.SDK.Sqlite.Contracts -> E:\digitalbrain\sdk\DigitalBrain.SDK.Sqlite\DigitalBrain.SDK.Sqlite.Contracts\bin\Debug\net11.0\DigitalBrain.SDK.Sqlite.Contracts.dll
  BrainOS.Domains.Travel.Contracts -> E:\digitalbrain\samples\BrainOS.Domains.Travel\BrainOS.Domains.Travel.Contracts\bin\Debug\net11.0\BrainOS.Domains.Travel.Contracts.dll
  BrainOS.Kernel.Contracts -> E:\digitalbrain\kernel\BrainOS.Kernel.Contracts\bin\Debug\net11.0\BrainOS.Kernel.Contracts.dll
  DigitalBrain.SDK.Identity.Contracts -> E:\digitalbrain\sdk\DigitalBrain.SDK.Identity\DigitalBrain.SDK.Identity.Contracts\bin\Debug\net11.0\DigitalBrain.SDK.Identity.Contracts.dll
  BrainOS.Domains.Onboarding.Contracts -> E:\digitalbrain\samples\BrainOS.Domains.Onboarding\BrainOS.Domains.Onboarding.Contracts\bin\Debug\net11.0\BrainOS.Domains.Onboarding.Contracts.dll
  DigitalBrain.SDK.Windows -> E:\digitalbrain\sdk\DigitalBrain.SDK.Windows\bin\Debug\net11.0\DigitalBrain.SDK.Windows.dll
  DigitalBrain.SDK.Grok -> E:\digitalbrain\sdk\DigitalBrain.SDK.Grok\DigitalBrain.SDK.Grok\bin\Debug\net11.0\DigitalBrain.SDK.Grok.dll
  DigitalBrain.SDK.Aspire.Contracts -> E:\digitalbrain\sdk\DigitalBrain.SDK.Aspire.Contracts\bin\Debug\net11.0\DigitalBrain.SDK.Aspire.Contracts.dll
  DigitalBrain.SDK.Aspire -> E:\digitalbrain\sdk\DigitalBrain.SDK.Aspire\bin\Debug\net11.0\DigitalBrain.SDK.Aspire.dll
  DigitalBrain.SDK.Mcp -> E:\digitalbrain\sdk\DigitalBrain.SDK.Mcp\DigitalBrain.SDK.Mcp\bin\Debug\net11.0\DigitalBrain.SDK.Mcp.dll
  BrainOS.Core.Hosting -> E:\digitalbrain\kernel\BrainOS.Core.Hosting\bin\Debug\net11.0\BrainOS.Core.Hosting.dll
  DigitalBrain.SDK.Sqlite -> E:\digitalbrain\sdk\DigitalBrain.SDK.Sqlite\DigitalBrain.SDK.Sqlite\bin\Debug\net11.0\DigitalBrain.SDK.Sqlite.dll
  BrainOS.Domains.Engineering -> E:\digitalbrain\samples\BrainOS.Domains.Engineering\BrainOS.Domains.Engineering\bin\Debug\net11.0\BrainOS.Domains.Engineering.dll
  DigitalBrain.SDK.Identity -> E:\digitalbrain\sdk\DigitalBrain.SDK.Identity\DigitalBrain.SDK.Identity\bin\Debug\net11.0\DigitalBrain.SDK.Identity.dll
  BrainOS.Domains.Onboarding -> E:\digitalbrain\samples\BrainOS.Domains.Onboarding\BrainOS.Domains.Onboarding\bin\Debug\net11.0\BrainOS.Domains.Onboarding.dll
  DigitalBrain.SDK.Google -> E:\digitalbrain\sdk\DigitalBrain.SDK.Google\DigitalBrain.SDK.Google\bin\Debug\net11.0\DigitalBrain.SDK.Google.dll
  DigitalBrain.SDK.Canvas -> E:\digitalbrain\sdk\DigitalBrain.SDK.Canvas\DigitalBrain.SDK.Canvas\bin\Debug\net11.0\DigitalBrain.SDK.Canvas.dll
  BrainOS.Domains.Dynamic -> E:\digitalbrain\kernel\BrainOS.Domains.Dynamic\BrainOS.Domains.Dynamic\bin\Debug\net11.0\BrainOS.Domains.Dynamic.dll
  BrainOS.Domains.Travel -> E:\digitalbrain\samples\BrainOS.Domains.Travel\BrainOS.Domains.Travel\bin\Debug\net11.0\BrainOS.Domains.Travel.dll
  DigitalBrain.SDK.Visuals -> E:\digitalbrain\sdk\DigitalBrain.SDK.Visuals\DigitalBrain.SDK.Visuals\bin\Debug\net11.0\DigitalBrain.SDK.Visuals.dll
  DigitalBrain.SDK.Ai -> E:\digitalbrain\sdk\DigitalBrain.SDK.Ai\DigitalBrain.SDK.Ai\bin\Debug\net11.0\DigitalBrain.SDK.Ai.dll
  BrainOS.Kernel -> E:\digitalbrain\kernel\BrainOS.Kernel\bin\Debug\net11.0\BrainOS.Kernel.dll
  BrainOS.AppHost -> E:\digitalbrain\kernel\BrainOS.AppHost\bin\Debug\net11.0\BrainOS.AppHost.dll
  BrainOS.NeuronTesting -> E:\digitalbrain\kernel\BrainOS.NeuronTesting\bin\Debug\net11.0\BrainOS.NeuronTesting.dll
  BrainOS.E2E.Tests -> E:\digitalbrain\UI\BrainOS.E2E.Tests\bin\Debug\net11.0\BrainOS.E2E.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:30.80
```

---

## 2. Test Execution Results

### Test Command
```powershell
dotnet test UI\BrainOS.E2E.Tests\BrainOS.E2E.Tests.csproj
```

### Test Output
```text
Running tests from E:\digitalbrain\UI\BrainOS.E2E.Tests\bin\Debug\net11.0\BrainOS.E2E.Tests.dll (net11.0|x64)
E:\digitalbrain\UI\BrainOS.E2E.Tests\bin\Debug\net11.0\BrainOS.E2E.Tests.dll (net11.0|x64) passed (44s 736ms)

Test run summary: Passed!
  total: 22
  failed: 0
  succeeded: 22
  skipped: 0
  duration: 46s 041ms
```

---

## 3. Discovered & Executed E2E Tests (22 Tests Total)

### Discover Command
```powershell
dotnet test UI\BrainOS.E2E.Tests\BrainOS.E2E.Tests.csproj --list-tests
```

### Discovered Tests List
1. **Creator drafts, compiles, tests, and promotes a neuron for the email-senders intent**
2. **find-a-youtube-video routes to the YouTube neuron and renders a VideoPlayerCard**
3. **PushFlutterPerf yields a FlutterPerfCard on the home feed**
4. **Sustained red samples emit exactly one VisualLoadHint**
5. **Per-client isolation routes hints by client_id**
6. **open-the-whiteboard routes to the Canvas neuron and renders a CanvasCard**
7. **Programmatic packaging, listing, purchasing, entitlement activation, and successful VIP travel planning**
8. **BrainOS.E2E.Tests.Ui.AdaptiveContainerRfwTests.Sample_neuron_uses_AdaptiveContainer_with_compact_and_medium_children**
9. **BrainOS.E2E.Tests.Ui.CodeEditorRfwTests.CodeEditor_is_registered_in_BrainOS_dictionary**
10. **BrainOS.E2E.Tests.Ui.CounterRfwTests.Counter_is_registered_in_BrainOS_dictionary**
11. **BrainOS.E2E.Tests.Ui.GlowIconRfwTests.GlowIcon_is_registered_in_BrainOS_dictionary**
12. **BrainOS.E2E.Tests.Ui.ImportBoundaryTests.DigitalBrainUi_does_not_import_from_app_layers**
13. **BrainOS.E2E.Tests.Ui.InoSourceCardRfwTests.InoEditorCard_RFW_source_declares_widget_with_Split_PromptInput_CodeEditor**
14. **BrainOS.E2E.Tests.Ui.InoSourceCardScenarioContract.Inocode_contains_scenario_block (source 1)**
15. **BrainOS.E2E.Tests.Ui.InoSourceCardScenarioContract.Inocode_contains_scenario_block (source 2)**
16. **BrainOS.E2E.Tests.Ui.InoSourceCardScenarioContract.Empty_chunks_fails_the_scenario_contract**
17. **BrainOS.E2E.Tests.Ui.PromptInputRfwTests.PromptInput_is_registered_in_BrainOS_dictionary**
18. **BrainOS.E2E.Tests.Ui.SplitRfwTests.Split_is_registered_in_BrainOS_dictionary**
19. **BrainOS.E2E.Tests.Ui.SynapseStreamRfwTests.SynapseStream_is_registered_in_BrainOS_dictionary**
20. **BrainOS.E2E.Tests.Ui.TaskManagerCardRfwTests.TaskManagerCard_RFW_source_declares_widget**
21. **BrainOS.E2E.Tests.Ui.TaskRowRfwTests.TaskRow_is_registered_in_BrainOS_dictionary**
22. **BrainOS.E2E.Tests.RfwHost.RfwSurfaceContractTests.SampleDocument_uses_only_bare_names_and_known_event**

All 22 tests compiled successfully, ran without errors, and passed perfectly!
