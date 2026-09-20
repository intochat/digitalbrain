# Code-first Composition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for inline execution, or superpowers:subagent-driven-development if the user selects delegation. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore explicit configuration of every application module and give AppHost, Unit, Integration and E2E one typed composition vocabulary.

**Architecture:** Module callbacks build host-independent declarations. Module-owned adapters validate and compile those declarations before Aspire materializes resources or an in-process host starts. E2E overrides the actual application's declarations rather than supplying a replacement application configuration.

**Tech Stack:** Repository-pinned .NET 11 prerelease, Orleans, Aspire 13.5.3, xUnit v3/Microsoft.Testing.Platform, Playwright and Flutter. No upgrades.

**Spec:** [Approved option A and workspace design](../specs/2026-09-20-code-first-composition-and-agent-data-design.md). Part 2 is [the interactive agent data window](2026-09-20-agent-data-window.md).

## Global Constraints

- Work on `archv2`; never push. Preserve existing stashes and unrelated changes. If an isolated execution checkout is needed, follow the worktree skill and return the work to `archv2` only when requested.
- Keep exactly four public testing libraries; retain the previous internal runner/hosting packaging fallback.
- Use one public declaration verb, `WithModule`, and `ConfigureModule` for changing an existing module. No permanent parallel AddModule authoring system.
- Code selects modules, providers and resource modes. External configuration only supplies explicitly selected values/references; no application-wide topology binding.
- Module option classes own their shape. There is no central application class with one property per module.
- Preserve deep configuration, private secrets, dependency resolution, real external process isolation, native Playwright access and explicit browser mode.
- Callbacks execute locally while building declarations. Never serialize delegates, service instances or Aspire resource builders.
- Build/test with `-p:CodeGraphRefresh=false`; use the repository's pinned test runner.
- The previous post-merge broad gate had one intermittent Aspire cleanup failure in InboxHttpFacts; keep that evidence, do not use automatic retries to conceal recurrence.

## Review Focus

1. Optional provider fields absent from defaults must survive transport when explicitly supplied; pin in tasks 1–2.
2. E2E overrides targeting an absent module must fail rather than silently adding it; pin in task 3.
3. Reusing a builder or mutating its captured options must not change a built/running composition; pin in task 1.
4. Restoring all modules must not make the default CI suite download models or contact live accounts; pin in tasks 4–5.
5. Generated resource values must reach runtime references without being logged or accepted as arbitrary user overrides of Orleans/storage identity; pin in tasks 2–3.

## Interface decisions

Names below are implementation targets. Add no synonym unless an existing public consumer genuinely requires migration support; all repository callers migrate before completion.

```csharp
public sealed class BrainCompositionBuilder
{
    public BrainCompositionBuilder WithModule<TModule>(
        Action<ModuleConfiguration<TModule>>? configure = null) where TModule : class, IModule, new();
    public BrainCompositionBuilder ConfigureModule<TModule>(
        Action<ModuleConfiguration<TModule>> configure) where TModule : class, IModule, new();
    public BrainComposition Build();
}

public sealed class BrainComposition
{
    public IReadOnlyList<ModuleDefinition> Modules { get; }
}

public sealed class ModuleConfiguration<TModule> where TModule : class, IModule, new()
{
    // Infrastructure used by module-owned extensions; ordinary callers use typed With... methods.
    public void ConfigureOptions<TOptions>(Action<TOptions> configure, params string[] assignedMembers)
        where TOptions : class, new();
    public void ReplaceOptions<TOptions>(TOptions options) where TOptions : class, new();
}

public interface IModuleConfigurationContract
{
    Type ModuleType { get; }
    Type OptionsType { get; }
    object CreateDefaults();
    object Copy(object options);
    ModuleDefinition Compile(object options);
    object ApplyOverride(object baseline, string json);
    string WriteOverride(object configured, IReadOnlyCollection<string> assignedMembers);
}
```

