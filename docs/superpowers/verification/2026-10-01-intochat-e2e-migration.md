# IntoChat E2E migration verification — 2026-10-01

## Scope and ownership

The plan `docs/superpowers/plans/2026-10-01-intochat-e2e-migration.md` governs this migration. All 16 moved fact files preserve their scenario bodies, attributes, gates, timeouts and assertions: an automated comparison against `ebb831506^` matched after normalizing only namespaces/imports, fixture/builder names, the HTTP class rename, Playwright's required `global::` qualification, and whitespace. No scenario was deleted. The only duplicate deleted is `ResearcherPackage.cs`, identical to the existing Apps fixture apart from namespace.

- Apps retains both package-sharing layers: the existing grain-contract fact and the moved HTTP fact (renamed `PackageSharingHttpFacts`). HTTP authorization, foreign-workspace rejection, listing, upgrades and uninstall assertions remain. `ShareCSharpFacts` stays in Apps because its substantive contract is sharing packages without private settings and installing with the recipient's account; sandbox execution is part of that journey.
- `LocalAppsJourneyFacts` goes to a new **Files E2E** project, rather than Apps. It exercises Files' immutable assets, image-save coordinator, image-editing surface, saved copy, original-file preservation and replay idempotence. The Files internal-access grant moves from IntoChat to Files E2E. Flutter is the browser connector, not the owner of these contracts.
- `ReceiptJourneyFacts` goes to **Assistant E2E** because it drives `/agent` and checks receipts in that stream, cross-checking durable usage. Compute implementation and contracts are unchanged.
- Flutter's browser persistence fact joins the suite containing `ShellPersistenceHttpFacts`; neither fact duplicates the other's assertions, so both remain.
- `LeadData` remains shared: Assistant, Supabase and Core telemetry scenarios use it.

## Reference composition

`DigitalBrain.Testing.E2E/Reference` owns the composed fixture, lease/reset base, scripted model, cookie clients, browser/upload helpers and diagnostics. `ReferenceBrain` independently declares the product's module set, including `CSharpModule` and authoring. It has no reference or linked source pointing into Applications. Product `ProfileCompositionFacts` now compares the resolved module types with the reference composition.

The reference path uses the real Identity cookie/session middleware and the copied host telemetry policy. Ordinary minimal module hosts retain their previous open-owner behavior. No shipped content or `intochat` publisher identity moves into a module. IntoChat's thin fixture override alone starts the product AppHost and publishes its settings package.

Aspire's caller-stack discovery needed a build-transitive factory compiled into each consumer, so an assembly fixture in shared support can locate that consumer's machine-local Aspire metadata. Sequential browser suites also exposed stale Flutter bundles pointing at the previous runtime port; reference startup now waits for the served bundle to contain the current runtime endpoint before yielding a lease.

## Commit slices

| Slice | Commit | Build evidence |
| --- | --- | --- |
| 1. Harness promotion | `ebb831506` | Testing.E2E and IntoChat E2E green |
| 2. Apps dedup/moves | `a5b8d7400` | Apps and IntoChat E2E green |
| 3. Assistant/AI | `805682cc1` | Assistant and IntoChat E2E green |
| 4. Flutter/Supabase/Files | `735b8bd26` | All three receivers and IntoChat E2E green |
| 5. Security/diagnostics | `fe4024c7a` | Identity, Core and IntoChat E2E green |
| 6. Product shrink/pruning and verification | This commit | Final results below |

All builds and tests target individual projects. The solution file only registers the four new projects; it was never built or tested.

## Verification results

Verification is in progress; final per-project results and Aspire resource evidence will replace this paragraph before the last commit.

## Per-file disposition

Source paths below are relative to `src/Applications/IntoChat/Tests/E2E/`; destination paths are repository-relative. This covers every originally tracked file, including project and runner configuration.

