# Integrations: registration as a neuron, typed module options

Status: ratified in conversation 2026-09-30 (owner: Vladyslav). Builds on the brain-as-neuron
standard (docs/superpowers/specs/2026-09-30-brain-as-neuron-design.md). Branch base:
`refactor/csharp-module`.

## Problem

Three distinct trust relationships hide behind two words ("connector", "options"), and only one
of them is modeled:

1. **The product's identity with a provider** (deployment-scoped): Google OAuth app
   ClientId/ClientSecret/PublicOrigin, GitHub App ids, AI provider API keys. Lives as ambient
   `IConfiguration` reached through a four-stage chain — `[ModuleConfiguration]` attribute →
   `ModuleConfigurationContract<TModule,TOptions>("key","list","strings")` → `Define(options)`
   hand-flattening typed options into config strings → a per-module config class re-parsing and
   re-validating them. Its only failure mode is a runtime exception that shows an operator
   message ("configure … privately in Aspire") to an end user.
2. **The user's accounts** (brain-scoped): integration-account neurons in `IntegrationsModule`, credentials
   in the Secrets vault as `SecretRef`s, probe/status/signals. This layer is right; only its
   name and surroundings are wrong.
3. **Capability selection** (a choice, not a credential): which model the assistant uses.
   Half compile-time AppHost fluent config, half settings; whether a choice works depends
   invisibly on layer 1.

The unmodeled layers leak through the modeled one: connecting Gmail can crash on missing
deployment config; picking DeepSeek fails inexplicably without a provider key. An ambient config
blob that gates user-visible behavior is a hidden fifth concept.

## Decision

An **Integration** is a catalog entry a module ships, with three explicit layers:

- **Registration** (layer 1) — a **neuron**, deployment-scoped, secrets in the vault, status
  observable, seedable from config.
- **Accounts** (layer 2) — today's connectors, renamed. 0..N per brain, vault-backed, probed,
  signal-emitting.
- **Capabilities** (layer 3) — what registration + accounts expose (mailboxes, models, repos).
  Settings select capabilities by reference; availability is derivable, failures explainable.

Alongside, module options become **typed end-to-end**: the attribute/contract/flattener chain is
replaced by a generic interface and one JSON round-trip.

## The contracts

### Module options

```csharp
public interface IModule
{
    void Configure(ISiloBuilder silo);
    void Configure(IEndpointRouteBuilder endpoints) { }
}

public interface IModule<TOptions> : IModule where TOptions : class, IModuleOptions, new();

public interface IModuleOptions
{
    void Validate();   // one place; runs at AppHost compile AND host startup
}
```

- Hosting resolves by convention: assembly `<ModuleAssembly>.Aspire.Hosting`, type `<ModuleTypeName>Hosting`
  (replaces `[ModuleHosting]`); a missing assembly means no hosting, a loaded assembly without the named type fails loudly.
- Kernel compiles once, generically: `Compile<TModule,TOptions>(options)` → `options.Validate()`
  → `JsonSerializer.Serialize(options)` stored under one config key
  (`DigitalBrain:Modules:{Name}:Options`) to cross the AppHost→host boundary. Host side:
  `configuration.GetModuleOptions<TOptions>()` deserializes and validates.
- Deleted: `ModuleConfigurationContract<,>` and every per-module contract class + string key
  list; every `Define(options)` flattener; `[ModuleConfiguration]`; `[ModuleHosting]`;
  per-module `*ModuleConfiguration` extension classes and `ConfigureOptions(..., "tracking.key")`
  strings.
- Options hold **structural/topology knobs only** (endpoints, hosting flags, source roots).
  Secrets never appear in options.
- Tests: `UnitTest.Create().WithModule<GmailModule, GmailModuleOptions>(o => o.TokenEndpoint = stub)`
  replaces the per-module `With*` sugar. AppHost keeps a fluent surface only where it reads
  better than a lambda; sugar is optional, contracts are not.

### Integration definition (shipped by the module)

```csharp
public sealed class GmailModule : IModule<GmailModuleOptions>
{
    public static IntegrationDefinition Integration { get; } = IntegrationDefinition
        .For("gmail", "Gmail")
        .RequiresSecret("ClientId")
        .RequiresSecret("ClientSecret")
        .RequiresSetting("PublicOrigin");   // operator-visible, not secret
}
```

The definition is the registration's schema and the catalog's row. Modules without a
registration ship none (`Integration` absent/null). One module may ship several definitions
(AI: one per provider).

### Registration neuron

