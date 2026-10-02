# NuGet Readiness Assessment — Kernel Ring, Client, Sdk, Platform, Aspire

Date: 2026-10-03. Scope: `DigitalBrain`, `DigitalBrain.Modules.Kernel`, `.Contracts`, `.Client`,
`DigitalBrain.Modules.Sdk`, `DigitalBrain.Platform`, `DigitalBrain.Aspire`, `DigitalBrain.Aspire.Hosting`
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

---

## Critical findings

| # | Project | Location | Finding |
|---|---------|----------|---------|
| C1 | Platform | `PlatformHosting.cs:28` | ✅ **Addressed in `9c1f84870`.** `FakeKeyVault`, `KeyVaultKeyWrapper`, `IKeyVault` and `DpapiKeyWrapper` deleted; `AddPlatform` registers `AddMasterKeyWrapper()` itself and the silo refuses to start without `DIGITALBRAIN_MASTER_KEY` (`ValidateOnStart`). The unit-test harness seeds a throwaway master key per cluster, so tests run the real wrapper. |
| C2 | Client | `DigitalBrain.Client.csproj:17-27` | Two products in one package; script consumers inherit Orleans/Azure/OTel/ASP.NET. Split script-edge client into a lean package (needs ~`System.Net.Http.Json`). |
| C3 | Sdk | whole project | Grab-bag SDK: grains, middleware, SQL compiler, MCP client, identity, secrets APIs in one assembly. Split public module-author surface from unpublished platform contracts. |
| C4 | Sdk | `Secrets/ISecrets.cs:17`, `Integrations/IIntegrationRegistration.cs:33`, `Auth/TokenHandoff.cs`, `Identity/Accounts.cs:41` | Plaintext-credential-returning APIs on the public NuGet surface; only runtime `[PlatformOnly]` protects them. Move to a non-published platform-contracts assembly. |
| C5 | Aspire | `DigitalBrain.Aspire.csproj` | Misnamed (it's the silo runtime, not an Aspire client integration), product-coupled ("Silo host wiring for IntoChat"), and references `DigitalBrain.Platform` — publishing it publishes the credential ring. Rename (e.g. `DigitalBrain.Silo`) and cut the Platform dependency from the public graph. |
| C6 | repo | `Directory.Build.props` | `PackageIcon` without a packed icon file → `dotnet pack` fails/ships wrong. Add the icon + pack item or drop the property; add a pack smoke test to CI. |
| C7 | Kernel | `Composition/CompositionOverrideTransport.cs` | Test-only global static in the shipped runtime; `Take` doesn't remove entries (unbounded growth, token reuse). Move to DigitalBrain.Testing; fix the leak regardless. |

## High findings

### Dependency / layering
- **H1** Kernel csproj: server runtime references the Client project — invert; the shared records belong in Contracts.
- **H2** Kernel csproj:8: `FrameworkReference Microsoft.AspNetCore.App` on the core runtime. Split `Enforcement/BrainRoutes|BrainAccessFilter` + `IModule.Configure(IEndpointRouteBuilder)` into a `.AspNetCore`/hosting companion package.
- **H3** `Aspire.Hosting.csproj:12-13`: AppHost package references the full Kernel implementation; depend on Contracts/composition abstractions only.
- **H4** Client `Edge/ScriptEdgeProtocol.cs`: wire protocol internal + IVT to a production module. Promote to Contracts; delete the production IVT.
- **H5** Contracts: relocate `TypeCatalog`/`SchemaExtensions`/`SecretRef` parsing and `IntentContext` (logic) out of Contracts; delete dead `Types/ValueTypes.cs` structs (unused except `Reference`).
- **H6** Platform csproj: "the ring scripts can never see" is `IsPackable=true`. Decide: private feed or `IsPackable=false`.

### Security / correctness
- **H7** Kernel `Enforcement/BrainAccessFilter.cs:36-38`: unauthenticated header-less requests bypass membership ("open development posture") — insecure-by-default in a public package; make it explicit opt-in, and pin the current behavior with a test.
- **H8** Platform `Identity/AccountSession.cs:100-103`: no credential ⇒ everyone is `owner`. Same opt-in treatment.
- **H9** Sdk `Auth/LoginPage.cs:14-16`: unencoded string interpolation into HTML — XSS the moment any consumer passes tainted strings. HtmlEncode.
- **H10** Platform `Identity/Directory/IdentityDirectoryNeuron.cs`: god grain — every account, member, invitation and password hash in one list-shaped global grain state; full scan per auth.
- **H11** Aspire `DigitalBrainRuntimeHostingExtensions.cs:24-29`: `DigitalBrain:Testing:PrivateConfiguration` reads an arbitrary JSON file into config in the production path; move behind a Testing seam (same family as C7).
- **H12** Aspire: Azure welded in with no seam (`:31-33,100-126`) — no provider choice, Orleans Dashboard unconditional (`:54`). Accept options; make Azure/Dashboard opt-in.
- **H13** Aspire.Hosting `Brain/DigitalBrainBuilder.cs`: two classes in one; `Orleans`/`GrainState` are `null!` until `AttachRuntime`; `Materialize()` only runs from `WithReference`, so an unreferenced brain never validates. Split and construct fully.
- **H14** Aspire reflection contracts: `"FullName, Assembly"` env strings + `Activator.CreateInstance` (`DigitalBrainRuntimeHostingExtensions.cs:77-98`) and `Assembly.Load(name + ".Aspire.Hosting")` + `<Type>Hosting` name convention (`DigitalBrainHostingExtensions.cs:124-142`). Replace with an assembly-level attribute or explicit registration; make `FindModuleHosting` internal.
- **H15** Client `BrainClientHosting.cs:16`: Azure Tables clustering hardcoded in the public entry point; `BrainOptions` is internal yet registered via public API (unconfigurable). 
- **H16** Kernel `Composition/ModuleDefinition.cs:24-25`: `CreateModule()` per Configure call — stateful modules silently get two instances. Cache or contract statelessness.
- **H17** Sdk twin `IIntegrationAccounts` interfaces (Sdk.Integrations.Accounts vs Contracts.Integrations) — same name, different neurons; rename one.
- **H18** Naming unification (systemic defect 5): one convention for package id = assembly = root namespace = folder, done before first publish. Includes `DigitalBrain` meta-package (ns `DigitalBrain.Contracts`) and the two Sdk files in `DigitalBrain.Identity`.

## Medium findings (abridged — see per-project notes below)

- Contracts `IntentContext.cs`: unsynchronized `List` under concurrent `AddUsage`; `AsyncLocal` stack breaks on out-of-order dispose. (Also move per H5.)
- Contracts `TypeCatalog.cs`: kind list maintained in four places (two switches, BuildCatalog, embedded JSON); derive from one. `ValidateDateTime` drops offsets despite promising ISO 8601.
- Contracts `SchemaExtensions.cs`: `x-intochat-*` vendor keys hard-code the product name in a platform package.
- Contracts `Signals/NeuronActivity.cs:8` and `CallRequest.SemanticTypeIds`: `IReadOnlyList<string>` on serialized records violates the repo's own concrete-arrays rule.
- Kernel `LocalSignalHub.cs:60-69`: fire-and-forget observer dispatch swallows exceptions, no backpressure; class fuses two hubs.
- Kernel `Neuron.cs:42-45`: reflection + attribute scan on every activation — cache per type. `PublishAsync` silently no-ops without a hub.
- Kernel `Persistence/GrainDocumentStore.cs`: magic 128 retries, no backoff; index `AddAsync` not atomic with write (crash ⇒ doc invisible to `ListIdsAsync`). `DocumentGrain` et al. public — make internal. `InMemoryDocumentStore` is test-only — move to Testing.
- Kernel `ModuleOptionsSerialization.cs:67-73`: redundant case-insensitive check after exact pattern match; casing policy inconsistent with `ModuleSettingsValidation`.
- Client: `ClientHostingDefaults` + `MapDefaultEndpoints` are Aspire service-defaults boilerplate in a client package — move to a host package. `DigitalBrainConnection` hardcodes `CSharpFile:*` module keys. `EdgeSignalSubscription` unbounded channel + no self-dispose, diverging from `SignalSubscription<T>` under the same interface. `DigitalBrainClient` spins up a full Generic Host just for env config + SIGTERM.
- Sdk: `BrowserLogins`/`TokenHandoff` duplicate nonce stores with inconsistent `TimeProvider` use; `MapJsonWebhook` is a no-op abstraction; `ModuleHttp.cs:15` maps status by `Message.Contains("changed since")`; `StoredRowsNeuron` grain in the SDK; `McpHttpSession` is Salesforce-only — move to that module.
- Platform: `AccountSession` body-sniffs `/identity/register` JSON inside the auth middleware — move the check to the endpoint/neuron. `IntegrationOperatorGate` reads raw config keys duplicating `BasicAuthOptions`. Caller self-escalation to `CallerKind.Platform` duplicated in two neurons — centralize one audited helper. ~~`DpapiKeyWrapper` hand-rolled P/Invoke; four `IKeyWrapper` implementations with DI-registration-order precedence~~ — ✅ resolved in `9c1f84870`: `MasterKeyWrapper` is now the single wrapper, registered in one place. |
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
1. ~~C1: remove `FakeKeyVault` from production composition; fail fast without real crypto.~~ ✅ Done in `9c1f84870` (also extracted the Aspire master-key parameter into `Aspire.Hosting/Brain/MasterKey.cs` and dissolved `AddIdentityStorage` into `AddCookieProtection`). Verified: Platform + both Aspire projects build clean; kernel-ring unit suite 197/197 green; broader module suites not re-run.
2. C6: fix `PackageIcon`/pack; add a `dotnet pack` smoke check.
3. C7 + H11: move `CompositionOverrideTransport` and the private-config hook to Testing; fix the token leak.
4. H9: HtmlEncode `LoginPage`.
5. H7/H8: make both open-posture defaults explicit opt-in, with pinning tests.

**Phase 1 — naming, once, before anything publishes**
6. H18: ratify one naming convention (package id = assembly = namespace = folder) and apply across the ring; rename `DigitalBrain.Aspire` → silo/runtime name (C5 part 1).

**Phase 2 — package shape (the big refactor)**
7. H4 + H1: move the script-edge wire protocol into Contracts; delete the Kernel→Client reference and the production IVT.
8. C2: split Client into lean script-edge package vs cluster-client/host package; drop Azure/OTel/ASP.NET from the script package (H15, service-defaults relocation).
9. H2: extract Kernel's ASP.NET enforcement into a hosting companion; Kernel loses the framework reference.
10. H5: purify Contracts (type catalog → own package/Sdk; IntentContext → runtime; delete ValueTypes).
11. C3 + C4: split the Sdk; move credential-shaped interfaces to an unpublished platform-contracts assembly; relocate `StoredRowsNeuron`, `McpHttpSession`.
12. C5 + H3 + H6: cut Aspire→Platform from the public graph; Aspire.Hosting depends on Contracts only; decide Platform packability.

**Phase 3 — API hardening**
13. H12–H14: options-based Aspire composition, attribute-based module-hosting discovery, builder split.
14. H10, H16, H17 and the Medium list.

**Phase 4 — tests**
15. Split the mono test suite along package lines; delete/fix the listed tests; close the High-priority gaps (BrowserLogins/TokenHandoff, RowQueryEvaluator, IntentContext), then Medium.