| Original file | Destination | Disposition |
| --- | --- | --- |
| `Agent/AgentModelsHttpFacts.cs` | `src/Modules/DigitalBrain/Assistant/DigitalBrain.Modules.Assistant.Tests.E2E/AgentModelsHttpFacts.cs` | Moved intact |
| `Agent/AgentTableJourneyFacts.cs` | `src/Modules/DigitalBrain/Assistant/DigitalBrain.Modules.Assistant.Tests.E2E/AgentTableJourneyFacts.cs` | Moved intact |
| `Agent/AgentWorkflowFacts.cs` | `src/Modules/DigitalBrain/Assistant/DigitalBrain.Modules.Assistant.Tests.E2E/AgentWorkflowFacts.cs` | Moved intact |
| `Agent/GoldenJourneyFacts.cs` | `src/Applications/IntoChat/Tests/E2E/Agent/GoldenJourneyFacts.cs` | Retained |
| `Agent/ScriptedModelServer.cs` | `src/Testing/DigitalBrain.Testing.E2E/Reference/Agent/ScriptedModelServer.cs` | Moved; public shared support |
| `Apps/AssistantNeuronUiFacts.cs` | `src/Modules/DigitalBrain/Assistant/DigitalBrain.Modules.Assistant.Tests.E2E/AssistantNeuronUiFacts.cs` | Moved intact |
| `Apps/BuiltInAppRoutesFacts.cs` | `src/Applications/IntoChat/Tests/E2E/Apps/BuiltInAppRoutesFacts.cs` | Retained |
| `AssemblyInfo.cs` | `src/Applications/IntoChat/Tests/E2E/AssemblyInfo.cs` | Retained product fixture and serialization; each receiver wires reference fixture |
| `BrainFact.cs` | `src/Testing/DigitalBrain.Testing.E2E/Reference/BrainFact.cs` | Moved; public shared support |
| `Composition/ApplicationStartupFacts.cs` | `src/Applications/IntoChat/Tests/E2E/Composition/ApplicationStartupFacts.cs` | Retained |
| `Composition/ProfileCompositionFacts.cs` | `src/Applications/IntoChat/Tests/E2E/Composition/ProfileCompositionFacts.cs` | Retained; adds reference/product module-set parity assertion |
| `Diagnostics/OtlpTraceParser.cs` | `src/Testing/DigitalBrain.Testing.E2E/Reference/Diagnostics/OtlpTraceParser.cs` | Moved; public shared support |
| `Diagnostics/TestTelemetryCollector.cs` | `src/Testing/DigitalBrain.Testing.E2E/Reference/Diagnostics/TestTelemetryCollector.cs` | Moved; public shared support |
| `Diagnostics/TraceAssertions.cs` | `src/Testing/DigitalBrain.Testing.E2E/Reference/Diagnostics/TraceAssertions.cs` | Moved; public shared support |
| `Diagnostics/TraceBudgetFacts.cs` | `src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.E2E/TraceBudgetFacts.cs` | Moved intact |
| `IntoChat.Tests.E2E.csproj` | `src/Applications/IntoChat/Tests/E2E/IntoChat.Tests.E2E.csproj` | Retained; pruned moved-test-only references |
| `IntoChatE2ETest.cs` | `src/Applications/IntoChat/Tests/E2E/IntoChatE2ETest.cs` | Retained product-host builder and shipped-package wait; independent ReferenceBrain mirrors composition |
| `IntoChatHostFixture.cs` | `src/Testing/DigitalBrain.Testing.E2E/Reference/ReferenceBrainFixture.cs` | Promoted; original path retains thin shipped-content override |
| `LocalApps/LocalAppsJourneyFacts.cs` | `src/Modules/Files/DigitalBrain.Modules.Files.Tests.E2E/LocalAppsJourneyFacts.cs` | Moved intact; Files owns image editing/save and asset contracts |
| `LocalApps/WorkspaceUploadFixture.cs` | `src/Testing/DigitalBrain.Testing.E2E/Reference/LocalApps/WorkspaceUploadFixture.cs` | Moved; public shared support |
| `Packages/PackageSharingFacts.cs` | `src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Tests.E2E/PackageSharingHttpFacts.cs` | Merged suite; retained HTTP half alongside existing grain facts |
| `Packages/People.cs` | `src/Testing/DigitalBrain.Testing.E2E/Reference/Packages/People.cs` | Moved; public shared support |
| `Packages/ResearcherPackage.cs` | `src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Tests.E2E/ResearcherPackage.cs` | Deleted duplicate; existing module copy identical except namespace |
| `Packages/ShareCSharpFacts.cs` | `src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Tests.E2E/ShareCSharpFacts.cs` | Moved intact |
| `Packages/SignedInPerson.cs` | `src/Testing/DigitalBrain.Testing.E2E/Reference/Packages/SignedInPerson.cs` | Moved; public shared support |
| `Receipts/ReceiptJourneyFacts.cs` | `src/Modules/DigitalBrain/Assistant/DigitalBrain.Modules.Assistant.Tests.E2E/ReceiptJourneyFacts.cs` | Moved intact; Assistant /agent receipt stream |
| `Security/CanarySecretFacts.cs` | `src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.E2E/CanarySecretFacts.cs` | Moved intact |
| `Security/ContentCaptureFacts.cs` | `src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.E2E/ContentCaptureFacts.cs` | Moved intact |
| `Security/CrossWorkspaceFacts.cs` | `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity.Tests.E2E/CrossWorkspaceFacts.cs` | Moved intact |
| `Security/GrantRevokeFacts.cs` | `src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity.Tests.E2E/GrantRevokeFacts.cs` | Moved intact |
| `Workspace/LeadData.cs` | `src/Testing/DigitalBrain.Testing.E2E/Reference/Workspace/LeadData.cs` | Moved; public shared support |
| `Workspace/ShellPersistenceFacts.cs` | `src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Tests.E2E/ShellPersistenceFacts.cs` | Merged suite; browser half retained beside existing HTTP durability facts |
| `Workspace/SupabaseTableDisplayFacts.cs` | `src/Modules/Supabase/DigitalBrain.Modules.Supabase.Tests.E2E/SupabaseTableDisplayFacts.cs` | Moved intact |
| `Workspace/WorkspaceBrowser.cs` | `src/Testing/DigitalBrain.Testing.E2E/Reference/Workspace/WorkspaceBrowser.cs` | Moved; public shared support |
| `Workspace/WorkspaceRestoreFacts.cs` | `src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Tests.E2E/WorkspaceRestoreFacts.cs` | Moved intact |
| `testconfig.json` | `src/Applications/IntoChat/Tests/E2E/testconfig.json` | Retained |
