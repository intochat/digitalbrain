# Integrations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the module-options attribute/contract/flattener chain with typed `IModule<TOptions>`, make provider registration an observable neuron with vault-backed secrets, and rename connectors to integration accounts with a catalog that reports availability instead of throwing.

**Architecture:** Options serialize once as JSON across the AppHost→host boundary and validate in one place. `IIntegrationRegistration` neurons (deployment-scoped, keyed `integration/{id}`) hold layer-1 credentials in the Secrets vault, seeded from config at startup; modules `Release` them at the point of use. Accounts (today's connectors) keep their persisted shape under new C# names in a neutral Integrations module. Capabilities are derived, never stored; settings reference them.

**Tech Stack:** .NET 11 / Orleans, ASP.NET Core minimal APIs, xUnit (`UnitTest.Create().WithModule<...>()`), System.Text.Json, Flutter/Dart shell.

**Spec:** `docs/superpowers/specs/2026-09-30-integrations-design.md`

## Global Constraints

- Persisted state discipline: Orleans aliases and `[Id(n)]` slots on persisted records never change or renumber; rename C# names only. Before renaming any signal alias, verify the signal is not persisted/replayed; if in doubt, keep the alias.
- Secrets never appear in module options, in `RegistrationSnapshot`, in logs, or in HTTP responses. Only field NAMES travel; values go vault→`Release` at the point of use.
- Wire-shape changes in C# are mirrored in the Dart shell in the same task.
- Brain scoping rules from the previous refactor hold: brain routes mount via `BrainRoutes.Group`, scope from `BrainScope.CurrentId()`; modules never reference IntoChat.
- No boilerplate `/// <summary>`; sentence-shaped fact names; `TestContext.Current.CancellationToken`.
- Build/test per project (`dotnet test src/<path>`), never the `.slnx`. IntoChat Tests/Unit has 5 known pre-existing failures (HostedDeployment ×2, PathTruth, ModuleConfigurationContract ×2 — note: the ModuleConfigurationContract facts may DISAPPEAR legitimately in Task 2 when contracts are deleted; deleting a known-failing fact along with the code it tested is correct, say so in the report).
- Final smoke: `aspire run` from `src/Applications/IntoChat/AppHost`, all resources Healthy.

## Review Focus

1. **A module option type that doesn't JSON round-trip** (e.g. `Uri` with defaults, dictionaries like GitHub's `Repositories`) silently loses values crossing the AppHost boundary → module misconfigured with no error. Pinned in Task 1 (round-trip theory over every composed options type).
2. **Registration snapshot leaking a secret value** through `Configure`'s response, a signal payload, or an error message. Pinned in Task 3 (facts assert names-only everywhere, including the rejected-input path).
3. **Seeding overwriting an operator's UI edit** on restart (seed must apply only to an Unconfigured registration). Pinned in Task 3.
4. **`Release` callable by an unauthorized caller** — layer-1 credentials are the crown jewels; only module-internal/trusted paths may release. Pinned in Task 3 (untrusted caller fact).
5. **Gmail connect against an unconfigured registration returning 500** instead of the catalog-shaped 409/422 — the exact UX bug this design exists to kill. Pinned in Task 4.

---

### Task 1: Kernel — `IModule<TOptions>`, generic compile/bind

**Files:**
- Modify: `src/Modules/DigitalBrain/Kernel/DigitalBrain/IModule.cs`
- Create: `src/Modules/DigitalBrain/Kernel/DigitalBrain/Composition/ModuleOptionsSerialization.cs`
- Modify: `src/Modules/DigitalBrain/Kernel/DigitalBrain/Composition/ModuleConfiguration.cs` (+ the ModuleDefinition/loader files it works with — read `DigitalBrainRuntimeHostingExtensions.LoadModules` first)
- Test: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Runtime.Tests.Unit/Composition/ModuleOptionsFacts.cs` (create)

**Interfaces:**
- Consumes: existing `ModuleDefinition(Type, IReadOnlyDictionary<string,string?>)`, `ModuleConfiguration<TModule>`, module loading from `DigitalBrain:Modules`.
- Produces (every later task builds on these exact signatures):

```csharp
public interface IModule<TOptions> : IModule where TOptions : class, IModuleOptions, new()
{
    static virtual Type? Hosting => null;
}

