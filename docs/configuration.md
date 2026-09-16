# Module configuration

Each module owns its settings. IntoChat supplies application-specific values, the module registers binding and validation, and its runtime services consume typed options. Capability contracts do not expose deployment settings.

```text
src/Modules/<Module>/
  Contracts/                         capability interfaces and messages
  <Module>/Configuration/            runtime options and configuration adapters
  Aspire.Hosting/Configuration/      graph settings and hosting configuration
src/Modules/DigitalBrain/
  DigitalBrain/Configuration/        framework runtime options, module manifest
  Aspire/Configuration/              runtime integration settings
  Aspire.Hosting/Configuration/      framework hosting options
src/Applications/IntoChat/
  IntoChat/Configuration/            product settings
  ServiceDefaults/Configuration/     telemetry configuration
```

Excel currently has no configurable settings; it does not need an empty options class or hosting project. Flutter's .NET runtime needs no independent runtime options: its host settings live in Aspire.Hosting, while the image-tool availability check intentionally reads the AI composition key without referencing AI's implementation package.

## Ownership

| Owner | Runtime configuration | Hosting configuration |
| --- | --- | --- |
| DigitalBrain | NeuronOptions and module-manifest adapter | DigitalBrainHostingOptions |
| AI | AIOptions, AIWorkspaceOptions, nested provider/default/telemetry/search settings | AI hosting state preserves model-marker fluent registration and deferred secrets |
| Memory | MemoryOptions, QdrantMemoryOptions | QdrantHostingOptions |
| ClickHouse | ClickHouseOptions | ClickHouseHostingOptions |
| Supabase | SupabaseOptions | SupabaseHostingOptions |
| Coding | CodingOptions, CodingToolOptions | CodingHostingOptions |
| Time | TimeOptions | No hosting adapter needed |
| Google | GmailOAuthOptions | GmailHostingOptions |
| Salesforce | SalesforceOAuthOptions, SalesforceMcpOptions | SalesforceHostingOptions |
| Microsoft | AspireOptions, GitHubAppOptions, repository options | AspireHostingOptions, GitHubAppHostingOptions, GitHubRepositoryHostingOptions |
| Flutter | Existing capability/composition checks | FlutterHostOptions, legacy toolchain adapter |
| IntoChat | Graph, auth, CORS, storage, session-stream options | Application composition selects module hosts |

Existing configuration section names, environment keys, public namespaces, and defaults are retained. Configuration-free modules stay configuration-free. `TimeOptions`, `NeuronOptions`, and `CodingToolOptions` retain direct singleton aliases for existing host overrides, backed by the options pipeline by default.

Session stream polling additionally exposes `IntoChat:SessionStream:PollInterval` (a TimeSpan, default `00:00:00.100`); it must be positive.

## Setup and consumption

The AppHost constructs resources with eager hosting settings:

```csharp
.AddModule<MemoryModule>(module => module.WithQdrant(options =>
{
    options.CollectionName = "intochat";
}))
.AddModule<ClickHouseModule>(module =>
    module.WithClickHouse(options => options.WithSeed("leads")))
.AddModule<CodingModule>(module => module.WithSolution(options =>
{
    options.SolutionPath = solutionPath;
    options.WorkspaceKey = "intochat";
}))
```

Module projections pass explicit environment values and connection references to the runtime process. Registering options in AppHost does not register them in IntoChat. Aspire parameters and endpoints remain deferred resource references; secrets are not converted into ordinary hosting strings.

Runtime modules register their settings with the standard .NET options pipeline:

```csharp
services.AddOptions<CodingOptions>()
    .BindConfiguration(CodingOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.WorkspaceKey),
        "Coding workspace key must not be empty.")
    .ValidateOnStart();
```

Services receive `IOptions<CodingOptions>` and read `options.Value`. Applications can apply runtime overrides after module registration:

```csharp
builder.Services.Configure<CodingOptions>(options =>
    options.WorkspaceKey = "intochat");
```

For ordinary runtime settings, precedence is property defaults, bound configuration, then later `Configure<T>` callbacks. Derived connection strings are resolved in `PostConfigure` after connection-name overrides. Validation sees the resulting values. Hosts should register their overrides after the module's registration to make this order explicit.

Hosting settings are applied before resource creation. The existing callback APIs remain usable without JSON binding; Flutter and framework hosting also bind their documented `DigitalBrain:Flutter:Hosting` and `DigitalBrain:Hosting` sections before the callback.

## Startup and optional capabilities

Provider selection and capability/tool registration are startup composition decisions. Set them in the application's configuration/AppHost before modules register. A later runtime options callback does not add a provider, tool, container, or service. Memory and ClickHouse reject provider changes that disagree with their registered backend. Runtime options are startup snapshots; this migration does not introduce hot reload.

Validation preserves optional behavior: Coding may start without a solution; unconfigured OAuth integrations remain available for setup; enabled data modules require real providers and valid connection strings. Salesforce endpoint restrictions and existing provider/connection guards remain in place. Connection strings use the existing `ConnectionStrings` convention.

Framework keys such as Orleans configuration, OTEL environment variables, and module type manifests are intentional parsing boundaries. They are not replaced with a second configuration system.

The design follows [Microsoft Learn's options guidance for library authors](https://learn.microsoft.com/dotnet/core/extensions/options-library-authors). Use `IOptionsMonitor<T>` only when a service explicitly supports live updates and its configuration source can reload.
