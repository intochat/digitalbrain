## 2026-05-23T05:11:16Z
You are the Google Tests Hotfix Worker.
Your working directory is e:/digitalbrain/.agents/worker_google_hotfix.
Your role is to execute the Google Tests Hotfix task based on the detailed hotfix plan.

### Technical Plan
Please read the plan first:
- Hotfix Plan: e:/digitalbrain/.agents/orchestrator/google_tests_hotfix_plan.md

### MANDATORY INTEGRITY WARNING
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

### Implementation Tasks
Follow these phases precisely:

1. **Phase 1: Environment Cleanup**
   - Identify and stop conflicting background Orleans/AppHost/DigitalBrain processes to avoid file locking:
     `Get-Process -Name BrainOS*, DigitalBrain* -ErrorAction SilentlyContinue | Stop-Process -Force`

2. **Phase 2: Apply Hotfix Changes**
   - **Stripe & Telegram bot token configuration**:
     Modify `sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/Support/TestDependencies.cs`. Pre-inject `BrainOS__Stripe__WebhookSecret` to `"whsec_test"` and `BrainOS__Telegram__BotToken` to `"mock-token"` using `.WithEnvironmentOverride()`.
   - **Telegram Alert Neuron Acknowledgment**:
     Modify `sdk/DigitalBrain.SDK/Google/Telegram/TelegramAlertNeuron.cs`. In `HandleAsync`, use `try { ... } finally { await FireSynapseAsync(request with { ReceiverNeuronType = request.CallerNeuronType ?? "External" }); }` (or similar) to ensure an acknowledgment synapse with the same correlation ID is fired back to the caller to avoid gateway timeouts.
   - **Gmail Digest Neuron Caller Type Hydration**:
     Modify `sdk/DigitalBrain.SDK/Google/Digest/GmailDigestNeuron.cs`. In `HandleStartAsync` (line 57) and `HandleSendersAsync` (line 87), change `CallerNeuronType: null` to `CallerNeuronType: GmailDigestNeuronType`.

3. **Phase 3: Compile and Test**
   - Build the fast solution and Google tests:
     `dotnet build BrainOS.Fast.slnx /nodeReuse:false`
     `dotnet build sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj /nodeReuse:false`
   - Run the isolated Google tests:
     `dotnet test sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj`
   - Assert all 11 tests in the Google test suite pass cleanly!
   - Run the fast unit tests to ensure no regressions:
     `dotnet test BrainOS.Fast.slnx --no-build`

4. **Phase 4: Handoff Report**
   - Write a detailed `handoff.md` in your working directory. Detail the files changed, the commands run, the compile outcomes, and the test run results (100% pass!).
   - Send me (your parent orchestrator) a message when your handoff is complete.