public interface IModuleOptions
{
    void Validate();
}

public static class ModuleOptionsSerialization
{
    public const string OptionsKeySuffix = ":Options";  // DigitalBrain:Modules:{ModuleName}:Options
    public static ModuleDefinition Compile<TModule, TOptions>(TOptions options)
        where TModule : IModule<TOptions> where TOptions : class, IModuleOptions, new();
    public static TOptions GetModuleOptions<TOptions>(this IConfiguration configuration, string moduleName)
        where TOptions : class, IModuleOptions, new();   // deserialize + Validate(); new() when key absent
}
```

- [ ] **Step 1: Write the failing facts**

```csharp
public sealed class ModuleOptionsFacts
{
    private sealed class FakeOptions : IModuleOptions
    {
        public Uri Endpoint { get; set; } = new("https://default.example/");
        public Dictionary<string, string> Bindings { get; set; } = [];
        public bool Flag { get; set; }
        public void Validate() { if (!Endpoint.IsAbsoluteUri) throw new ArgumentException("Endpoint must be absolute."); }
    }
    private sealed class FakeModule : IModule<FakeOptions>
    {
        public void Configure(ISiloBuilder silo) { }
    }

    [Fact]
    public void OptionsSurviveTheCompileAndBindRoundTripByValue()
    {
        var options = new FakeOptions { Endpoint = new("https://x.example/api"), Flag = true, Bindings = { ["a"] = "1" } };
        var definition = ModuleOptionsSerialization.Compile<FakeModule, FakeOptions>(options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(definition.Settings).Build();
        var bound = configuration.GetModuleOptions<FakeOptions>(nameof(FakeModule));
        Assert.Equal(options.Endpoint, bound.Endpoint);
        Assert.True(bound.Flag);
        Assert.Equal("1", bound.Bindings["a"]);
    }

    [Fact]
    public void ValidationRunsAtCompileAndAtBind()
    {
        Assert.Throws<ArgumentException>(() =>
            ModuleOptionsSerialization.Compile<FakeModule, FakeOptions>(new() { Endpoint = new Uri("/relative", UriKind.Relative) }));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["DigitalBrain:Modules:FakeModule:Options"] = """{"Endpoint":"/relative"}""" }).Build();
        Assert.ThrowsAny<Exception>(() => configuration.GetModuleOptions<FakeOptions>(nameof(FakeModule)));
    }

    [Fact]
    public void AbsentOptionsBindToValidatedDefaults()
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Equal(new Uri("https://default.example/"), configuration.GetModuleOptions<FakeOptions>(nameof(FakeModule)).Endpoint);
    }
}
```

(Adapt `definition.Settings` to `ModuleDefinition`'s real member name after reading it.)

- [ ] **Step 2: Run, verify failure** — `dotnet test src/Modules/DigitalBrain/Kernel/DigitalBrain.Runtime.Tests.Unit --filter ModuleOptionsFacts`. Expected: `IModuleOptions` not defined.
- [ ] **Step 3: Implement** the two interfaces and `ModuleOptionsSerialization`. `Compile` = `options.Validate()` → `new ModuleDefinition(typeof(TModule), new Dictionary<string,string?> { [$"DigitalBrain:Modules:{typeof(TModule).Name}:Options"] = JsonSerializer.Serialize(options) })`. `GetModuleOptions` = read the key; absent → `new TOptions()`; present → `JsonSerializer.Deserialize<TOptions>`; then `Validate()`. Do NOT remove the existing contract machinery yet (Task 2 migrates modules; both paths coexist for one task).
- [ ] **Step 4: Run facts** — expected PASS. Build the kernel.
- [ ] **Step 5: Commit** — `git commit -m "Give modules typed options that survive the AppHost boundary as one JSON value."`

---

### Task 2: Migrate every module off contracts/attributes/flatteners

**Files:**
- Modify: every module with `[ModuleConfiguration]` / `ModuleConfigurationContract` — enumerate first: `grep -rln "ModuleConfigurationContract\|\[ModuleConfiguration" src/Modules src/Applications --include=*.cs`. Known set: Gmail, AI, CSharp, Flutter, Coding, Memory, GitHub, Identity, Connector(Sdk), Salesforce, Supabase, Assistant, CustomerResearcher, Apps, Files, Time, ClickHouse (+ any the grep adds).
- Modify: `src/Applications/IntoChat/AppHost/AppHost.cs` (the `.WithModule<T>(...)` fluent chain), the `ModuleConfiguration<TModule>` composition type, `UnitTest`/`E2ETest` `WithModule` overloads in `src/Testing/`.
- Delete: `ModuleConfigurationContract<,>` base, every per-module `*ConfigurationContract` class, every `Define(options)` flattener, `[ModuleConfiguration]` and `[ModuleHosting]` attribute types, per-module `*ModuleConfiguration` extension classes (keep an extension ONLY where the AppHost reads clearly better — e.g. `ai.WithLlm<IGpt56Luna>()` marker helpers may stay as thin wrappers over typed option edits, WITHOUT tracking-key strings).
- Test: each module's existing unit suite; extend the Task-1 round-trip fact into a theory over all composed options types.

**Interfaces:**
- Consumes: Task 1's `IModule<TOptions>`, `Compile`, `GetModuleOptions`.
- Produces: every module declares `IModule<TOptions>` (modules with no options implement plain `IModule`); `[ModuleHosting("string")]` → `static Type? Hosting => typeof(GmailModuleHosting);` where the hosting assembly is referenced, else the naming convention `{ModuleNamespace}.Aspire.Hosting.{ModuleName}Hosting` resolved by the loader (read how `[ModuleHosting]` is consumed in the AppHost side first and mirror that resolution); test composition becomes `WithModule<TModule, TOptions>(Action<TOptions>)`.

Per module, the mechanical recipe:
1. `class XModule : IModule` → `IModule<XModuleOptions>` (skip if optionless).
2. Move ALL validation from `Define()`/config classes into `XModuleOptions.Validate()`; delete `Define()`.
3. Delete the contract class and both attributes; add `static Type? Hosting` where a hosting class exists.
4. Module reads options via `configuration.GetModuleOptions<XModuleOptions>(nameof(XModule))` at its current read sites (or receives them through the loader if the loader binds centrally — pick ONE mechanism in the loader and use it everywhere).
5. Secrets stay where they are for now (Gmail ClientId etc. keep their current config path until Tasks 3–5) — this task changes the OPTIONS transport only.
6. Run that module's unit suite before moving to the next.

- [ ] **Step 1: Read the loader** (`DigitalBrainRuntimeHostingExtensions.LoadModules` + `ModuleConfiguration<TModule>` + how AppHost's `WithModule` compiles definitions) and write down the one binding mechanism chosen. Add it to the report.
- [ ] **Step 2: Migrate the kernel loader + composition types** to compile via `ModuleOptionsSerialization` for `IModule<TOptions>` modules while still supporting plain `IModule`.
- [ ] **Step 3: Migrate modules one by one** (recipe above), running each suite. AppHost chain updated as each module flips; keep `aspire run` startable at the end.
- [ ] **Step 4: Delete the dead machinery** (contract base, attributes) once zero references remain; `grep -rn "ModuleConfigurationContract\|ModuleHostingAttribute\|\[ModuleConfiguration" src --include=*.cs` → 0.
- [ ] **Step 5: Extend Task-1 facts** with a theory enumerating every `IModule<TOptions>` in the product composition and round-tripping a default-constructed options instance (catches non-serializable members forever).
- [ ] **Step 6: Run** all touched module suites + IntoChat Tests/Unit (the 2 ModuleConfigurationContract known-failures disappear WITH their subject — report it) + `aspire run` smoke.
- [ ] **Step 7: Commit per module or per small group**, final message `"Retire the module configuration contract chain for typed JSON options."`

---

### Task 3: `IntegrationDefinition` + registration neuron + seeding + catalog

**Files:**
- Create: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk/Integrations/IntegrationDefinition.cs`, `IIntegrationRegistration.cs`, `RegistrationNeuron.cs`, `RegistrationSeeder.cs`, `IntegrationCatalogEndpoints.cs`, `IntegrationsModule.cs` (new module in Sdk beside Connectors; it will also absorb accounts in Task 6)
- Test: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk.Tests.Unit/Integrations/RegistrationFacts.cs` (find the Sdk test project via Glob; if none, put facts in the Kernel Runtime.Tests.Unit)

**Interfaces:**
- Consumes: `SecretsModule`/`ISecrets` vault + `SecretRef` (read `src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk/Secrets/` first and use its real API), `CallerContext`/`CallerContextStamper`, `Neuron<TState>` base, `Signal`.
- Produces (exact spec contracts):

```csharp
public sealed record IntegrationDefinition(string Id, string DisplayName,
    string[] SecretFields, string[] SettingFields)
{
    public static IntegrationDefinition For(string id, string displayName);
    public IntegrationDefinition RequiresSecret(string field);
    public IntegrationDefinition RequiresSetting(string field);
}

