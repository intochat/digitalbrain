# Marketplace Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Make Apps own authoring and publishing while IntoChat supplies content and publisher configuration.
**Architecture:** Introduce a provider-neutral sandbox contract, then move contracts and implementation without changing routes or wire shapes. Runtime descriptions and shipped sources remove host-specific knowledge from module code.
**Tech Stack:** C#, Orleans, ASP.NET Core, xUnit, Aspire, Flutter.
**Spec:** `docs/superpowers/specs/2026-10-01-intochat-cleanup-marketplace-design.md`

## Global Constraints

- Execute two phases as separate commit series; this is phase 2 after the test cleanup plan.
- Preserve existing uncommitted master-key work.
- Preserve Orleans field IDs, grain keys, signal provenance, and JSON field shapes.
- Pin AppDraftStatus to Empty=0, Drafted=1, Building=2, Published=3, Failed=4.
- Run dotnet test per affected project, never the solution.
- Do not run the 18-minute IntoChat E2E suite; compile it to validate moved/shared support code.
- Flutter changes require flutter analyze and flutter test in the shell directory.

## Review Focus

- Sandbox absent: capabilities and authoring endpoints compose cleanly and builds reject before entering Building (tasks 1, 6).
- Contract namespace relocation: persisted draft reactivation retains values and ownership (task 2).
- Cross-principal draft/brain access: reject foreign access after endpoint relocation (task 3).
- Runtime registration changes: prompts describe the registered provider rather than unavailable implementations (task 4).
- Alternate shipped publisher and repeated startup: stamp its identity and avoid duplicate content commits (task 5).

## Task 1: Sandbox contract

**Files:** create `src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/Authoring/IScriptSandbox.cs`; create CSharp `Authoring/CSharpScriptSandbox.cs`; modify CSharp registration, IntoChat `Marketplace/{AppDraftNeuron.cs,MarketplaceService.cs}`, `Program.cs`, and Assistant `AssistantModule.cs`.
**Interfaces:**
- `IScriptSandbox`: `bool CanRun { get; }`, `Task<ScriptContractCatalog> ReadContracts(IReadOnlyList<string> modules, CancellationToken cancellationToken)`, `ScriptCompilationCheck Check(IReadOnlyDictionary<string,string> files)`.
- Provider-neutral records: `ScriptContractModule(string Id, string? Directive)`, `ScriptContractCatalog(IReadOnlyList<ScriptContractModule> Modules, IReadOnlyList<string> Contracts, string Example, bool Truncated = false)`, `ScriptCompilationDiagnostic(string File, string Id, int Line, string Message)`, `ScriptCompilationCheck(bool Success, IReadOnlyList<ScriptCompilationDiagnostic> Errors)`.

- [x] Add CSharp unit coverage proving the adapter returns real discovery/check results and reports unavailable when discovery is absent. Run the new test and confirm failure before implementing the adapter.
- [x] Implement adapter delegation and DTO mapping, preserving JSON property names. Register IScriptSandbox only through CSharp; consumers treat absent service as unavailable. Replace authoring concrete calls and both capability type checks with contract availability.
- [x] Run CSharp Unit, IntoChat Unit, Assistant Unit separately; build IntoChat. Verify no CSharpToolService references remain in host authoring. Commit `refactor: expose app authoring through a sandbox contract`.

## Task 2: Draft wire contracts

**Files:** move IntoChat `Marketplace/{IAppDraft.cs,IAppDrafts.cs,AppDraftView.cs,AppDraftState.cs,AppDraftStatus.cs,AppDraftAttempt.cs,AppDraftEntry.cs,AppDraftsState.cs,AppDraftChanged.cs,AppDraftsChanged.cs}` into Apps.Contracts `Authoring/`; extract AppSpecView from MarketplaceService into the same folder. Update all consumers and Flutter screens only for actual wire changes.
**Interfaces:** Existing members unchanged; types use namespace `DigitalBrain.Apps`. Add explicit enum numeric assignments. Retain serialized aliases including `intochat.app-draft-state` and `intochat.app-draft-view`. Convert durable Attempts storage to a concrete array without changing its field ID or JSON array shape, adapting constructors/consumers together.

- [x] Capture current serialized JSON and Orleans type identities; add Apps contract coverage for unchanged draft status numbers and wire fields plus draft reactivation with existing state fields. Confirm relocation would expose identity mismatches before adjusting aliases.
- [x] Relocate types and update imports. Keep concrete arrays for durable state and all Id attributes unchanged. Update screens in the same change if shapes must change; prefer preserving shapes.
- [x] Run Apps Unit and IntoChat Unit; build IntoChat E2E. Run Flutter analyze/test if Dart changed. Commit `refactor: move draft contracts into Apps`.

## Task 3: Authoring implementation and routes

**Files:** move remaining draft neurons, BuilderTools, AgentPrompts, MarketplaceService, and MarketplaceEndpoints from IntoChat Marketplace into Apps `Authoring/`; move AppRuntimes into Apps `Runtimes/{GroupChatRuntime.cs,PromptRuntime.cs}`. Modify AppsModule, AppsEndpoints, host Program, Apps csproj, and Assistant `Packages/{InstalledPackages.cs,PackageEndpoints.cs}`; extract its PackageRouteGuard into Apps `Packages/PackageRouteGuard.cs`.
**Interfaces:** Move `InstalledPackages.AppKey(PackageId id)` into Apps without changing `BrainScope.CurrentId() + "/packages/" + id`; move the dependency-free PackageRouteGuard into Apps. Keep the existing runtime `Name` and `Answer` signatures. Module configuration registers authoring services and both runtimes and maps existing routes once.