Associate one contract with each module through a trusted local `ModuleConfigurationAttribute(Type contractType)`. Module-specific contracts implement validation/copy/serialization in their own assembly. A settings-free module requires no options class or attribute. ConfigureOptions/ReplaceOptions check the registered type; do not store arbitrary option types in an object bag. Internal reflection locates a locally declared contract, never a remote supplied implementation type.

Reuse `ModuleDefinition`/`ModuleComposition` as compiled runtime output. Typed authoring and immutable snapshots sit above that existing implementation; do not rebuild the runtime module system. Module hosting choices compile into module-owned configuration fields consumed by the corresponding hosting adapter. Add explicit mapping for each supported field rather than generic property-name inference. Internal serializable override documents preserve omitted fields, explicit false/zero and explicit clearing of optional values. Fluent setters record the members they explicitly assign, even when assigning the module default. Never compute E2E patches solely by diffing against defaults: the test has not seen the application's draft. ReplaceOptions is an explicit whole-options replacement and marks every contract member assigned; module-specific WithOptions(value) exposes that meaning. Contracts validate assigned member names and reject unknown ones.

Defaults → application callback → test override → module validation/compilation. A callback chain mutates only its private draft. Build copies options, resolves dependencies, freezes the builder and returns the same immutable snapshot on repeated Build. Further mutation throws. Explicit duplicate declarations throw; equivalent transitive requirements deduplicate.

### Task 1: Implement composition drafts, contracts and freezing

**Files:** Create `src/Modules/DigitalBrain/DigitalBrain/Composition/{BrainComposition,BrainCompositionBuilder,ModuleConfiguration,IModuleConfigurationContract,ModuleConfigurationAttribute}.cs`. Modify `Composition/ModuleDefinition.cs` only where compilation needs metadata. Create `src/Testing/DigitalBrain.Testing.Framework.Tests/CompositionBuilderFacts.cs`.

**Interfaces:** Produce the signatures above. Keep `ModuleComposition.Resolve` authoritative for dependencies and conflicting compiled settings.

- [x] Write failing tests with a small test module/contract. Verify defaults, explicit override, unknown configuration type, duplicate declarations, missing override target, conflict validation, freezing and copy isolation. Test an optional endpoint absent from defaults, false and zero values, and explicit optional-value clearing.

```csharp
var draft = new BrainCompositionBuilder().WithModule<ExampleModule>();
Assert.Throws<InvalidOperationException>(() => draft.WithModule<ExampleModule>());
Assert.Throws<InvalidOperationException>(() => draft.ConfigureModule<OtherModule>(_ => { }));
var snapshot = draft.Build();
Assert.Same(snapshot, draft.Build());
Assert.Throws<InvalidOperationException>(() => draft.ConfigureModule<ExampleModule>(_ => { }));
```

`ExampleModule` and `OtherModule` are test-local `IModule` implementations. The mutable-options test retains a reference from the callback, changes it after Build, and asserts the snapshot's compiled value is unchanged.

- [x] Run `dotnet test --project src/Testing/DigitalBrain.Testing.Framework.Tests/DigitalBrain.Testing.Framework.Tests.csproj -p:CodeGraphRefresh=false -- --filter-class '*CompositionBuilderFacts'`; confirm nonzero selection and expected missing-interface failures.
- [x] Implement draft storage, trusted contract discovery, module-owned copy/compile and freeze semantics. No hosting side effects in these types.
- [x] Run the new cases and the existing composition/transport tests. Commit the independently tested core.

### Task 2: Port AI, Supabase and Flutter declarations through real adapters

**Files:** Create `Configuration/{AIModuleConfiguration,AIConfigurationContract}.cs` in `src/Modules/AI/AI`; equivalent `SupabaseModuleConfiguration/SupabaseConfigurationContract` in `src/Modules/Supabase/Supabase/Configuration` and `FlutterModuleConfiguration/FlutterConfigurationContract` in `src/Modules/Flutter/Flutter/Configuration`. Modify their module files and options. Modify `src/Modules/DigitalBrain/Aspire.Hosting/Brain/{DigitalBrainBuilder,DigitalBrainHostingExtensions}.cs`, AI/Supabase `Aspire.Hosting/*HostingExtensions.cs`, Flutter `Aspire.Hosting/{FlutterModuleHosting,ShellHostingExtensions}.cs`. Create `src/Testing/DigitalBrain.Testing.Framework.Tests/CompositionHostingFacts.cs`.

