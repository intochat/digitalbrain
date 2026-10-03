# NuGet Readiness Assessment — Kernel Ring, Client, Sdk, Platform, Aspire

Date: 2026-10-03. Scope: `DigitalBrain`, `DigitalBrain.Contracts`, `DigitalBrain.Kernel`,
`DigitalBrain.Client`, `DigitalBrain.Client.Orleans`, `DigitalBrain.Sdk`, `DigitalBrain.Platform.Contracts`,
`DigitalBrain.Platform`, `DigitalBrain.Kernel.AspNetCore`, and the three `DigitalBrain.Aspire.*` integrations
plus their covering test suites. Every finding carries a severity; the ordered action plan is at the end.

---

## The fundamental problem (the answer to "what is wrong")

The code *inside* classes is mostly good (SignalSubscription, enforcement, secrets ciphertext
discipline). What is fundamentally wrong is **package shape**: almost every assembly is two or three
products fused together, logic sits one ring away from where it belongs, and test plumbing ships in
production paths. Concretely, five systemic defects explain most of the perceived complexity:

1. **Fused packages.** Kernel = neuron runtime + composition framework + ASP.NET HTTP enforcement
   (dragging `FrameworkReference Microsoft.AspNetCore.App` and `Orleans.Server` into the core
   package). Client = Orleans in-cluster client + script-edge HTTP client (a single-file script
   consumer downloads Azure clustering + five OpenTelemetry packages + the ASP.NET shared
   framework). Sdk = seven unrelated concerns (OAuth middleware, MCP client, SQL compiler, a grain
   implementation, vector store, identity, webhooks). `DigitalBrain.Aspire` = the Azure-only silo
   runtime wearing an Aspire-integration name.
2. **Backwards / privileged dependencies.** Kernel (server) references the Client package;
   `DigitalBrain.Aspire` references `DigitalBrain.Platform` so the silo package publishes the
   credential ring transitively; `Aspire.Hosting` (an AppHost package) references the full Kernel
   implementation instead of Contracts.
3. **Contracts aren't contracts; wire contracts aren't in Contracts.** `Kernel.Contracts` ships a
   validation/type-catalog engine and an `AsyncLocal` ambient-state machine; meanwhile the actual
   client↔edge wire protocol (`ScriptEdgeProtocol`) is `internal` in Client and shared with the
   server via `InternalsVisibleTo` — a de facto public API with no versioning story.
4. **Test plumbing in production.** `CompositionOverrideTransport` (global static, leaks tokens),
   the `DigitalBrain:Testing:PrivateConfiguration` file-injection hook in `DigitalBrain.Aspire`,
   and — worst of all — `FakeKeyVault` registered by default in `PlatformHosting.AddPlatform`
   (owner data keys persisted effectively unencrypted unless the host wires a real wrapper).
5. **Identity chaos.** Package id ≠ assembly ≠ root namespace ≠ folder across the ring:
   `DigitalBrain.Modules.Kernel` → ns `DigitalBrain.Core`; `DigitalBrain` and `Kernel.Contracts`
   both use ns `DigitalBrain.Contracts`; folder `Kernel.Client` → assembly `DigitalBrain.Client`;
   folder `Kernel.Sdk` → assembly `DigitalBrain.Modules.Sdk` → ns `DigitalBrain.Sdk`, with two
   files in `DigitalBrain.Identity`. Renaming after v0.1 is a breaking change — this is the last
   cheap moment.

Also: **`dotnet pack` is currently broken repo-wide** — `Directory.Build.props` sets
`<PackageIcon>icon.png</PackageIcon>` but no icon.png exists and the pack item is
condition-guarded, so packing either fails (NU5046) or silently ships without the declared icon.
No packable project has `PackageReadmeFile` or `GenerateDocumentationFile`.

The findings above describe the original assessment; addressed status and the action plan below record subsequent changes.

---

## Critical findings

