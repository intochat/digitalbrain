# DigitalBrain Public Framework Design

**Date:** 2026-07-18
**Status:** Approved
**Supersedes:** The packaging, AI integration, and Aspire composition portions of `2026-07-18-durable-neuron-architecture-design.md` without weakening its durability, ownership, typing, or security constraints.

## Goal

DigitalBrain is a reusable .NET framework distributed through NuGet. A consumer must be able to add a durable DigitalBrain kernel and a typed client to an Aspire application with a small, conventional API, configure real OpenAI and Anthropic models in the AppHost, and verify the system through a package-only quickstart.

The repository must prove that DigitalBrain behaves as an external framework, not merely as a set of projects that happen to compile together.

## Non-negotiable outcomes

- Publishable public NuGet packages target `net8.0` and remain consumable by newer .NET applications.
- Aspire hosting and client integrations are separate packages.
- `DigitalBrainResource` is a real Aspire composite resource.
- Model roles are configured in the AppHost and resolve inside the privileged kernel to real `IChatClient` and `IEmbeddingGenerator` implementations.
- The kernel receives privileged storage and provider configuration.
- ordinary clients receive only Orleans client discovery through `brain.AsClient()`.
- The quickstart consumes locally packed NuGet packages, not project references.
- A console client can hold an interactive conversation with DigitalBrain.
- Development-only standalone Orleans Dashboard and Microsoft Agent Framework DevUI hosts can inspect and exercise the same running DigitalBrain.
- Production durability uses official Orleans journaling, reminders, streams, and Azure Storage integrations.
- Streams distribute notifications but never become the source of truth.

## Public package family

| Package | Responsibility |
| --- | --- |
| `DigitalBrain.Abstractions` | Grain contracts, durable operation contracts, notifications, identifiers, model-role contracts, and provider-neutral public abstractions. |
| `DigitalBrain.Client` | Typed application-facing DigitalBrain client, owner sessions, conversation proxies, and provider-neutral role facades over an Orleans client. |
| `DigitalBrain.Kernel` | Kernel grains, durable conversations, real provider adapters, durable state transitions, official journaling, reminders, streams, outbox recovery, and kernel service registration. |
| `DigitalBrain.Aspire` | Runtime client integration for `IHostApplicationBuilder`, including Orleans client wiring, owner-session creation, options validation, health checks, and telemetry. It has no provider SDK dependency. |
| `DigitalBrain.Aspire.Hosting` | AppHost integration containing `DigitalBrainResource`, model declarations, storage and Orleans composition, and restricted reference projections. |
| `DigitalBrain.DevTools` | Optional development-only adapters for a standalone Orleans Dashboard host and Microsoft Agent Framework DevUI host. |
| `DigitalBrain` | Optional convenience package referencing the common application-facing packages only. It must not pull AppHost or kernel dependencies into an ordinary application. |

Package IDs and namespaces use the `DigitalBrain` prefix. The repository should request a NuGet prefix reservation before the first stable release.

All public packages target `net8.0`. The current repository hosts and tests may remain `net11.0` and consume the `net8.0` packages. The package-consumer quickstart itself targets `net8.0`, proving the published minimum. This explicitly supersedes the earlier repository-local decision to keep every active project on `net11.0`; it does not require retargeting unrelated hosts or tests.

## Framework layout

The durable foundation currently under `kernel/Brain.*` becomes the public `DigitalBrain.*` package family. Renames must preserve behavior and happen under test. Old `src/**` and `sources/**` implementations are not compatibility authorities.

Recommended active layout:

```text
kernel/
  DigitalBrain.Abstractions/
  DigitalBrain.Client/
  DigitalBrain.Kernel/
integrations/
  DigitalBrain.Aspire/
  DigitalBrain.Aspire.Hosting/
  DigitalBrain.DevTools/
hosts/
  Brain.Kernel.Host/
  DigitalBrain.AppHost/
samples/
  DigitalBrain.Quickstart/
    DigitalBrain.Quickstart.AppHost/
    DigitalBrain.Quickstart.Kernel/
    DigitalBrain.Quickstart.Console/
    DigitalBrain.Quickstart.OrleansDashboard/
    DigitalBrain.Quickstart.DevUI/
tests/
  DigitalBrain.Tests/
  DigitalBrain.PackageTests/
```

## Consumer experience

### AppHost

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain("brain")
    .WithLLM<GptFast>().AsFast()
    .WithLLM<ClaudeBalanced>().AsBalanced()
    .WithLLM<GptReasoning>().AsReasoning()
    .WithEmbedding<TextEmbedding>();