**Interfaces:** Module methods return the same `ModuleConfiguration<TModule>`. AI: `WithLlm<TModel>()`, `WithDefaultLlm<TModel>()`, `WithDefaultEmbedding<TModel>()`, `WithVoiceToText<TModel>()`, `WithTavilySearch()`, `WithModelEndpoint(AiProvider provider, Uri endpoint)`, `WithoutLocalModels()`, `WithoutVoiceToText()`, `WithoutWebSearch()`. Flutter: `WithWebHost()`, `WithWindowHost()`, `WithHeadlessHost()`, `WithoutHost()` and `WithOptions(FlutterModuleOptions options)` for explicit complete replacement. Supabase: `WithConnection(string name)` and `WithPostgres()`. `WithPostgres` means a run-scoped PostgreSQL resource projected under the Supabase connection name, not the full Supabase service stack. Repeated provider/host mode methods replace that one choice, not append another host. Convert authoring options to mutable draft classes where callbacks require mutation; contract copies isolate them from compiled snapshots.

- [x] Add failing hosting-model tests. A Web override of Window yields one web resource and zero native processes; selected AI endpoint/default/model/profile values reach the runtime projection; Supabase gets one generated private connection reference. Verify callbacks create no module resource before finalization.

```csharp
var composition = new BrainCompositionBuilder()
    .WithModule<FlutterModule>(f => f.WithWindowHost())
    .ConfigureModule<FlutterModule>(f => f.WithWebHost())
    .WithModule<SupabaseModule>(s => s.WithPostgres())
    .Build();
Assert.Equal("Web", composition.Modules.Single(m => m.ModuleType == typeof(FlutterModule))
    .Configuration["DigitalBrain:Flutter:Hosting:Kind"]);
```

- [x] Map the complete existing AI option shape, including provider endpoints, profiles, default reasoning/output limits/capabilities and selected model resources. Separate secret references from public values. Fix the current omission of provider endpoints in `AIModule.Define` through the new contract.
- [x] Buffer module declarations in `DigitalBrainBuilder`. The first `WithReference(brain)` or `WithReference(brain.AsClient())` compiles and materializes once; subsequent references reuse the result, later configuration throws. Both reference paths must finalize. Existing core storage creation may remain in AddDigitalBrain; module resource creation waits for validation.
- [x] Move existing eager hosting extensions behind adapters. Keep their working resource implementations; remove their role as a second user-facing configuration mechanism after migration.
- [x] Use the pinned Aspire PostgreSQL integration and module-owned connection projection. Validate SQL endpoint readiness before runtime startup; never put its resolved credential into public composition serialization.
- [x] Verify native Dart/desktop mode declarations by resource model assertions and Web by the existing real browser smoke. Unit compilation does not interpret host modes as permission to launch processes. Unsupported explicit hosted-only operations in Unit report a capability error.
- [x] Run framework tests, AI unit tests, Supabase unit tests and Flutter unit/integration suites. Commit after the model/projection checks pass.

### Task 3: Give test entry points the same authoring contract

**Files:** Modify `src/Testing/DigitalBrain.Testing.Unit/{UnitTest,UnitOptions}.cs`, `DigitalBrain.Testing.Integration/{IntegrationTest,IntegrationOptions}.cs`, `DigitalBrain.Testing.E2E/{E2ETest,E2EOptions}.cs`, `DigitalBrain.Testing.Hosting/AspireTestSession.cs`, `DigitalBrain.Testing.ModuleAppHost/AppHost.cs`; create sibling `UnitTestBuilder.cs`, `IntegrationTestBuilder.cs`, `E2ETestBuilder.cs`. Replace `src/Modules/DigitalBrain/DigitalBrain/Composition/ApplicationConfigurationTransport.cs` with `CompositionOverrideTransport.cs`. Create framework `TestCompositionFacts.cs` and integration `CompositionTransportFacts.cs`.