| # | Project | Location | Finding | Addressed |
|---|---------|----------|---------|-----------|
| C1 | Platform | `PlatformHosting.cs:28` | `TryAddSingleton<IKeyVault, FakeKeyVault>` in default production composition — fake crypto wired in silently; keys persist as `"kv1:"+base64(key)` unless a real wrapper is separately registered. Move fake to Testing; fail fast when no real key vault/master key is configured. | ✅ `9c1f84870` |
| C2 | Client | `DigitalBrain.Client.csproj:17-27` | Two products in one package; script consumers inherit Orleans/Azure/OTel/ASP.NET. Split script-edge client into a lean package (needs ~`System.Net.Http.Json`). | ✅ HTTP-only `DigitalBrain.Client`; cluster transport in `DigitalBrain.Client.Orleans`. Host defaults remain in Aspire.Client. |
| C3 | Sdk | whole project | Grab-bag SDK: grains, middleware, SQL compiler, MCP client, identity, secrets APIs in one assembly. Split public module-author surface from unpublished platform contracts. | ✅ Platform contracts extracted; stored rows/query evaluator moved to Kernel; MCP session moved to Salesforce. SDK retains module-author helpers. |
| C4 | Sdk | `Secrets/ISecrets.cs:17`, `Integrations/IIntegrationRegistration.cs:33`, `Auth/TokenHandoff.cs`, `Identity/Accounts.cs:41` | Plaintext-credential-returning APIs on the public NuGet surface; only runtime `[PlatformOnly]` protects them. Move to a non-published platform-contracts assembly. | ✅ Approved design adjustment: publish `DigitalBrain.Platform.Contracts` separately. Publication is not an authorization boundary; assembly-level PlatformOnly enforcement remains. Token handoff implementation lives in Platform. |
| C5 | Aspire | `DigitalBrain.Aspire.csproj` | Misnamed (it's the silo runtime, not an Aspire client integration), product-coupled ("Silo host wiring for IntoChat"), and references `DigitalBrain.Platform` — publishing it publishes the credential ring. Rename and cut the Platform dependency from the public graph. | ✅ Server integration references Kernel, HTTP companion and Orleans client, with no Platform dependency. Applications explicitly install Platform. |
| C6 | repo | `Directory.Build.props` | `PackageIcon` without a packed icon file → `dotnet pack` fails/ships wrong. Add the icon + pack item or drop the property; add a pack smoke test to CI. | ✅ `1b65d23c7` |
| C7 | Kernel | `Composition/CompositionOverrideTransport.cs` | Test-only global static in the shipped runtime; `Take` doesn't remove entries (unbounded growth, token reuse). Move to DigitalBrain.Testing; fix the leak regardless. | ✅ `3a724f8a4` (deleted, not moved — see note) |

## High findings

### Dependency / layering
- **H1** Kernel csproj: server runtime references the Client project — invert; the shared records belong in Contracts. — ✅ Kernel no longer references either client package.
- **H2** Kernel csproj:8: `FrameworkReference Microsoft.AspNetCore.App` on the core runtime. Split `Enforcement/BrainRoutes|BrainAccessFilter` + `IModule.Configure(IEndpointRouteBuilder)` into a `.AspNetCore`/hosting companion package. — ✅ HTTP routes/filter and `IHttpModule` moved into `DigitalBrain.Kernel.AspNetCore`; core Kernel has no ASP.NET framework reference.
- **H3** `Aspire.Hosting.csproj:12-13`: AppHost package references the full Kernel implementation; depend on Contracts/composition abstractions only. — ✅ AppHost and feature hosting adapters depend on contracts, not runtime implementations.
- **H4** Client `Edge/ScriptEdgeProtocol.cs`: wire protocol internal + IVT to a production module. Promote to Contracts; delete the production IVT. — ✅ Public v1 wire DTO/constants in Contracts; production protocol IVTs removed. Paths and JSON unchanged.
- **H5** Contracts: relocate `TypeCatalog`/`SchemaExtensions`/`SecretRef` parsing and `IntentContext` (logic) out of Contracts; delete dead `Types/ValueTypes.cs` structs (unused except `Reference`). — ✅ Type catalog, schema validation and secret-reference parsing moved to SDK; ambient intent state moved to Kernel with synchronized usage snapshots; only the used Reference wrapper remains.
- **H6** Platform csproj: "the ring scripts can never see" is `IsPackable=true`. Decide: private feed or `IsPackable=false`. — ✅ Approved decision: Platform and Platform.Contracts are packable, explicitly installed server capabilities. Credentials remain inaccessible to untrusted callers through runtime enforcement.

### Security / correctness
- **H7** Kernel `Enforcement/BrainAccessFilter.cs:36-38`: unauthenticated header-less requests bypass membership ("open development posture") — insecure-by-default in a public package; make it explicit opt-in, and pin the current behavior with a test. — ✅ Addressed: every request requires a caller stamp and membership check; the explicit Open posture permits the synthetic owner through a composed access policy. Regression tests cover requests with and without Authorization.
- **H8** Platform `Identity/AccountSession.cs:100-103`: no credential ⇒ everyone is `owner`. Same opt-in treatment. — ✅ Addressed: DigitalBrain:Auth:Posture must be Open or Secured; missing posture fails startup, and Secured rejects anonymous requests even without a bootstrap credential. Aspire local runs and the unit harness supply Open; published deployments must configure their posture.
- **H9** Sdk `Auth/LoginPage.cs:14-16`: unencoded string interpolation into HTML — XSS the moment any consumer passes tainted strings. HtmlEncode. — ✅ Addressed: title and message are HTML-encoded, with regression coverage for both title locations and message markup.
- **H10** Platform `Identity/Directory/IdentityDirectoryNeuron.cs`: god grain — every account, member, invitation and password hash in one list-shaped global grain state; full scan per auth.
- **H11** Aspire `DigitalBrainRuntimeHostingExtensions.cs:24-29`: `DigitalBrain:Testing:PrivateConfiguration` reads an arbitrary JSON file into config in the production path; move behind a Testing seam (same family as C7). — ✅ `3a724f8a4`: became the neutral `DigitalBrain:ConfigurationFile` setting loaded via standard `AddJsonFile` (secrets-file pattern, legitimate in production).
- **H12** Aspire: Azure welded in with no seam (`:31-33,100-126`) — no provider choice, Orleans Dashboard unconditional (`:54`). Accept options; make Azure/Dashboard opt-in. — ✅ Azure and dashboard are opt-in; AppHost defaults to memory providers. Product and E2E compositions explicitly retain Azure.
- **H13** Aspire.Hosting `Brain/DigitalBrainBuilder.cs`: two classes in one; `Orleans`/`GrainState` are `null!` until `AttachRuntime`; `Materialize()` only runs from `WithReference`, so an unreferenced brain never validates. Split and construct fully. — ✅ Builder constructor receives its complete runtime state; optional Azure resources are nullable by contract. Module options and adapter selection validate during composition, with no deferred Materialize.
- **H14** Aspire reflection contracts: `"FullName, Assembly"` env strings + `Activator.CreateInstance` (`DigitalBrainRuntimeHostingExtensions.cs:77-98`) and `Assembly.Load(name + ".Aspire.Hosting")` + `<Type>Hosting` name convention (`DigitalBrainHostingExtensions.cs:124-142`). Replace with an assembly-level attribute or explicit registration; make `FindModuleHosting` internal. — ✅ Stable IDs plus explicit server factories and explicit hosting adapters replace production type-name loading. Docker/publish profiles use Enabled keys. Assembly-qualified test modules remain solely in Testing.E2E.
- **H15** Client `BrainClientHosting.cs:16`: Azure Tables clustering hardcoded in the public entry point; `BrainOptions` is internal yet registered via public API (unconfigurable). — ✅ Aspire.Client Azure registration is opt-in; public SubscriptionOptions configure client timing/buffers. Kernel owns observer lease options.
- **H16** Kernel `Composition/ModuleDefinition.cs:24-25`: `CreateModule()` per Configure call — stateful modules silently get two instances. Cache or contract statelessness. — ✅ ModuleDefinition caches one instance with Lazy; the server passes the same instance to silo and HTTP configuration.
- **H17** Sdk twin `IIntegrationAccounts` interfaces (Sdk.Integrations.Accounts vs Contracts.Integrations) — same name, different neurons; rename one.
- **H18** Naming unification (systemic defect 5): one convention for package id = assembly = root namespace = folder, done before first publish. Includes `DigitalBrain` meta-package (ns `DigitalBrain.Contracts`) and the two Sdk files in `DigitalBrain.Identity`. — ✅ Addressed: the nine approved projects now use matching folder, csproj, assembly, package ID, and root namespace. The vocabulary is in `DigitalBrain`; Identity contracts now reside in `DigitalBrain.Platform.Contracts.Identity`.

**Status note (2026-10-03):** the findings below describe the original audit. The coordinated implementation and current verification results are recorded at the end of this document; use that section for current progress.

## Medium findings (abridged — see per-project notes below)

- Contracts `IntentContext.cs`: ✅ Moved to Kernel; locked usage collection, detached snapshots, nesting checks and explicit scopes across async iterator steps.
- Contracts `TypeCatalog.cs`: kind list maintained in four places (two switches, BuildCatalog, embedded JSON); derive from one. `ValidateDateTime` drops offsets despite promising ISO 8601.
- Contracts `SchemaExtensions.cs`: `x-intochat-*` vendor keys hard-code the product name in a platform package.
- Contracts `Signals/NeuronActivity.cs:8` and `CallRequest.SemanticTypeIds`: `IReadOnlyList<string>` on serialized records violates the repo's own concrete-arrays rule.
- Kernel `LocalSignalHub.cs:60-69`: fire-and-forget observer dispatch swallows exceptions, no backpressure; class fuses two hubs.
- Kernel `Neuron.cs:42-45`: reflection + attribute scan on every activation — cache per type. `PublishAsync` silently no-ops without a hub.
- Kernel `Persistence/GrainDocumentStore.cs`: magic 128 retries, no backoff; index `AddAsync` not atomic with write (crash ⇒ doc invisible to `ListIdsAsync`). `DocumentGrain` et al. public — make internal. `InMemoryDocumentStore` is test-only — move to Testing.
- Kernel `ModuleOptionsSerialization.cs:67-73`: redundant case-insensitive check after exact pattern match; casing policy inconsistent with `ModuleSettingsValidation`.
- Client: `ClientHostingDefaults` + `MapDefaultEndpoints` are Aspire service-defaults boilerplate in a client package — move to a host package. `DigitalBrainConnection` hardcodes `CSharpFile:*` module keys. `EdgeSignalSubscription` unbounded channel + no self-dispose, diverging from `SignalSubscription<T>` under the same interface. `DigitalBrainClient` spins up a full Generic Host just for env config + SIGTERM.
- Sdk: `BrowserLogins`/`TokenHandoff` duplicate nonce stores with inconsistent `TimeProvider` use; `MapJsonWebhook` is a no-op abstraction; `ModuleHttp.cs:15` maps status by `Message.Contains("changed since")`; `StoredRowsNeuron` grain in the SDK; `McpHttpSession` is Salesforce-only — move to that module.
- Platform: `AccountSession` body-sniffs `/identity/register` JSON inside the auth middleware — move the check to the endpoint/neuron. `DpapiKeyWrapper` hand-rolls crypt32 P/Invoke instead of `ProtectedData`. `IntegrationOperatorGate` reads raw config keys duplicating `BasicAuthOptions`. Caller self-escalation to `CallerKind.Platform` duplicated in two neurons — centralize one audited helper. Four `IKeyWrapper` implementations with DI-registration-order precedence. *(DPAPI/four-wrapper items: ✅ addressed in `9c1f84870`.)*
- Aspire.Hosting: public grab-bag SPI on `DigitalBrainBuilder` (`GetOrAddState`, `AddProjection`…); `WithReference` name collision with Aspire's own semantics + undocumented `DigitalBrain__Modules__{index}` env wire contract; silo duck-typing by endpoint names; single-brain assumption (fixed storage/parameter names collide on two `AddDigitalBrain` calls); `BrainEndpointAnnotation` file holds `BrainBrowserAnnotation` record.
- Aspire: magic `"digitalbrain-v2-state"` container name; `MapDigitalBrainModules` does three jobs.
- Meta-package `Signal.Publisher` is a public `init` — the "provenance is inviolable" guarantee is not visible in the contract; document/kernel-only setter. `IDigitalBrain.Get<T>` constrained to `IGrainWithStringKey`, not `INeuron`.
- Packaging: mono-version `0.1.0-alpha.1` for 60+ packables (decide policy); `net11.0`-only narrows audience — consider multi-targeting Contracts; IVT without public keys breaks if signing ever lands.

## Test suite assessment

**What's good:** enforcement and secrets tests are genuinely behavioral (canary strings absent from
ciphertext, forged stamps, path traversal, rollback); `SignalSubscription` concurrency is well
covered; `EmbeddedJsonCatalogMatchesTheCodeCatalog` and `TheDigitalBrainAssemblyExportsOnlyTheWords`
are exemplary API guards. The "stupid tests" problem is smaller than feared — the dominant problem
is **misplaced tests and missing tests**, not tautologies.