```csharp
[Alias("integration.registration"), DefaultGrainType("integration.registration")]
public interface IIntegrationRegistration : INeuron   // keyed "integration/{id}", deployment scope
{
    [ReadOnly] Task<RegistrationSnapshot> Read();     // status + missing field NAMES, never values
    Task<RegistrationSnapshot> Configure(ConfigureRegistration request); // values → vault; record keeps SecretRefs
    Task<RegistrationSnapshot> Clear(string field);
    Task<ReleasedRegistration> Release(CallerContext caller); // values, only at the point of use (like the connector probe)
}

[GenerateSerializer, Alias("integration.registration-snapshot")]
public sealed record RegistrationSnapshot
{
    [Id(0)] public required string IntegrationId { get; init; }
    [Id(1)] public required RegistrationStatus Status { get; init; }  // Unconfigured | Partial | Ready
    [Id(2)] public required string[] MissingFields { get; init; }
}

[GenerateSerializer, Alias("integration.registered")]
public sealed record RegistrationChanged(string IntegrationId, RegistrationStatus Status) : Signal;
```

- **Scope: deployment-global** (`integration/gmail`). The key shape leaves room for brain-scoped
  registrations later (`{brainId}/integration/gmail`); do not build that now.
- **Seeding**: a startup task writes config values (`DigitalBrain:Integrations:{id}:{Field}` from
  env/appsettings/Aspire parameters) into an *unconfigured* registration, then config is out of
  the loop; UI edits win thereafter. Operator ergonomics and test setup keep working.
- **Consumption**: modules resolve the registration at the point of use.
  `GmailOAuthConfiguration`, `RequireConfigured`, and the "configure in Aspire" exception path
  are deleted; the OAuth callback calls `Release`. `ApiKeyProviderFactory.RequireApiKey` reads
  the provider's registration the same way.
- **Catalog**: `GET /integrations` reports every definition with its registration status
  (`Ready` / `Unconfigured` + missing field names). Nothing user-facing throws for a missing
  deployment credential again; unavailability is data.
- Registration edit rights: deployment owner/operator — gated by the existing enforcement
  stages (grants), not a new mechanism.

### Accounts (rename of connectors)

- `IConnectors` → `IIntegrationAccounts`; `ConnectorRecord` → `IntegrationAccount` (C# names
  only — Orleans aliases and `[Id(n)]` slots stay for persisted state, same discipline as the
  brain rename); `ConnectorStatusChanged`/`ConnectorDisconnected` →
  `AccountStatusChanged`/`AccountDisconnected` (new aliases only if the signals are not
  persisted; verify before renaming aliases).
- Routes `/brains/{brainId}/connections` → `/brains/{brainId}/integrations/accounts` (Dart
  mirrored in the same change).
- The record gains `IntegrationId` linking it to its definition (today's `Source` string,
  formalized).
- Follow-up affinity: this is the moment to move the accounts endpoints out of the Salesforce
  module into a neutral Integrations module (the parked finding from the brain refactor).

### Capabilities (layer 3, minimal v1)

- A capability is `(integrationId, kind, id, display)` — e.g. `("deepseek", "llm", "deepseek-r1", …)`,
  `("gmail", "mailbox", "alice@gmail.com", …)` — derived from registration status plus the
  caller's accounts; never stored, always re-derived (repo rule: derived state is re-derived
  wholesale).
- Settings reference capabilities (`assistant.model = deepseek/deepseek-r1`); resolution failure
  yields an explainable answer ("needs the DeepSeek registration; missing: ApiKey").
- AI providers become integration definitions; `AIOptions` keeps hosting topology and defaults
  only. BYO-key later = an account on the provider integration; no new machinery.

## What stays

- Secrets vault (`SecretsModule`) — both layers store there; only the reference owners differ.
- The kernel enforcement pipeline, `BrainRoutes.Group`, brain scoping.
- Edge neurons (`IGmail`), signals, watch webhooks — transport unchanged.
- `AIOptions` hosting matrix (which models to host, defaults) as plain module options.

## Testing

- Registration facts: configure → Ready + `RegistrationChanged`; partial → missing names, never
  values; `Release` only for authorized callers; seed-then-edit precedence.
- Options facts: `Validate()` rejection at compile and at startup; JSON round-trip fidelity for
  every module's options type (one generic theory over all `IModule<TOptions>` in the
  composition).
- Catalog facts: unconfigured integration reported, not thrown; Gmail connect against an
  unconfigured registration → clean 409/422 with the integration status, not a 500.
- Rename facts: mechanical, existing account/connector suites keep passing.
- Bar: affected unit suites + `aspire run` Healthy; skip the 18-minute E2E.

## Sequencing

1. Kernel: `IModule<TOptions>`/`IModuleOptions`, generic compile/bind, migrate all modules off
   contracts/attributes/flatteners (mechanical per module).
2. Integration definitions + registration neuron + seeding + catalog endpoint.
3. Gmail onto registration (delete `GmailOAuthConfiguration` chain); then GitHub, Tavily, AI
   providers.
4. Connectors → accounts rename + route change + Dart mirror + neutral Integrations module home.
5. Capabilities v1: model selection resolved through the catalog.

## Out of scope

- Brain-scoped registrations (multi-tenant per-owner provider apps).
- BYO-key AI accounts (design ready, not built).
- Marketplace/package-visible integration permissions (open front: cross-app scopes).