**Interfaces:** `UnitTest.Create(): UnitTestBuilder`; `IntegrationTest.Create(): IntegrationTestBuilder`; `E2ETest.For<TAppHost>(): E2ETestBuilder<TAppHost>`. Unit/Integration expose `WithModule<T>`, `ConfigureModule<T>`, `WithExecution(TestExecutionOptions)` and `StartAsync(CancellationToken)`. Unit also retains typed silo/client callbacks and reminders. E2E exposes `ConfigureModule<T>`, `WithExecution`, `WithBrowser(BrowserOptions)`, `StartAsync`; it does not expose module addition. Preserve existing concrete brain return types/capabilities.

- [x] Write a builder test asserting a supplied browser option reaches the resolver unchanged and execution budgets reach probes. Write a hosted test selecting a non-default public endpoint absent from module defaults, confirming the runtime consumes it.
- [x] Implement a versioned overrides envelope: stable module ID plus module-owned option patch, strict size bound, allowed target modules, reserved-key and secret rejection. Validate optional fields by module contract, not equality with default key sets. Apply patches to application drafts before materialization. Invoke callbacks once while creating the patch; no delegates cross the process boundary.
- [x] Test that an E2E patch for Supabase fails when the AppHost lacks Supabase; the test must not add it. Verify malformed/oversized envelopes fail with redacted actionable errors.
- [x] Implement module-specific Unit `WithProvider<TProvider>()` for Supabase and retain local silo callbacks for other fakes. Consume that descriptor as a local replacement after normal module registration. Reject this local-only substitution in hosted modes unless a compiled provider is explicitly included and resolved in the existing external bundle. The planned E2E uses HTTP fixtures, so no application test-assembly loader is added.
- [x] Preserve single session lifetime, cleanup budget, cancellation, process identity and trace behavior. Run the existing runtime restart/isolation/provider tests unchanged in semantics.
- [x] Migrate enough representative callers to prove each builder; keep any temporary compatibility code confined to this migration and remove it in task 5. Commit passing infrastructure and representative cases.

### Task 4: Restore the complete application module inventory

**Files:** Modify `src/Applications/IntoChat/AppHost/{AppHost.cs,IntoChat.AppHost.csproj}`, runtime `IntoChat.csproj`; add module-owned configuration contracts/extensions and adapter changes alongside each module's existing `Configuration` and `Aspire.Hosting` files. Modify `src/Applications/IntoChat/Tests/{IntoChat.Tests.csproj,ConfigurationFacts.cs}`; create `ApplicationCompositionFacts.cs` and `IntoChatTestDeployment.cs` there.

**Interfaces:** New extensions on module-specific drafts preserve these choices:

| Module | Explicit application declaration | E2E deployment choice |
|---|---|---|
| AI | selected/default LLM, embedding, voice, search | explicit OpenAI-compatible fixture model/endpoint; disable local model downloads, voice and web search |
| Memory | `WithQdrant()` | disposable Qdrant, no persistent volume |
| ClickHouse | `WithClickHouse(o => o.WithSeed("leads"))` | disposable ClickHouse, deterministic seed |
| Supabase | `WithConnection("supabase")` | disposable PostgreSQL |
| Time | no options | unchanged |
| Excel | no options | unchanged |
| Google | `WithGmail()` | local OAuth fixture, synthetic private credentials |
| Salesforce | `WithHostedMcp()` | local protocol fixture through typed endpoint override; no live account |
| Microsoft | `WithAspire(appHostPath).WithGitHubRepositories(repositories)` | `WithoutAspire()` and explicit empty repository selection |
| Coding | `WithSolution(solutionPath)` | fixture solution path; no edits to the developer checkout |
| Flutter | `WithWindowHost()` | `WithWebHost()` or explicit `WithoutHost()` for HTTP tests |

The typed endpoint/deactivation methods introduced for this table belong to their module contracts; no catch-all DisableIntegrations flag. Resource modes are test deployment choices, not module removal. Restore the separate MCP resource if its current runtime implementation remains supported; include its endpoint/reference smoke test and do not revive excluded legacy APIs merely to imitate old code.