**Structural problem (Medium):** `DigitalBrain.Modules.Kernel.Tests.Unit` is really the whole
kernel-ring suite — it tests Platform, Sdk, Client, and the Testing harness itself. Per the repo's
own rule, each package publishing to NuGet needs its own suite; today coverage attribution is
misleading.

**Tests to delete / fix (the "stupid" ones):**
- `Identity/BrainEstablishmentFacts.EstablishIsIdempotentWithIdentityComposed` — near-duplicate of `Brain/BrainFacts.EstablishIsIdempotent` with identical composition despite its claim.
- `CompositionFacts.DefinitionCopiesConfigurationAndDeduplicatesIdenticalModules` — half asserts that copying a dictionary copies a dictionary.
- `CapacityResolverFacts` exact-prose assertion — brittle; assert the exception type.
- `LifetimeFacts`/`HarnessCancellationFacts` — harness testing itself inside the kernel suite; move to Testing's own suite.
- `DigitalBrainClientFacts` — one nearly tautological missing-config test is the only direct edge-client coverage.
- Mixed naming conventions in Platform facts (`StoresCiphertextAndResolvesByReference` vs `The_master_key_store_...`).

**Coverage gaps, by priority:**
- **High**: `BrowserLogins`/`TokenHandoff` (security-critical nonce state machines, zero direct tests); `RowQueryEvaluator` (175 lines, untested — can silently diverge from the SQL compiler; drive both from one shared theory); `IntentContext` concurrency.
- **Medium**: `BrainAccessFilter` anonymous-bypass pinning; `GrainDocumentStore` retry exhaustion + index-crash gap; `NeuronProxy` unsupported return types / token extraction; `EdgeSignalSubscription` lifecycle; `BrainClientHosting` options validation; `AccountSession.BasicCredential` parsing; `ModuleHttp`/`ModuleRouteGuard` mapping; zero direct coverage of `DigitalBrain.Aspire` (standalone guard, module-load failure messages); `FindModuleHosting` failure path and the naming convention itself; cluster/service-id derivation matrix; two-brains-in-one-AppHost behavior.
- **Low**: `LocalSignalHub` observer fault path; buffer-overflow path in `SignalSubscription`; `DpapiKeyWrapper` (Windows-gated fact); `LoginPage` encoding.

