# Code-first composition and the agent-to-data UI journey

Status: option A approved by the user on 2026-09-20, including the selected interactive workspace table-window direction. Implementation plans are ready for review on `archv2`: [Part 1 — composition](../plans/2026-09-20-code-first-composition.md), [Part 2 — agent data window](../plans/2026-09-20-agent-data-window.md). No product implementation has started. All code snippets below are proposed interfaces, not callable APIs today. Nothing is pushed.

## Intent

Restore the readable, explicit module declarations from the remote master AppHost while retaining the new neuron runtime, process isolation, typed module settings and reliable browser lifecycle. Application code should show which modules, providers and capabilities are enabled. Tests should speak the same composition language.

The first product scenario is a user typing a request for Supabase data into the existing Flutter chat and seeing actual database rows in an interactive table window within the current workspace. The user explicitly selected a separate workspace window, including filters and related table interactions. It must include loading, empty and error states. The request is for a proposal and alternatives before implementation.

## Evidence and current gaps

- Remote master explicitly declares AI, Memory, ClickHouse, Supabase, Time, Excel, Google, Salesforce, Microsoft, Coding and Flutter. Its separate MCP resource is also visible. See [original AppHost](https://github.com/intochat/digitalbrain/blob/master/src/Applications/IntoChat/AppHost/AppHost.cs).
- `src/Applications/IntoChat/Configuration/DigitalBrainConfiguration.cs` currently fixes four selections: Google, Flutter, Time and TestTwitterModule. Its top-level module properties, `Bind`, factories and complete application snapshot must evolve together whenever a module changes.
- `src/Modules/DigitalBrain/Aspire.Hosting/Brain/DigitalBrainHostingExtensions.cs` has both imperative `AddModule` hosting callbacks and a definition-based `AddModules` path. These need one authoring contract, not another permanent wrapper layered over two competing mechanisms.
- `ApplicationConfigurationTransport` compares incoming setting key sets with the default composition. Sparse option mappings such as `AIModule.Define` make this unsuitable as the long-term schema for optional provider settings. Validation should belong to each module's declared contract.
- `AgentNeuron.Ask` currently has no tool list and creates a fresh session per call. A persistent UI thread cannot simply be mapped to this method while claiming conversation continuity.
- Flutter's `runAgent` posts to `/agent` and expects streamed run/message/tool events. The old conversation and workspace endpoints are excluded from the runtime project; `Program.cs` does not map that conversation route. Re-enabling every legacy file would restore old dependencies, not complete the new architecture.
- Supabase tool names appear in `ConversationalAgent`, but current module registrations do not provide them. `NativeTools.Resolve` silently skips missing names. Explicitly requested capabilities must fail validation if unavailable.
- Supabase uses Npgsql against PostgreSQL. Existing Supabase tests use `FakeSupabaseProvider`. The new E2E must exercise the real database provider.
- The workspace expects richer table metadata and paged responses than the current simple Flutter `ITable` state. Define one result-to-table adapter; do not assume matching route names imply matching payloads.
- Workspace window membership, modes and bounds currently live in Dart `WorkspacePresentation`/`WorkspaceStore`; `WorkspaceApp` reacts to tool result maps and calls `store.openArtifact`. No workspace/window neuron contract exists in the current Flutter contracts. The rendering/windowing implementation already exists and should be reused.

The previous post-merge solution run passed 204/205 tests, with an Aspire cleanup failure that passed a focused recheck. Record and investigate that lifecycle issue if it recurs; do not bury it under scenario retries.

## Three interface options

| Option | Shape | Strength | Cost |
|---|---|---|---|
| A — shared declarative builder (recommended) | `WithModule<T>(module => module.With...())` in AppHost and test builders | Closest to the requested master experience; one source of validation and composition | Existing imperative hosting methods must become declarations with adapters |
| B — typed module instances | `WithModule(new SupabaseModule(options))` | Ordinary C# objects and constructors; fewer fluent methods | Changes the parameterless module construction model; nested options remain; hosting choices need their own typed values |
| C — keep AppHost callbacks and add test overrides | Existing `AddModule<T>` plus test-specific configuration hooks | Smallest immediate change | Unit cannot use Aspire callbacks; two configuration mechanisms and incomplete process transport remain |

Changing `record DigitalBrainConfiguration` to `class DigitalBrainConfiguration` alone does not solve this problem. Generated equality/clone members are normal record implementation details. The design problem is that a single application settings object owns module membership, binding and serialization as well as values. Option A removes that responsibility from the object entirely.

## Recommended authoring model

Use one canonical spelling, `WithModule`, for declaring modules. Do not retain both `AddModule` and `WithModule` as competing public conventions after migration.

The AppHost remains the readable composition root:

```csharp
var brain = builder.AddDigitalBrain("modules")
    .WithModule<AIModule>(ai => ai
        .WithLlm<OpenAIModels.IGpt56Luna>()
        .WithDefaultLlm<OllamaModels.IGemma4>()
        .WithDefaultEmbedding<OpenAIModels.ITextEmbedding3Small>()
        .WithVoiceToText<IWhisperLargeV3Turbo>()
        .WithTavilySearch())
    .WithModule<MemoryModule>(memory => memory.WithQdrant())
    .WithModule<ClickHouseModule>(db => db.WithClickHouse(o => o.WithSeed("leads")))
    .WithModule<SupabaseModule>(db => db.WithConnection("supabase"))
    .WithModule<TimeModule>()
    .WithModule<ExcelModule>()
    .WithModule<GoogleModule>(google => google.WithGmail())
    .WithModule<SalesforceModule>(salesforce => salesforce.WithHostedMcp())
    .WithModule<MicrosoftModule>(microsoft => microsoft
        .WithAspire(appHostPath)
        .WithGitHubRepositories(repositories))
    .WithModule<CodingModule>(coding => coding.WithSolution(solutionPath))
    .WithModule<FlutterModule>(flutter => flutter.WithWindowHost());

// Explicit application behavior/capability registration belongs beside this list.
// Runtime and MCP resources remain explicit Aspire resource declarations.
```

Model names above preserve the existing application's choices, not a new model recommendation. Paths and external input collections are explicitly obtained by the AppHost. `WithConnection("supabase")` selects a named secret connection reference, never a connection string literal. Supabase needs an explicit connection declaration even though the old AppHost omitted its hosting callback.

Every callback configures a module-specific builder. That builder produces a declaration; it does not immediately launch containers, mutate global configuration, or connect to services. Reusing the old method names does not mean retaining their eager side effects.

Unit and integration callers use the same module extensions:

```csharp
await using var brain = await UnitTest.Create()
    .WithModule<SupabaseModule>(db => db.WithProvider<FakeSupabaseProvider>())
    .StartAsync(ct);

await using var brain = await IntegrationTest.Create()
    .WithModule<SupabaseModule>(db => db.WithPostgres())
    .StartAsync(ct);
```

`WithProvider<T>` is a proposed typed module extension. Local Unit resolves its implementation through DI. An external runtime needs a compiled, discoverable provider implementation in its dependency bundle; local delegates never cross processes.

Application E2E starts the actual AppHost declaration, rather than reconstructing a second application inside a test:

```csharp
await using var brain = await E2ETest.For<Projects.IntoChat_AppHost>()
    .ConfigureModule<FlutterModule>(flutter => flutter.WithWebHost())
    .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, modelFixture.Endpoint))
    .ConfigureModule<SupabaseModule>(db => db.WithPostgres())
    .WithBrowser(new() { Headless = false, SlowMoMilliseconds = 250 })
    .StartAsync(ct);
```

The real implementation must specify the selected model/provider as well as the endpoint; the snippet highlights the intended authoring shape. Synthetic model credentials use the existing private configuration channel. Database credentials and dynamically allocated addresses come from the resource adapter.

Two verbs preserve clear semantics: `WithModule` adds a declaration; `ConfigureModule` alters an existing declaration and fails if it is absent. Unit/Integration build compositions; E2E changes deployment choices of the application's declared modules. An E2E test must not quietly add the missing AI or Supabase module and thereby conceal broken application wiring.

## What lives underneath

Use a small host-independent `BrainCompositionBuilder` class with a built immutable composition. There is no global `Google`, `Flutter`, `Supabase` property inventory. Each module owns its typed authoring methods, option validation and mapping to runtime/hosting settings. Options can be ordinary classes; the built declaration must snapshot them so later mutation cannot affect a running test.

AppHost and test builders delegate module registration to this same implementation. Adapters interpret declarations for in-process Unit, external Integration and Aspire application hosting. A declaration includes module identity, typed settings, provider/resource choices and dependency requirements; it contains no live `IResourceBuilder`, service instance or executable delegate.

Collect registrations and test overrides, validate once, then materialize the graph before attaching runtime references. Freeze before `.WithReference(brain)` consumes it and reject further changes. This replaces eager hosting callbacks. Unknown modules/options, incompatible provider selections and missing required capabilities fail before application resources start.

Module identities remain stable. Explicit duplicate registrations fail with guidance to use `ConfigureModule`; equivalent transitive dependencies deduplicate. Configure calls patch the selected module before final validation. Provider replacement is a single typed choice, not an ambiguous second registration. Do not use DI ordering as the user-facing replacement contract.

Precedence is module defaults, explicit application declarations, then explicit test overrides. External configuration contributes only values at declared references or explicitly requested binding points. There is no automatic wholesale `Bind(IConfiguration)` of application topology. Browser/run preferences remain separate from module settings.

There is still an internal transport because AppHost and runtime are different processes. Serialize validated values and references, not callbacks or live objects. Keep schemas, version checks and secret separation behind the module interface. Replace the complete settings-key equality check with module-owned validation for optional fields. No arbitrary remotely supplied type activation.

Remove the current application-wide `DigitalBrainConfiguration`, `Bind` and required `IApplicationConfiguration` input from E2E when callers migrate. Keep the immutable internal composition where it earns its place; do not replace it with a second application-specific object hierarchy.

## Restoring all modules honestly

Port the eleven explicitly selected modules listed above, checking each old hosting method against the new runtime. Verify model selection/defaults, Qdrant resource references, ClickHouse seeds, Supabase connection reference, Gmail, Salesforce MCP, GitHub/Aspire, solution path and Flutter host selection. Compile success alone is insufficient evidence of restored behavior.

Keep resource concerns in module hosting adapters. A Unit run must not start a native Flutter host because it shares the composition interface. Unsupported explicitly requested resource modes fail clearly. Production full-composition smoke tests validate graph and resource wiring; separate module/provider tests validate external capabilities.

The full application E2E must retain the application's module membership. Each external dependency needed at startup gets a declared test substitute or disposable local resource before graph materialization. Expensive AI model downloads, external OAuth and remote MCP connections cannot silently be triggered by an unrelated CI scenario. Inventory each module's startup requirements; configure test deployment choices explicitly, do not silently delete modules or weaken readiness checks. Keep a smaller three-module integration scenario for fast fault isolation as well.

## Product flow

1. User submits a request in the existing Flutter chat. Keep its run/thread identifiers and cancellation semantics.
2. A new-architecture `/agent` adapter validates the request and invokes the production agent. Adapt to the existing stream contract instead of reviving excluded legacy neuron/session code.
3. The agent has an explicitly selected Supabase read capability. Use the existing tool contributor mechanism with a narrowly scoped application-level adapter combining AI contracts, Supabase neurons and the UI result contract. Core AI must not depend on Supabase or Flutter implementations.
4. The agent discovers the schema, then invokes the query/table capability. The actual `ISupabase`/`ISupabaseTable` path executes through the existing read-only guards and Npgsql provider.
5. Return a typed table reference, title, columns, source and row count/limits. The UI fetches the authoritative table snapshot. Do not parse the assistant's prose to recover a table and do not make the model retype the database rows.
6. The tool opens the typed table view in the originating workspace through the workspace neuron. Flutter renders a real workspace window using its existing windowing and table controls; chat receives a link to that result. Stream a plain-language progress state, then a result. Empty results are a real empty state; query failures are errors with a retry action, not fabricated data.

Own conversation state in the runtime with stable thread identity. At minimum preserve completed turns and result references across requests; the browser must not be the sole source of history. Allow bounded cancellation and prevent stale completion from populating another thread. Run IDs must distinguish an interrupted request from an explicit retry. Do not bolt the legacy brain graph/session subsystem back on solely to obtain this behavior.

The Supabase table snapshot and Flutter workspace table contract need an explicit adapter and contract tests for names, types, nulls, paging and revision semantics. Prefer a data-backed table reference over copying rows into the simple UI-kit table neuron as a second source of truth. Filters, sorting, pagination and column visibility are part of this read-only interactive result; row editing and arbitrary writes are outside this slice.

## Workspace/window neurons

Introduce one `IWorkspace` neuron in the Flutter module for the logical UI workspace, with `Open`, `Close` and `Read` operations and a versioned `WorkspaceChanged` signal. Initially windows are descriptors owned by that workspace; a separate neuron for every window adds coordination without a demonstrated need. Add `IWindow` later only if independent lifetime or behavior requires it.

The workspace owns durable membership and typed view references, titles, open/closed state and idempotent open request identities. A table reference is UI-facing, not a Supabase client, SQL string or credential. An application adapter resolves that reference to the existing `ISupabaseTable`. The Supabase table owns its query, filters, sort, paging contract and revisions. It is not duplicated inside workspace state.

Flutter continues to own device-specific geometry, drag/resize interactions and focus. A persisted open window can be reconstructed from the server snapshot after refresh; layout bounds remain local. Replace local authoritative open/closed membership with a projection of workspace state, preserving local layout preferences and existing unrelated artifacts. Do not leave two writers competing over window membership. Remote opening must not steal focus after the user switches to a different workspace.

Bind workspace identity to the authenticated request and originating conversation. The model must not choose an arbitrary user's workspace ID. The server derives stable result/window identity from run and tool-call identity. Replayed tool results or reconnects reconcile the same window; they do not create duplicates or reopen a window the user subsequently closed. Separate query result views receive separate table IDs when independent filters are desired.

Proposed product-side interaction, not test setup:

```csharp
var table = brain.Get<ISupabaseTable>(resultId);
var view = await table.CreateFromQuery(new CreateQueryTable(title, sql));
await brain.Get<IWorkspace>(workspaceId).Open(
    new OpenWindow(operationId, resultId, title, new TableViewReference(view.Id)));
```

Exact DTO signatures are design targets. The existing CreateQueryTable signature must be followed when implementing. The coordinator must be retry-safe across the table-create and workspace-open operations: a completed table creation followed by a failed open must resume from the same table, not call create again and fail with "already exists". This needs an idempotent operation record or an equivalent existing-result path. A single orchestration tool should return the completed view reference; the LLM does not coordinate these persistence steps.

Keep production tool execution and workspace commands accessible through typed neuron interfaces. The browser test must cause the agent to invoke them, not directly manufacture the window from test code.

## E2E strategy and what it proves

Default CI: actual AppHost, real runtime and agent tool loop, real Flutter browser, real Supabase Npgsql provider, disposable PostgreSQL with known seed data, and a scripted local model HTTP endpoint. The production OpenAI-compatible provider already reads a custom endpoint in `LlmProviderFactories.cs`; `AIModule.Define` currently omits that provider endpoint, which the new typed mapping must fix. Use an explicit test model selection rather than leaving the application's Ollama default active.

This preference over injecting a test `IChatClient` into E2E keeps the real model client/serialization and avoids inventing test-assembly loading in the application. A scripted `IChatClient` remains appropriate in Unit tests. The HTTP fixture needs only the request/streaming protocol actually used by the selected provider and must reject unexpected calls.

The scenario:

- Create a fresh database and seed leads including a unique per-run company name, two matching rows, and an excluded control row. Select a least-privilege read connection for the application.
- Start the actual application's test deployment and wait for database seed completion, runtime health and Flutter semantic readiness. Open the browser in either headed or headless mode.
- Type “Show the active leads from Supabase with company and email” and click Send through the UI.
- The model fixture returns real tool-call messages for schema discovery and query-table creation. It must require real tool results on subsequent requests before completing. It never writes UI state or calls the provider for the application.
- Assert a window opens in the originating workspace with the visible table title, company/email columns, matching rows including the unique value, no excluded control row, and no remaining loading state. Verify no UI result/window was seeded by test code.
- Use the window's filter and sort controls, clear the filter, change pages and check the expected rows/counts. Include a matching row beyond the initially loaded page so browser-only filtering cannot accidentally pass as server-side filtering.
- Refresh and verify the workspace restores that same window and table view settings. Replayed completion must not duplicate the window. Closing and reopening from the result link must preserve the data view. Check a second workspace cannot accidentally receive or focus the first workspace's result.
- Add focused empty-result and database-error scenarios. Assert clear UI feedback and no invented table rows. Cover cancel/navigation cleanup with the existing ownership framework.

This proves application E2E with a deterministic model and the real PostgreSQL-backed Supabase provider. It does not prove live-model interpretation, hosted Supabase connectivity, Auth, REST or Realtime. Add a separate opt-in live-model run against the same seeded database, with semantic data assertions rather than exact wording. It should fail clearly if explicitly requested credentials are absent. Do not use a paid live run as the default CI gate.

Use PostgreSQL for the default fixture because it matches the actual provider. A full local Supabase stack becomes warranted if the selected scenario starts depending on its Auth/REST/Realtime behavior. See [research and primary sources](../../research/2026-09-20-ai-supabase-e2e-options.md).

## Proposed delivery order

1. Agree on option A/B/C; the user has selected an interactive table window in the existing workspace. Lock the composition/override semantics and test guarantee before changing interfaces.
2. Implement shared module declarations and adapters with regression tests for typed settings, validation, precedence, duplicate selection, snapshots and process transport. Exercise AI, Supabase and Flutter first to prove the full depth of the interface.
3. Restore the explicit full AppHost inventory; migrate Unit, Integration and E2E authoring and remove the application-wide configuration record. Verify each provider/hosting declaration, missing capability errors and all existing tests.
4. Implement the workspace neuron and window projection, missing production chat route, conversation state, selected agent tools and table adapter using new neuron contracts. Verify idempotent window opening, the HTTP/stream contract and table filtering/paging before browser testing.
5. Add isolated database/model fixtures and the browser journey. Add empty/error/cancel coverage; run the success case in headless and visible modes independently without scenario retries.
6. Run module suites, actual full-application smoke/E2E tests and the whole solution. Review the final public interface and remove obsolete configuration/binding paths. Keep the packaging fallback from the previous refactor out of scope.

No changes to branch placement, remote publishing, package certification or unrelated UI redesign are part of this proposal.