- [x] Add a failing graph test asserting the eleven production module types are explicitly present, no test module is silently included, and each provider resource/reference is represented. Retain the existing TestTwitterModule registration explicitly labeled as the current demo behavior dependency beside the production list so existing Elon scenarios still exercise the actual application. Removing or replacing that demo is outside this migration; never inject it implicitly from the harness.
- [x] Port each old hosting declaration to the new module-owned contract. For settings-free Time/Excel, add no empty options type. For provider values, test at least one non-default setting at the runtime projection, not only inside a DTO.
- [x] Put the full readable WithModule chain directly in AppHost.cs. Keep runtime, health checks, CORS and MCP resource wiring visible. Bind paths, connection references and repository inputs explicitly.
- [x] Implement `IntoChatTestDeployment.Configure(E2ETestBuilder<Projects.IntoChat_AppHost> builder, Uri modelEndpoint, string solutionPath)` as the test-owned reuse point for the explicit deployment table. It returns the builder and changes providers/resources, never module membership. Own Google/Salesforce fixture lifetimes in an `IAsyncDisposable` test fixture and pass their generated endpoints via typed ConfigureModule calls.
- [x] Verify ordinary application graph uses Window and the same app under the E2E deployment uses Web without duplicate hosts. Assert tests invoke no configured remote AI/OAuth/MCP endpoint. Keep startup health checks active against the test resources.
- [x] Run real application health/HTTP/UI tests plus provider/module tests for migrated adapters. Commit this full application wiring as one reviewable change.

### Task 5: Finish caller migration and remove the old configuration surface

**Files:** All testing callers under `src/Applications`, `src/Modules` and `src/Behaviors`; `src/Testing/README.md`; solution/project references. Remove `src/Applications/IntoChat/Configuration/DigitalBrainConfiguration.cs`, its now-unused `IntoChat.Configuration.csproj` if no code remains, and `IApplicationConfiguration.cs`. Remove superseded E2E input/overloads and public eager AddModule authoring entry points.

- [x] Migrate every Unit/Integration module list and every E2E application object to the finalized builders. Preserve each test's scenario, timeouts and chosen host mode; do not expand live external access.
- [x] Search for stale interfaces and classify internal implementation uses:

```powershell
rg 'DigitalBrainConfiguration|IApplicationConfiguration|AddModule<|E2ETest.StartAsync|Modules\s*=' src
```

- [x] Remove compatibility paths after callers compile. Keep internal ModuleDefinition compilation; callers should not need raw string dictionaries for normal options.
- [x] Document one ordinary Unit test, one real database Integration test and an actual AppHost E2E with visible browser. Explain declaration versus override and the three different execution boundaries.
- [x] Run the complete solution suite and inspect all failures. Investigate the previously seen cleanup failure if it recurs; preserve logs and do not increase deadlines to hide it.
- [x] Commit and record exact results. Part 1 is complete only when the full module graph is explicit, the old configuration object is gone, all existing scenarios pass and the core has no new Aspire/testing dependency.

## Validation commands

```powershell
dotnet test --project src/Testing/DigitalBrain.Testing.Framework.Tests/DigitalBrain.Testing.Framework.Tests.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Testing/DigitalBrain.Testing.Framework.IntegrationTests/DigitalBrain.Testing.Framework.IntegrationTests.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/AI/Tests/DigitalBrain.Modules.AI.Tests.Unit.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Supabase/Tests/DigitalBrain.Modules.Supabase.Tests.Unit.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Flutter/Tests.Unit/DigitalBrain.Modules.Flutter.Tests.Unit.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Flutter/Tests.Integration/DigitalBrain.Modules.Flutter.Tests.Integration.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Applications/IntoChat/Tests/IntoChat.Tests.csproj -p:CodeGraphRefresh=false
dotnet test --solution DigitalBrain.slnx -p:CodeGraphRefresh=false
```

Run new targeted tests red before implementation, then green. Run affected suites when their implementation changes; do not repeat broad gates without a relevant change or unresolved failure. Do not push.