---

## Ordered action plan

**Phase 0 — stop the bleeding (small, independent fixes)**
1. C1: remove `FakeKeyVault` from production composition; fail fast without real crypto. — ✅ Done in `9c1f84870` (also extracted the master-key parameter into `Aspire.Hosting/Brain/MasterKey.cs`; `AddIdentityStorage` dissolved into `AddCookieProtection`; kernel suite 197/197 green, broader module suites not re-run).
2. C6: fix `PackageIcon`/pack; add a `dotnet pack` smoke check. — ✅ Done in `1b65d23c7` (512×512 icon at `src/Assets/nuget/icon.png`, `Exists` guard removed so a missing icon fails the pack; CI's existing solution-wide pack step enforces it; verified with the icon inside the nupkg).
3. C7 + H11: move `CompositionOverrideTransport` and the private-config hook to Testing; fix the token leak. — ✅ Done in `3a724f8a4`, by removing the root cause instead of relocating it: module options now compile to flat configuration keys (`DigitalBrain:Modules:{Name}:Options:{Property}`) bound with the standard binder, and `AddModules` overlays the AppHost's own configuration over code defaults, so tests override options with plain host arguments. The transport, `CompositionOverrides`, `ApplyOverrides`, the `Testing:Enabled` gate, and the frozen-builder `EnsureMutable` ceremony are deleted; the private-config hook became the neutral `DigitalBrain:ConfigurationFile` (`AddJsonFile`). Verified: kernel 197/197 plus CSharp, AI, Assistant, Gmail, Postgres, Supabase, ClickHouse, Flutter, Salesforce, GitHub, Time, Qdrant, Memory unit suites green (remaining small suites were finishing green at commit time); live-gated E2E not run.
4. H9: HtmlEncode `LoginPage`. — ✅ Done: both title locations and the message encode untrusted input; regression test covers markup, quotes, and ampersands.
5. H7/H8: make both open-posture defaults explicit opt-in, with pinning tests. — ✅ Done: mandatory Open/Secured host configuration, fail-fast validation, secured anonymous rejection, and unconditional edge membership enforcement. The Open policy is composed explicitly; Aspire run mode and the unit harness select Open, while publish mode supplies no default. Bootstrap-username reservation now runs in the registration endpoint, and the integration operator gate uses the same auth options. Verified 2026-10-03: kernel-ring suite 205/205 passed (0 skipped), including header-less membership regressions and H9 encoding coverage; git diff --check passed. Broader module suites and live E2E were not run for this change.

**Phase 1 — naming, once, before anything publishes**
6. H18: ratify one naming convention (package id = assembly = namespace = folder) and apply across the ring; rename `DigitalBrain.Aspire` (C5 part 1). — ✅ Complete and verified on 2026-10-03.

   Approved identities (each is the project folder, csproj stem, assembly, package ID, and root namespace):
   - `DigitalBrain`
   - `DigitalBrain.Contracts`
   - `DigitalBrain.Kernel`
   - `DigitalBrain.Client`
   - `DigitalBrain.Sdk`
   - `DigitalBrain.Platform`
   - `DigitalBrain.Aspire.Hosting` — AppHost resource composition.
   - `DigitalBrain.Aspire.Client` — consuming-process configuration, health endpoints, service discovery, and telemetry, extracted from Client.
   - `DigitalBrain.Aspire.Server` — existing server-process integration, renamed from `DigitalBrain.Aspire`.

   References, solution entries, friend assemblies, script imports, tests, and compatibility-baseline type names follow the new names. Kernel test projects are `DigitalBrain.Kernel.Tests.Unit` and `.Tests.E2E`. Existing parent grouping folders are retained. Orleans aliases and persisted field IDs are unchanged.

   Validation: full solution Release build passed with zero warnings/errors; all 24 unit/architecture suites passed (906 passed, 6 skipped, 0 failed). Solution-wide NuGet packing passed; all nine package identities and packed DLL names match the naming rule. Live E2E was not run. The architecture baseline also records previously unrecorded, additive Apps/CSharp contracts after checking that existing field IDs remain unchanged; the Assistant route test now supplies the access policy required by H7.

   Scope at the H18 commit: naming and Aspire client-integration extraction only. The subsequent coordinated refactor below completes the transport split, SDK split, dependency inversions and Platform publication decision.

**Phase 2 — package shape (the big refactor)**

**Completed: coordinated package/composition refactor (2026-10-03).**

```text
DigitalBrain.Client          -> Contracts -> DigitalBrain
DigitalBrain.Client.Orleans  -> Contracts
DigitalBrain.Kernel          -> Contracts
DigitalBrain.Kernel.AspNetCore -> Kernel
DigitalBrain.Sdk             -> Kernel.AspNetCore + Platform.Contracts
DigitalBrain.Platform       -> Sdk
DigitalBrain.Aspire.Hosting  -> Contracts
DigitalBrain.Aspire.Client   -> Client.Orleans
DigitalBrain.Aspire.Server   -> Kernel.AspNetCore + Client.Orleans
```

```csharp
// AppHost: explicit infrastructure and module adapters; no runtime module assemblies.
var brain = builder.AddDigitalBrain("brain", options: new()
{
    UseAzureStorage = true,
    Dashboard = true,
}).WithModule<PostgresModuleHosting, PostgresModuleOptions>(db => db.WithPostgres());

// Server: available implementations are registered in code; configuration selects stable IDs.
builder.AddDigitalBrainServer(server => server
    .UseAzureStorage().WithDashboard()
    .AddModule<PostgresModule>("postgres"));
builder.AddDigitalBrainPlatform();
app.MapDigitalBrainModules();
app.MapDigitalBrainPlatform();
app.MapDigitalBrainDashboard();
// DigitalBrain__Modules__postgres__Enabled=true
```

7. H4 + H1 — ✅ Shared v1 protocol; no Kernel→Client reference or protocol IVT.
8. C2 + H15 — ✅ HTTP script client separated from Orleans; no Generic Host lifecycle in script client. Orleans generator is a private build dependency of contracts.
9. H2 — ✅ ASP.NET companion extracted.
10. H5 — ✅ Runtime/catalog/parsing separated from wire DTOs. Intent collection supports concurrency and detached snapshots; async iterator steps explicitly enter the intent scope.
11. C3 + C4 — ✅ Privileged contracts extracted into **publishable** Platform.Contracts (approved replacement for the original unpublished-package proposal). StoredRowsNeuron/RowQueryEvaluator live in Kernel; Salesforce owns McpHttpSession; Platform owns TokenHandoff.
12. C5 + H3 + H6 — ✅ Explicit Platform installation, no Aspire.Server→Platform or AppHost→Kernel graph. Assistant and Playwright projects made packable to close existing downstream package dependencies.

Compatibility: all 522 persisted type baselines retain their field IDs; moved types retain Orleans aliases. Legacy collection payloads are unchanged. The local observer path remains through Contracts.Signals.ILocalSignalHub because Orleans forbids creating an observer object reference inside a grain.

Validation (2026-10-03):
- Release solution build: **0 warnings, 0 errors**.
- All 24 unit/architecture suites: **919 passed, 6 skipped, 0 failed**. Includes legacy collection deserialization and identity-state fixtures.
- **73 NuGet packages** produced; every internal dependency exists in the feed.
- Five isolated consumers restored and built from the produced feed: HTTP script, Kernel, all AppHost adapters, Server + Platform, and all packages. A fresh package cache and source mapping prevented reuse of older DigitalBrain packages.
- Restored graph assertions: script has no Azure/Aspire/OTel/Generic Host/Orleans client or server; Kernel has no Client/Platform; AppHost adapters have no runtime module dependencies.
- `git diff --check` passed. Container-backed E2E deployment was not run.

**Phase 3 — API hardening**
13. H12–H14, H16 — ✅ Explicit infrastructure options, stable module IDs, explicit hosting adapters/server factories, fully initialized builder and one module instance.
14. H10 + H17: complete and verified in the coordinated identity/reliability change below. Live identity is keyed by principal, account, and exact account/brain scope; the old directory is a migration facade.

**Phase 4 — tests**
15. Package-owned suites now cover Platform, SDK, HTTP Client, Orleans Client, Aspire.Hosting and Testing.Unit, with Kernel retaining runtime tests. Nonce concurrency, registration recovery, scoped grant ownership, local/HTTP overflow, and shared SQL/in-memory query vectors were added. IntentContext concurrency coverage remains in Kernel.


## Coordinated identity and reliability implementation — 2026-10-03

```text
Principal(principalId)         password + stable registration allocation + checkpoint
Account(accountId)             owner + idempotent brain provisioning
BrainAuthority(account, brain) membership + invitations + grants + atomic authorization
```

- **H10:** normal login/membership/grant paths use directly keyed actors. All access checks carry account and brain. One-time grants are checked and consumed in one non-reentrant turn. Owner checks and grant mutations also share one turn; stale cookie roles cannot authorize them.
- Configured Basic bootstrap credentials provision ownership of one explicit default scope, with no cross-account or arbitrary-brain bypass.
- Registration persists allocation before provisioning and resumes after storage failure. `POST /identity/brains` accepts `Idempotency-Key`; retry cannot recreate revoked ownership.
- Migration is maintenance-only, refuses ambiguous legacy ownership, preserves legacy snapshots/aliases/field IDs, seals old grant writers, and checkpoints imports. Repeated imports cannot restore revoked access. [Upgrade procedure](IDENTITY-UPGRADE.md).
- **H17:** `IConnectionRequests` is the script API; `IConnectionRegistry` is privileged. Existing `integrations.accounts` and `connections` identities are retained.
- Credential probes, provider-registration writes and OAuth token operations have named services. Elevated contexts remain internal; logs retain the initiating actor and never log credential values. `RequirePrincipal` no longer implies an ownership check.
- OAuth uses explicit, separate browser-login and token-handoff state transitions with injected clocks. `TryTake` is atomic. Both stores remain bounded and process-local; failures after consumption require a fresh login.
- Document writes reserve a durable index entry **before** committing a document. Listing filters incomplete reservations. CAS retries are bounded with backoff and a typed `DocumentConflictException`; in-memory storage moved to Testing. This replaces the proposed outbox worker with fewer moving parts.
- Local and HTTP signal delivery is bounded; overflow and observer failures are visible. HTTP reader disposal closes the subscription; completion does not require draining the queue. Neuron type metadata is cached; missing hubs fail explicitly.
- Type metadata, validators and accepted inputs come from one runtime registry. JSON is now a test snapshot. Date-time offsets survive validation; unspecified local times are rejected. Serialized activity/request type-ID collections are arrays.
- Query validation is shared across SQL/in-memory paths. Text stays text, empty aggregate queries produce a row, and multi-column grouping cannot collide on embedded separator characters. Shared vectors cover both paths; this is not a live database equivalence test.
- No-op webhook wrapper removed; IO status handling no longer depends on exception prose. Existing domain-conflict mappings remain compatible.
- Aspire scopes infrastructure, parameters, module nodes and hosted AI resources by brain. Per-brain configuration overrides global/code defaults. A two-brain PostgreSQL model test pins resource isolation. Silo discovery uses an explicit composition annotation. The memory-only AppHost path now configures development clustering and can be referenced by a server.
- Six package-owned test suites replace the mixed ownership in the Kernel suite; duplicate brain-establishment coverage and a brittle exact-prose capacity assertion were removed.
- The former package-consumer script verified five consumers from a fresh cache and checked dependency boundaries; that script has since been removed (see status below).

Deliberate compatibility decisions:

- Keep existing `x-intochat-*` JSON extension keys. A cosmetic rename would create another migration without reducing runtime complexity.
- Keep `IDigitalBrain.Get<T>` capable of addressing keyed Orleans actors: the new identity actors deliberately do not implement `INeuron`.
- `Signal.Publisher` is documented as routing metadata, never authentication evidence; the publishing neuron overwrites it.
- Keep one synchronized prerelease release train and `net11.0` until a tested multi-target runtime matrix exists. Signing remains off. See the upgrade document for policy and commands.

Validation (2026-10-03):
- Release solution build: **0 warnings, 0 errors**.
- **30 unit/architecture suites: 949 passed, 6 skipped, 0 failed**, including targeted reruns after final changes.
- **73 NuGet packages** produced. Five isolated consumers (HTTP script, Kernel, AppHost adapters, Server + Platform, all packages) restored and built from the new feed; dependency-boundary checks passed.
- All **522 existing serialized type baselines** retain their field IDs. Three new state types and two additive migration fields are reviewed in the baseline (525 types total).
- `git diff --check` passed. Container-backed E2E and a production-state upgrade rehearsal were not run.

**Current release status — script removal:** the standalone release verifier, package-consumer
verifier and publishing scripts have been removed at the user's request. Their CI invocation
and evidence uploads have also been removed. The successful runs recorded below are
historical evidence, not ongoing automated release gates.

**Next priority:** run the existing build/pack/test CI and decide which package-consumer
checks belong in normal test projects. Rehearsal with actual state is conditional on having
an existing legacy deployment to upgrade; it is not a prerequisite for fresh installations.
Publishing and package verification manifests are no longer automated by this workflow.
The `eng/ReleaseRehearsal` sources remain, without a supported entry point.

Legacy `.orleans` files are retained compatibility fixtures from the old source revision, moved with the Platform tests. Their purpose and capture provenance are documented in `Identity/LegacyState/README.md` beside the fixtures.

## Unified deployment and release lifecycle — 2026-10-03

Historical implementation and validation before the script removal described above. Runtime
identity and migration changes remain; the standalone release automation does not.

The remaining gates shared one problem: independently reconstructed deployment identities,
resource names, persistence assumptions and process lifetimes. They now use resolved AppHost
references and an explicit storage binding, with one package-based rehearsal workflow.

- AppHost resolves service ID and cluster ID independently. E2E selects a named server when
  several exist, then uses that server's actual brain configuration. Scoped infrastructure
  projects stable connection aliases and Orleans service keys to server/client processes.
- Local development Orleans endpoints bind localhost without TCP proxy translation, so
  static clients reach the advertised gateway and can fetch the cluster manifest.
- Azure Blob grain names do **not** include `ServiceId`. Platform now stores a versioned
  `.digitalbrain-deployment.json` binding beside grain state and rejects changed service IDs
  or master keys. Existing unbound storage requires explicit maintenance adoption. This
  preserves all existing grain blob names; it is not a new namespace migration.
- Migration has read-only inspection and explicit apply. Preflight validates source scopes,
  principal defaults and the declared grant inventory before writes. The directory records
  the source/mapping digest before binding storage or importing actors. Interrupted
  grant-only migrations block normal use even with an otherwise empty legacy directory.
- Normal startup never performs migration. Maintenance skips bootstrap and registration
  seeding, refuses Platform HTTP exposure, and does not provision cookie storage. Existing
  completed checkpoints remain authoritative and cannot recreate revoked authorization.
- Durable test volumes have explicit ownership across server sessions. Docker failures and
  remaining writers fail the operation; unsuccessful rehearsals retain their volumes.
- Release verification restores package consumers into a fresh isolated cache, compiles an
  old writer/reader from pinned revision `e39ecb35df23b80ef2f9340ea80a8bd6c8f8e3a5`,
  and runs real Azurite persistence with packed candidate code. No new binary fixtures were added.
- CI runs the verifier after packing and retains the exact package files plus their SHA-256
  manifest. Publishing validates the complete package set before pushing anything.

See [the upgrade procedure](IDENTITY-UPGRADE.md) for commands and maintenance configuration.
The complete inventory and original key for an actual deployment remain operator-supplied
inputs; an arbitrary mapping list does not prove that every legacy grant store was inventoried.

Validation:
- Release solution build: **0 warnings, 0 errors**.
- Four focused unit/architecture suites: **162 passed, 0 skipped, 0 failed** (Hosting 10,
  Platform 117, Architecture 23, Testing.Unit 12). Existing serialized field IDs remain intact;
  the directory adds only the reviewed `MigrationPlanId` field at ID 5.
- **73 packages** passed all five isolated consumer builds and dependency-boundary checks.
- **14 lifecycle checks passed:** packed composition, two-brain isolation, resource rename
  with restart, memory composition, changed-service rejection, read-only preflight, process
  termination after the plan checkpoint, blocked incomplete startup, termination after an
  account write, migration resume, revocation across restart/retry, key reuse, wrong-key
  rejection, and snapshot rollback read by the pinned old version.
- Inspection was checked against a byte-for-byte snapshot of all blob containers. Rollback
  restored all original blob containers, including removal of new cookie keys and the
  storage-binding marker. Successful owned volumes were removed without forced deletion.
- Publishing preflight rejected a tampered manifest and missing credentials; **nothing was
  published**. The complete external-provider/product E2E matrix and production-state copy
  rehearsal were not run locally; CI retains its existing broader E2E suite.
- `git diff --check` passed. Release commands, prerequisites, and storage-adoption semantics
  are documented in `docs/IDENTITY-UPGRADE.md`.

## Memory module removal

Removed the Memory implementation, contracts and tests, plus application/test-host registrations
and project references. Qdrant remains the vector-store module. This removes the separate
Memory API and its neuron-backed state; Qdrant is not an automatic migration of that data.