- [x] Add Apps route tests for registered draft/spec/verify/discussion paths, principal-scoped draft keys, invalid GUID rejection, and foreign brain membership. Run to confirm routes are initially absent from module-only composition.
- [x] Move implementation and small shared routing responsibilities into Apps. Remove host AddMarketplace/MapMarketplace calls; temporarily retain shipped publisher registration at the host until task 5. Remove any dependency cycle instead of adding Apps→Assistant.
- [x] Move draft behavior tests and their scripted runner into Apps Unit now that the code belongs there; keep embedded host content tests in IntoChat. Run Apps, Assistant, and IntoChat Unit separately; build IntoChat E2E. Commit `refactor: make Apps own authoring and runtime routes`.

## Task 4: Runtime descriptions drive prompts

**Files:** Apps `Runtimes/{IAppRuntime.cs,GroupChatRuntime.cs,PromptRuntime.cs}`, `Authoring/{AgentPrompts.cs,AppDraftNeuron.cs}`, sandbox contract/adapter, and runtime tests.
**Interfaces:** Extend `IAppRuntime` with `string AuthoringDescription { get; }`. Extend `IScriptSandbox` with the same property for the built-in script runtime. Prompt assembly consumes available runtime descriptions; GroupChatRuntime remains the single owner of `ChatKey(string appKey, Guid invocationId)`.

- [x] Add coverage with an extra test runtime whose description appears in the author/builder context, and an unavailable sandbox whose description does not. Assert runtime behavior still uses its declared settings/files.
- [x] Put group-chat name, `{Name}Model`, groupchat.json and addressing instructions in its description; put prompt files/settings in PromptRuntime; put #:project paths and C# conventions in the sandbox provider description. Remove duplicate instructions from AgentPrompts and shipped-content loader.
- [x] Run Apps and CSharp Unit plus IntoChat Unit. Commit `refactor: generate authoring prompts from runtime descriptions`.

## Task 5: Generic shipped app sources and settings identity

**Files:** move ShippedApps and ShippedAppPublisher into Apps `Shipping/`; create `IShippedAppSource.cs`, `EmbeddedShippedAppSource.cs`, `ShippedAppsServiceCollectionExtensions.cs`; modify host Program and Assistant `Packages/BuiltInSettingsEndpoints.cs`; update ShippedAppFacts.
**Interfaces:** `ShippedApp(PackageId Package, PackageContent Content)`; `IShippedAppSource` exposes `string Publisher { get; }` and `IReadOnlyList<ShippedApp> Load()`; `IServiceCollection AddShippedApps(this IServiceCollection services, Assembly assembly, string resourcePrefix, string publisher)` registers a source plus the publisher once. `BuiltInSettingsOptions` contains nullable `PackageId Package`; host configures `intochat/settings` explicitly.

- [x] Add source/publisher tests with two distinct publishers, unchanged embedded resources, repeated startup, a red verification, and ShipOnStartup=false. Assert alternate identity is stamped, same content produces no extra revision, red stays unpublished, and false ships nothing. Add Assistant coverage for absent configured settings returning ServiceUnavailable.
- [x] Implement reusable loading/publishing and preserve selection semantics, newline normalization, sorted-file equality, and cancellation. Stamp each source's publisher during its work. Host calls AddShippedApps with its assembly/prefix/intochat; remove ShippedPublisher from Assistant and inject the configured settings package.
- [x] Run Apps, Assistant, and IntoChat Unit. Build IntoChat E2E; update fixtures to use the generic API. Commit `refactor: configure shipped apps and built-in package identity at the host`.

## Task 6: Consolidated availability, configuration, and acceptance

**Files:** Apps `Authoring/AppAuthoringPolicy.cs` (new), AppDraftNeuron, MarketplaceService, host configuration/fixtures, CLAUDE.md.
**Interfaces:** `AppAuthoringPolicy.RequireSandbox()` validates `IScriptSandbox?.CanRun == true`; `RequireRuntime(string runtime)` validates known runtime registration and sandbox when required. Preserve the existing sandbox-unavailable message. Use `DigitalBrain:Apps:AuthorModel`, `DigitalBrain:Apps:BuilderModel`, and existing `DigitalBrain:Apps:ShipOnStartup`.

- [x] Add absence/composition tests for draft build, tested revision verification, and untested non-CSharp revisions. Missing sandbox must reject required operations without stranding state; non-script content remains readable.
- [x] Consolidate gates into the policy, normalize every configuration producer/consumer, and update CLAUDE.md authoring locations. Remove the host Marketplace directory after all consumers move.
- [x] Run every affected project test suite separately and build IntoChat E2E. If Dart changed, run flutter analyze and flutter test from `src/Modules/Google/Flutter/app/shell`.
- [x] Run Aspire from IntoChat/AppHost until all resources are Healthy, following the orchestration skill. Record limitations as failures to verify, never as successful smoke results.
- [x] Run a whole-change code review before finishing; fix actionable issues and repeat only affected verification. Check no host publisher literal remains in modules, no host authoring concrete sandbox references remain, and no persisted IDs/wire fields drifted. Commit `refactor: consolidate app authoring policy and configuration` with verification notes.

Execution evidence and documented adaptations: see ../verification/2026-10-01-intochat-test-cleanup.md and ../verification/2026-10-01-marketplace-extraction.md.