[Alias("integration.registration"), Orleans.Metadata.DefaultGrainType("integration.registration")]
public interface IIntegrationRegistration : INeuron   // key: "integration/{id}"
{
    [ReadOnly] Task<RegistrationSnapshot> Read();
    Task<RegistrationSnapshot> Configure(ConfigureRegistration request);
    Task<RegistrationSnapshot> Clear(string field);
    Task<ReleasedRegistration> Release(CallerContext caller);
}
// RegistrationSnapshot { [Id(0)] IntegrationId, [Id(1)] RegistrationStatus, [Id(2)] string[] MissingFields }
// ConfigureRegistration { [Id(0)] Dictionary<string,string> Values }  (field name → value; values → vault immediately)
// ReleasedRegistration  { [Id(0)] Dictionary<string,string> Values }  (never serialized to state; check Orleans transient use)
// RegistrationChanged(string IntegrationId, RegistrationStatus Status) : Signal, alias "integration.registered"
// RegistrationStatus { Unconfigured, Partial, Ready }
```

- Definitions discovery: modules expose `public static IntegrationDefinition Integration` (or `Integrations` array for AI). The IntegrationsModule collects them from the composed modules — read how modules are enumerable at runtime (`GetServices<IModule>()`) and collect via reflection over the module types; register the collected set as `IReadOnlyList<IntegrationDefinition>` in DI.
- Catalog endpoint: `GET /integrations` (NOT brain-scoped — deployment info) → `[{ id, displayName, status, missingFields }]`. Registration write endpoints `POST /integrations/{id}/registration` + `DELETE .../registration/{field}` gated by the existing grant/enforcement stages for the owner role (read how Identity gates owner-only actions and reuse it; if no owner-only HTTP gate exists yet, gate on authenticated principal whose Member role is Owner via `IIdentityDirectory` — state the choice in the report).
- Seeding: `RegistrationSeeder` is a hosted startup task reading `DigitalBrain:Integrations:{id}:{Field}` and calling `Configure` ONLY when `Read()` returns `Unconfigured` (not Partial — a partial edit is an operator's).

- [ ] **Step 1: Write the failing facts** (all six, full code, against `UnitTest.Create().WithModule<IntegrationsModule>().WithModule<SecretsModule>()`):
  - `ConfiguringAllFieldsMakesTheRegistrationReadyAndSignals` (observe `RegistrationChanged`; snapshot has empty MissingFields and NO values anywhere — assert the snapshot type has no value-bearing member via its shape).
  - `APartialConfigurationReportsMissingFieldNamesOnly`.
  - `ReleaseReturnsValuesOnlyForATrustedCaller` (a `CallerKind.User`-stamped caller from HTTP is refused — decide the trust rule from `TrustedEdge`/`CallerKind` after reading them; the rule: release is for module-internal grain calls, not user HTTP; assert an untrusted caller throws/denies and a trusted one gets values).
  - `SeedingAppliesOnlyToAnUnconfiguredRegistration` (seed → Ready; operator `Clear`+`Configure` one field → reseed does NOT overwrite).
  - `ClearingAFieldReturnsToPartialAndRemovesTheVaultEntry`.
  - `TheCatalogReportsEveryDefinitionWithItsStatus` (route-table + response-shape fact for `GET /integrations`).
- [ ] **Step 2: Run, verify failure.**
- [ ] **Step 3: Implement** definition, neuron (values → vault via ISecrets, state holds `SecretRef`s keyed by field name in a `Dictionary<string,SecretRef>` — persisted: `[Id]` on state members, concrete types), seeder, catalog + registration endpoints, module registration in AppHost composition.
- [ ] **Step 4: Run facts + Sdk/kernel suites.** Expected PASS.
- [ ] **Step 5: Commit** — `git commit -m "Make provider registration an observable neuron with vault-backed secrets."`

---

### Task 4: Gmail onto registration

**Files:**
- Modify: `src/Modules/Google/Gmail/DigitalBrain.Modules.Google.Gmail/` — `GoogleModule.cs` (add `public static IntegrationDefinition Integration` per the spec: secrets ClientId, ClientSecret; setting PublicOrigin), OAuth callback + `GmailLogins` + token exchange sites
- Delete: `Configuration/GmailOAuthConfiguration.cs`, `GmailUnavailableException`'s "configure in Aspire" throw path (the exception type may survive for genuine runtime faults)
- Modify: Gmail E2E facts (`GmailOAuthCallbackFacts` seeds the registration instead of `PrivateConfiguration` client id/secret)
- Test: Gmail unit + E2E-that-run (OAuth callback fact runs in-process)

**Interfaces:**
- Consumes: Task 3's `IIntegrationRegistration.Release`, `RegistrationSnapshot`; the seeder key shape `DigitalBrain:Integrations:gmail:ClientId`.
- Produces: the pattern every provider task copies: definition on the module + `Release` at point of use + user-facing unavailability as data.

- [ ] **Step 1: Write the failing fact** — Review Focus #5:

```csharp
[Fact]
public async Task ConnectingGmailAgainstAnUnconfiguredRegistrationExplainsInsteadOfCrashing()
{
    // Start brain WITHOUT seeding the gmail registration. Hit the login-start route
    // (BrowserLogins Require path). Assert: a 4xx (409 or 422) whose body carries
    // { integration: "gmail", status: "Unconfigured", missing: [...] } — and NOT a 500,
    // NOT the "configure ... privately in Aspire" text.
}
```

Write it fully against the real login-start surface (read `BrowserLogins.Require` and the connections `services/{provider}/start` route to pick the right entry point).
- [ ] **Step 2: Run, verify failure** (today it throws `GmailUnavailableException`).
- [ ] **Step 3: Implement** — definition on `GmailModule`; callback/token-exchange call `Release`; `GmailLogins.PublicOrigin` reads the registration's PublicOrigin setting (settings may be read from `Read()`-adjacent API — non-secret settings can live in the snapshot; extend `RegistrationSnapshot` with `[Id(3)] Dictionary<string,string> Settings` — append, never renumber); the login-start path checks status and returns the explanatory shape.
- [ ] **Step 4: Migrate the E2E facts' setup** to seeding (`DigitalBrain:Integrations:gmail:ClientId` etc. via the test host's configuration).
- [ ] **Step 5: Run Gmail suites** (unit + the in-process E2E facts). Expected PASS.
- [ ] **Step 6: Commit** — `git commit -m "Register Gmail through the integration neuron and answer unavailability as data."`

---

### Task 5: AI providers and GitHub onto registration

**Files:**
- Modify: `src/Modules/AI/DigitalBrain.Modules.AI/` — `AIModule` gains `public static IntegrationDefinition[] Integrations` (one per `AiProvider` needing keys: OpenAI/Anthropic/Google/Azure/DeepSeek per the provider matrix — enumerate `AiProvider` first — plus Tavily); `Providers/Common/ApiKeyProviderFactory.cs` (`RequireApiKey` reads the provider's registration via `Release`); `AIOptions` drops key/secret members, keeps endpoints+hosting+defaults (endpoint stays an option OR moves to a registration setting — decide by who changes it: operator topology → registration setting; test stubs → option. Pick registration setting, mirroring Gmail's PublicOrigin, and let tests seed it).
- Modify: `src/Modules/Microsoft/GitHub/DigitalBrain.Modules.Microsoft.GitHub/` — `Integration` with secret `PrivateKey`, settings `AppId`; the per-repo `Repositories` dictionary STAYS in options (topology).
- Test: AI module unit suite (scripted providers), GitHub unit suite.

**Interfaces:**
- Consumes: Task 3 contracts; Task 4's pattern.
- Produces: `ILlmProviderFactory.IsConfigured` derived from registration status → the settings screen can show per-provider availability.

- [ ] **Step 1: Write failing facts** — `AnUnregisteredProviderReportsUnavailableInsteadOfThrowing` (factory `IsConfigured` false + catalog row Unconfigured; a chat request routed to it fails with the explanatory shape, not a raw exception) and `ASeededProviderKeyIsReleasedOnlyInsideTheFactory` (scripted provider; assert the key string never appears in any snapshot/catalog response).
- [ ] **Step 2: Run, verify failure.**
- [ ] **Step 3: Implement** both modules per the Task-4 pattern; migrate test setups from options-keys to seeded registrations.
- [ ] **Step 4: Run AI + GitHub suites + Assistant suite** (it consumes models). Expected PASS.
- [ ] **Step 5: Commit** — `git commit -m "Move AI provider and GitHub app credentials into integration registrations."`

---

### Task 6: Connectors → integration accounts (rename + neutral module home + routes + Dart)

**Files:**
- Move+rename: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk/Connectors/*` → `.../Integrations/Accounts/`: `IConnectors`→`IIntegrationAccounts`, `ConnectorsNeuron`→`IntegrationAccountsNeuron`, `ConnectorRecord`→`IntegrationAccount` (+ member `Source`→`IntegrationId`), `ConnectRequest`→`ConnectAccount`, `ConnectorStatus`→`AccountStatus`, `ConnectorStatusChanged`→`AccountStatusChanged`, `ConnectorDisconnected`→`AccountDisconnected`, `ScopedConnectorRecords`→`ScopedAccounts`. **Orleans aliases (`"connections"`, `"connections.connection"`, …) and every `[Id(n)]` stay** — persisted; add a one-line comment `// alias predates the integrations rename; persisted, do not touch`. Signal alias renames only if a grep proves the signals aren't persisted/replayed from storage (check the watch/replay path); otherwise keep.
- Move: the accounts HTTP endpoints out of the Salesforce module (`Connections/ConnectionsEndpoints.cs`, `WorkspaceConnectionRecords`-successor) into the Sdk `IntegrationsModule` (Task 3's module — the neutral home; resolves the parked brain-refactor finding). The Salesforce-specific OAuth start/probe pieces stay in Salesforce, discovered through the existing `BrowserLogins`/`IConnectorProbe` seams.
- Modify: routes `/brains/{brainId}/connections*` → `/brains/{brainId}/integrations/accounts*`; the old Sdk `ConnectorEndpoints` (`/connections/{owner}`) folds into the same module under the new path.
- Modify: Dart — `ui_client.dart` connection routes + any screens (`grep -rn "connections" src/Modules/Google/Flutter/app --include=*.dart` and rename wire paths; UI copy "Connections" may stay as product vocabulary).
- Test: moved `WorkspaceConnectionsFacts` successor + Salesforce suite + Flutter `flutter test`.

- [ ] **Step 1: Grep the persistence question** (are `ConnectorStatusChanged` signals replayed from stored state?) and record the alias decision in the report before renaming.
- [ ] **Step 2: Rename+move mechanically**; update all references (Salesforce, Gmail, tests).
- [ ] **Step 3: Route change + Dart mirror in the same commit**; run `flutter analyze && flutter test`.
- [ ] **Step 4: Run** Sdk/kernel, Salesforce, Gmail, IntoChat unit suites. Expected PASS (known failures only).
- [ ] **Step 5: Commit** — `git commit -m "Rename connectors to integration accounts and give them a neutral module home."`

---

### Task 7: Capabilities v1 — model selection through the catalog

**Files:**
- Create: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Sdk/Integrations/Capability.cs` (`sealed record Capability(string IntegrationId, string Kind, string Id, string Display)`) + a derivation service `ICapabilities { Task<Capability[]> List(CallerContext caller, string? kind = null, CancellationToken ct = default); }` implemented in `IntegrationsModule` by asking registrations (status) + the caller's accounts; AI module contributes model capabilities (`kind: "llm"`) for Ready providers via a small contribution seam (`ICapabilitySource` registered per module).
- Modify: the settings/model-selection path — find where the user's model choice is read (`grep -rn "Default.Model\|SelectedModel" src/Modules/AI src/Modules/DigitalBrain/Assistant --include=*.cs`) and route resolution through `ICapabilities`: an unavailable selection yields the explanatory shape (integration id + missing fields), and `GET /brains/{brainId}/integrations/capabilities?kind=llm` feeds the settings screen.
- Modify: Dart settings screen if it lists models from a wire shape that changes (mirror in same commit).
- Test: AI/Assistant facts.

- [ ] **Step 1: Write failing facts** — `CapabilitiesListOnlyReadyProvidersModels` (two providers, one seeded → only its models listed; derivation repeated after registration change reflects it — re-derived, not cached stale) and `SelectingAnUnavailableModelExplainsWhatIsMissing`.
- [ ] **Step 2: Run, verify failure.**
- [ ] **Step 3: Implement**; wire the endpoint under `BrainRoutes.Group(endpoints, "/integrations")`.
- [ ] **Step 4: Run AI + Assistant + Flutter suites.** Expected PASS.
- [ ] **Step 5: Commit** — `git commit -m "Derive capabilities from registrations and accounts and resolve model selection through them."`

---

### Task 8: Final sweep, docs, smoke

- [ ] **Step 1: Sweeps** (zero hits or listed-and-justified): `ModuleConfigurationContract|ModuleHostingAttribute|\[ModuleConfiguration` (0); `RequireApiKey|RequireConfigured|GmailOAuthConfiguration` (0); `IConnectors\b|ConnectorRecord\b` in src/*.cs (0 outside preserved aliases/comments); `"/connections"` wire paths in .cs/.dart (0).
- [ ] **Step 2: CLAUDE.md** — module contract line gains: modules declare `IModule<TOptions>` (options validate once, JSON across the boundary) and ship `IntegrationDefinition`s; registrations are deployment neurons; connectors are now integration accounts.
- [ ] **Step 3: Run** all touched suites; `aspire run` all-Healthy; open the shell and confirm the integrations catalog answers (network tab `/integrations`).
- [ ] **Step 4: Commit** — `git commit -m "Finish the integrations refactor: sweep, document, smoke."`