builder.AddProject<Projects.DigitalBrain_Quickstart_Kernel>("kernel")
    .WithReference(brain);

builder.AddProject<Projects.DigitalBrain_Quickstart_Console>("console")
    .WithReference(brain.AsClient());

if (builder.Environment.IsDevelopment())
{
    builder.AddProject<Projects.DigitalBrain_Quickstart_OrleansDashboard>("orleans-dashboard")
        .WithReference(brain.AsClient());

    builder.AddProject<Projects.DigitalBrain_Quickstart_DevUI>("ai-devui")
        .WithReference(brain.AsClient());
}

builder.Build().Run();
```

The sample may expose explicit configuration switches for the development tools, but they default on only in Development.

### Kernel host

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDigitalBrainKernel("brain");

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
```

`AddDigitalBrainKernel` consumes the privileged reference and fails startup with a useful error when required storage, Orleans, or model-role configuration is missing.

### Console client

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.AddDigitalBrainClient("brain");

using var host = builder.Build();
await host.StartAsync();

var sessions = host.Services.GetRequiredService<DigitalBrainSessionFactory>();
await using var session = sessions.Create(new BrainOwnerId("quickstart-user"));
var brain = session.Client;
```

The Development sample obtains its owner from an explicit `digitalbrain-owner` parameter, defaulting to `quickstart-user`. Production applications create owner sessions only after their authentication boundary has produced a validated `BrainOwnerId`.

The sample interactive shell supports:

- plain text to send a turn;
- `/role fast`, `/role balanced`, and `/role reasoning`;
- `/new` to start another durable conversation;
- `/conversation` to print the current durable identity;
- `/help`;
- `/exit`.

It prints streamed display output when available, final output, the selected role, and the durable conversation identifier. The console never talks directly to OpenAI or Anthropic.

## Typed durable conversation surface

The console and DevUI exercise an approved neuron API rather than inventing a client-side chat shortcut.

Public contracts include:

- `ConversationId`, a validated stable identifier;
- `ConversationRole`, limited to the configured fast, balanced, and reasoning roles;
- `ConversationTurnId`, used as the durable idempotency key;
- `ConversationTurnRequest`, containing typed text input, role, and turn identity;
- `ConversationTurnResult`, containing the committed final response and revision;
- `ConversationSnapshot`, containing committed turns and current revision;
- `IConversationNeuron : INeuron`, exposing `SubmitTurnAsync` and `ReadAsync`.

`DigitalBrainClient.Conversations.Open(ConversationId)` derives a grain identity from both the authenticated owner and conversation identifier. The client never accepts an owner component from the caller.

The canonical key format is `v1.<base64url(UTF8 owner.Value)>.<base64url(UTF8 conversationId.Value)>`. `ConversationId` is validated before encoding, base64url segments contain no delimiter, parsing must consume exactly three segments, and decoded values are revalidated. This prevents delimiter injection and owner-prefix forgery.

Conversation grains are the explicit typed exception to the V1 one-leaf-instance-per-owner rule: provider leaves retain an exact owner-only key, while `IConversationNeuron` permits multiple owner-scoped instances. An internal typed marker on the conversation grain selects composite-key authorization in `BrainOwnerIncomingCallFilter`. For marked conversation grains the filter parses the canonical key and compares the decoded owner with the typed request owner; every other neuron retains exact `owner.Value == primaryKey` authorization. There is no heuristic prefix matching or Kind/string routing.

Conversation notification stream identifiers use the complete canonical conversation key as an opaque identifier. The kernel verifies that the incoming owner call context matches the decoded owner. An owner cannot open, read, subscribe to, or submit a turn to another owner's conversation.

`SubmitTurnAsync` is the only initial conversational mutation. It is not named `Ask`, does not accept generic JSON, and does not expose provider operations. The conversation neuron chooses the configured typed role inside the kernel. Client-side fast, balanced, and reasoning types are non-grain convenience facades that only set `ConversationRole` before calling the same conversation client; they are not alternate AI entry points.

The conversation state journal records turn identity, idempotency key, validated input, requested role, operation status, committed final response, revision, and notification delivery state.

Display-token streams are optional ephemeral progress. They are never journal authority. A missed stream is repaired by the committed `ConversationTurnResult` or `ReadAsync` snapshot. Final turn notification delivery follows the durable outbox/reminder rules.

Crash recovery replays the journal, identifies incomplete turns through the durable operation ledger, and resumes according to the approved external-operation policy. Provider retry behavior and provider request-id support are adapter-specific, but the public result is committed at most once per `ConversationTurnId`.

## `DigitalBrainResource`

`DigitalBrainResource` is an Aspire composite resource containing or owning references to:

- an official Orleans service;
- official Azure Storage resources for clustering, grain state, journaling, reminders, and any durable outbox state;
- a stream provider used only for delivery;
- declared chat model roles;
- a declared embedding model;
- secret parameters and provider endpoints;
- health dependencies and publish-manifest metadata.

It does not hardcode a generated consumer `Projects.*` type. The consumer adds its kernel project and references the resource.

Run mode uses Azurite through official Aspire Azure Storage resources. Orleans clustering, reminder storage, grain storage, and journal blobs are explicitly separate logical resources; the journal blob is never reused as ordinary grain storage. The resource configures official `WithClustering`, `WithReminders`, and `WithStreaming` relationships. Production kernel hosts do not call `UseLocalhostClustering`, do not register in-memory reminders, and do not select volatile journal storage.

### Reference projections

Two projections are required:

1. `WithReference(brain)` is privileged. It is intended only for a DigitalBrain kernel host and propagates Orleans silo configuration, official durable storage configuration, model-role configuration, and required secrets.
2. `WithReference(brain.AsClient())` is restricted. It propagates only the Orleans client connection and non-secret client metadata. Client applications receive Orleans-proxied role and conversation facades; they never construct provider SDK clients.

Tests must inspect generated environment variables and connection properties to prove that provider keys, journal storage, reminder storage, and kernel-only values are absent from the client projection.

## Model declarations and runtime binding

The public role API remains strongly typed:

```csharp
.WithLLM<GptFast>().AsFast()
.WithLLM<ClaudeBalanced>().AsBalanced()
.WithLLM<GptReasoning>().AsReasoning()
.WithEmbedding<TextEmbedding>()
```

Model descriptor types declare provider, model identifier, endpoint policy, and supported capability. They do not contain credentials and do not copy provider APIs.

The hosting integration uses stable official Aspire OpenAI hosting resources for OpenAI-compatible chat and embedding models. Anthropic resources are modeled by DigitalBrain as a small Aspire connection resource because Aspire does not currently ship an official Anthropic hosting integration.

At runtime:

- OpenAI roles use the official OpenAI client through `Microsoft.Extensions.AI.OpenAI`.
- Claude roles use Anthropic's official C# SDK and its `AsIChatClient` adapter.
- Embeddings use `IEmbeddingGenerator`.
- role-specific provider wrappers resolve only inside the privileged kernel without keyed provider DI.
- `DigitalBrain.Aspire` registers Orleans-proxied conversation and role facades and has no OpenAI or Anthropic SDK dependency.
- invalid or missing role configuration fails during startup validation.
- no production `IChatClient` implementation may unconditionally throw or return canned output.

Provider SDK churn is isolated inside internal adapters. Anthropic's current beta status must be documented and its version pinned.

## Durability and recovery

The existing approved neuron durability rules remain in force:

- official Orleans journaling is the mutation authority;
- journaled state contains the durable operation ledger;
- reminders recover incomplete external operations and undelivered notifications;
- streams distribute committed notifications;
- stream delivery never proves that a mutation committed;
- idempotency keys prevent duplicate external side effects;
- notification delivery state survives restart;
- production startup must never fall back to volatile storage.

## Development tools

### Standalone Orleans Dashboard

`DigitalBrain.DevTools` supplies the minimal registration and endpoint helpers needed by a separate ASP.NET Core project using `Microsoft.Orleans.Dashboard`.

The dashboard process:

- consumes `brain.AsClient()`;
- joins as an Orleans client, never as a silo;
- maps the official dashboard endpoints;
- is development-only by default;
- has no journal or provider credentials;
- is covered by a startup and endpoint smoke test.

### Microsoft Agent Framework DevUI

`DigitalBrain.DevTools` adapts DigitalBrain's Orleans-proxied conversation clients to named Microsoft Agent Framework agents exposed through `Microsoft.Agents.AI.DevUI`.

The DevUI process:

- consumes `brain.AsClient()`;
- obtains the same explicit Development-only `digitalbrain-owner` parameter as the console and creates a `DigitalBrainSessionFactory` owner session before agent discovery;
- exposes fast, balanced, and reasoning agents backed by DigitalBrain;
- does not call provider SDKs directly;
- is development-only by default;
- uses explicit local access controls and does not bind publicly by default;
- is treated as a preview dependency and isolated from production packages;
- is covered by discovery, startup, and one-turn smoke tests.

DevUI is a testing surface, not the DigitalBrain programming model.

## Quickstart and package-consumer proof

The quickstart is a package-only consumer. It restores from a repository-local NuGet feed created by the pack script and must not contain `ProjectReference` entries to framework projects.

Required experience:

```powershell
.\eng\pack.ps1
aspire run --apphost .\samples\DigitalBrain.Quickstart\DigitalBrain.Quickstart.AppHost\DigitalBrain.Quickstart.AppHost.csproj
```

The Aspire dashboard prompts for required secret parameters. A developer selects the console resource to chat, the Orleans Dashboard to inspect the cluster, and DevUI to test role-backed agents.

The quickstart README includes:

- prerequisites;
- the two commands above;
- how to provide OpenAI and Anthropic credentials;
- how to select models;
- how to disable either development tool;
- the console commands;
- an architecture diagram;
- production warnings.

An automated package-consumer test packs the framework, restores the sample from only the local feed plus nuget.org, builds it, starts the AppHost, waits for resources, performs a durable console/API turn against a controlled test provider, restarts the kernel, and verifies recovery.

The controlled provider is a test-only HTTP resource in `DigitalBrain.PackageTests`. The AppHost supplies explicit provider endpoint overrides and synthetic secret parameters to the normal OpenAI and Anthropic resources. The privileged kernel receives those endpoints and the real official SDK adapters make HTTP requests to the controlled server. No fake `IChatClient`, canned production implementation, ambient provider-key lookup, or client-side provider path is introduced.

## Deliberate dependency line

The framework uses one reviewed Orleans release line:

| Dependency | Version | Reason |
| --- | --- | --- |
| Aspire SDK and hosting packages | `13.4.6` | Current stable Aspire line. |
| Orleans Core, Client, Server, Runtime, SDK | `10.2.2-rc.2` | Required by the selected official journaling packages. |
| `Microsoft.Orleans.Journaling` | `10.2.2-rc.2.alpha.1` | Official journaling remains prerelease. |
| `Microsoft.Orleans.Journaling.AzureStorage` | `10.2.2-rc.2.alpha.1` | Official Azure journal provider matching the journaling line. |
| `Microsoft.Orleans.Dashboard` | `10.2.2-rc.1` | Latest dashboard in the same `10.2.2` release line; compatibility with the rc.2 client is a mandatory live gate. |
| Microsoft.Extensions.AI | `10.8.0` | Stable common AI abstractions and middleware. |
| Anthropic official C# SDK | `12.36.0` | Official SDK with `IChatClient`; beta status and its `>= 10.5.1` MEAI dependency are documented. |
| Microsoft Agent Framework DevUI | `1.13.0-preview.260703.1` | Development-only preview isolated to `DigitalBrain.DevTools`. |

No package is described as stable when its selected version is prerelease. A newer or aligned release may replace a pin only after official metadata and compatibility tests prove it.

## NuGet quality bar

Every public package includes:

- package ID, title, description, authors, tags, project URL, repository URL and commit;
- SPDX license expression;
- embedded README and icon;
- XML documentation;
- deterministic builds;
- SourceLink;
- `.snupkg` symbols;
- API compatibility baseline;
- package vulnerability and deprecated-package checks;
- license and dependency review.

Release flow:

1. `dotnet pack -c Release`;
2. inspect `.nupkg` and `.snupkg` contents;
3. install into the package-only quickstart from a clean local feed;
4. run all package-consumer and Aspire smoke tests;
5. publish a prerelease to the NuGet test service;
6. verify metadata and installation;
7. publish to nuget.org only with an explicit credential and operator approval.

The first public version is a prerelease such as `0.1.0-alpha.1`.

## Forbidden shortcuts

The implementation must not introduce:

- Kind routing;
- `DispatchProxy`;
- generic JSON invocation;
- keyed provider DI;
- copied provider operations;
- `Ask`;
- `InvokeMcpTool`;
- a custom journal provider;
- volatile production durability;
- streams as truth;
- a production fake AI client;
- a client-side provider SDK or ambient provider-key lookup;
- provider credentials in `brain.AsClient()`;
- sample `ProjectReference` shortcuts;
- direct provider calls from the console or DevUI;
- a monolithic package that drags AppHost dependencies into ordinary consumers.

## Verification

Completion requires:

- all unit, architecture, package, and integration tests green;
- `git diff --check`;
- clean Release builds;
- clean package restore from an empty NuGet cache;
- Aspire manifest inspection;
- Aspire resource state green;
- standalone Orleans Dashboard reachable;
- DevUI discovers all configured roles and completes a controlled turn;
- interactive console completes a turn;
- kernel restart proves durable recovery;
- secret-projection tests prove client isolation;
- Grok read-only architecture and code reviews have no unresolved blocking findings.
