# UI Kit — Chart & Image in Chat Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** "Show me a chart" renders a live ChartEntity card inside the chat, and asking the assistant whether it can generate an image gets an honest toolset-based answer followed by a generated ImageEntity rendered in chat.

**Architecture:** Entity-backed reference cards (ratified in docs/ARCHITECTURE.md): an AI tool call creates/updates an `Entity<TState>`, then posts a `KitCardOffer` synapse to the chat; the chat journals it into a `Responded`, the SSE projector emits a `cards` array on the chat-turn event, and Flutter mounts the matching kit widget which reads the entity state over new `/kit/*` HTTP endpoints. Tools reach the assistant through a new `IAgentToolSource` seam (UI module implements, AI module consumes) because AI cannot reference UI (would be circular). Capability honesty falls out of tool visibility: models whose descriptor says `SupportsTools == false` (local Gemma) never see the tools and honestly answer "no"; tool-capable cloud models see them and act.

**Tech Stack:** .NET 11 / Orleans 10 (grains, `Entity<TState>`), Microsoft.Extensions.AI 10.9 (`AIFunctionFactory`, `FunctionInvokingChatClient` already in every pipeline), OpenAI .NET 2.12 (`GetImageClient("gpt-image-1").GenerateImageAsync`), Azure Blob Storage (existing keyed `BlobServiceClient` named `grainstate`), Flutter (`digitalbrain_flutter` core + `digitalbrain_ui_kit` widgets — `KitChart` already exists).

**Spec:** `docs/ARCHITECTURE.md` (sections "Core loop (chat + dynamic UI)" and "UI kit — 13 components"). This plan delivers the kit template plus the first two components (Chart, Image) end-to-end; the other 11 components repeat the same template later.

## Global Constraints

- `TreatWarningsAsErrors` + `AnalysisLevel preview-all` are on repo-wide: every warning is a build break.
- Tests must stay green offline: testing mode (`DigitalBrainNames.TestingMode`) must never require provider keys, network, or Azurite.
- User CLAUDE.md rules: no meaningless `/// <summary>` docs; inline comments only for non-obvious constraints; self-explanatory names.
- One deterministic verification loop per task: `dotnet build DigitalBrain.slnx` then `dotnet test DigitalBrain.slnx --no-build`; Flutter tasks add `flutter analyze` + `flutter test` in the touched package dir.
- Commit after every green task (`git commit` on branch `finalv2`; end commit messages with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`).
- JSON on the HTTP surface is camelCase (ASP.NET Core defaults, matching the existing `chat-turn` SSE payload).

## Existing seams you will build on (read these files first in every task that touches them)

| Seam | File | Fact |
|---|---|---|
| Card precedent | `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/Note.cs` + `Chat.HandleAsync(Note)` in `src/Modules/UI/DigitalBrain.Modules.UI/Chat/Chat.cs:~183` | A synapse handled by `IChat` that `Remember`s a turn and `EmitAsync`es a `Responded` — cards copy this exact flow |
| Turn wire | `src/Kernel/DigitalBrain.Kernel/MapChatStreams.cs` `ProjectTurn` | `Responded`/`UserMessaged`/`TurnLifecycle` → `ChatTurnEvent` SSE (`event: chat-turn`) |
| Dormant Flutter parsing | `src/Modules/UI/Flutter/core/lib/src/models/chat_models.dart` `ChatTurnEvent.fromJson` | Already tolerantly parses optional arrays (`buttons`/`charts`/`timers`); `cards` is added the same way |
| Kit widgets | `src/Modules/UI/Flutter/kit/lib/src/components/chart/kit_chart.dart`, `models/kit_part.dart` | `KitChart` renders `KitChartPart(title, points(label,value), chartKind)`; `KitPart.tryParse` dispatches by `kind` |
| Entity citizen | `src/Kernel/DigitalBrain.Core/Entities/Entity.cs`, `src/Kernel/DigitalBrain.Abstractions/Entities/IEntity.cs`, `src/Modules/UI/DigitalBrain.Modules.UI/Surface/SurfaceEntity.cs` | `Entity<TState>` + `IEntity<TState>.Read()`; leaf class must redeclare `[PersistentState("state", DigitalBrainNames.DefaultGrainStorage)]` |
| Grain-proxy HTTP | `src/Kernel/DigitalBrain.Kernel/MapOwnerCommands.cs` | `brain.GetGrainProxy<IChat>(instance)` + `PrincipalScoped.InstanceName(principal, localName)` scoping |
| Blob client | `src/Aspire/DigitalBrain.Aspire/DigitalBrainRuntimeHostingExtensions.cs:28` | `AddKeyedAzureBlobServiceClient(DigitalBrainNames.GrainState)` — keyed `BlobServiceClient` exists in the silo |
| Tool wrapper | `src/Modules/AI/AI/Tools/TurnBoundFunction.cs` | Wraps an `AIFunction` to invoke on a captured `TaskScheduler` (grain-safe tool execution) |
| Tool creation precedent | master history: `git show master:src/Modules/AI/AI/Assistant.cs` and `master:src/Modules/AI/AI/Capabilities/SystemTools.cs` | `AIFunctionFactory.Create(method, name, description)` with `[Description]` parameters |
| Model catalog | `src/Modules/AI/Contracts/LLM/LLMModel.cs` | `LLMModel` descriptors; pipeline built in `src/Modules/AI/AI/Clients/AIClients.cs` `BuildChatPipeline` (already has `UseFunctionInvocation`) |
| Testing chat | `src/Modules/AI/AI/Testing/TestChatClient.cs` + `AITestingClients.cs` | Deterministic client every marker resolves to in testing mode |

---

### Task 1: KitCardOffer synapse → Chat → SSE `cards` array

**Files:**
- Create: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/KitCardOffer.cs`
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/Responded.cs`
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/IChat.cs` (add `IHandle<KitCardOffer>`)
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/Chat/Chat.cs` (handler next to the `Note` handler)
- Modify: `src/Kernel/DigitalBrain.Kernel/MapChatStreams.cs` (`ChatTurnEvent` record + `ProjectTurn`)
- Test: `tests/DigitalBrain.Simulation.Tests/ChatTurnTests.cs` (extend)

**Interfaces:**
- Consumes: `Note` handler shape in `Chat.cs`; `Responded` record; `ChatTurnEvent` record in `MapChatStreams.cs` (find its declaration in the same file or `HttpSurfacePaths.cs` neighborhood — search `record ChatTurnEvent`).
- Produces: `KitCardOffer(string Kind, string Name, string Caption)` with `KitCardKinds.Chart = "chart"`, `KitCardKinds.Image = "image"`; `Responded.Cards` (`KitCardOffer[]? Cards = null`, `[property: Id(4)]`, keep `Author` at `Id(3)`? **No** — `Author` is currently `Id(3)`; append `Cards` as `Id(4)` to avoid re-numbering committed serializer ids); SSE JSON gains `cards: [{kind, name, caption}]`.

- [ ] **Step 1: Write the failing test** (extend `ChatTurnTests.cs` — copy its existing fixture usage; it already boots a simulation host with a chat):

```csharp
[Fact]
public async Task KitCardOfferLandsInTheChatJournalAsARespondedCard()
{
    var chat = Fixture.GrainFactory.GetGrain<IChat>(ChatInstance);          // reuse the file's existing helpers
    await chat.HandleAsync(new KitCardOffer(KitCardKinds.Chart, "chart-abc12345", "Quarterly sales"), CancellationToken.None);

    var transcript = await chat.Read();
    Assert.Contains(transcript.Turns, turn => !turn.FromUser && turn.Text == "Quarterly sales");
    // The Responded card itself is asserted at the journal projection level in Step 6.
}
```

Adapt naming (`Fixture`, `ChatInstance`) to what `ChatTurnTests.cs` actually uses — read the file first; it already sends messages to a chat grain.

- [ ] **Step 2: Run it** — `dotnet test tests/DigitalBrain.Simulation.Tests --no-build` after a build; expected: FAIL (no `KitCardOffer`, no handler).

- [ ] **Step 3: Implement the contract**

```csharp
// Contracts/Chat/Synapses/KitCardOffer.cs
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Messaging;

namespace DigitalBrain.Chat;

public static class KitCardKinds
{
    public const string Chart = "chart";
    public const string Image = "image";
}

// A reference card: state lives in the named kit entity, never in the message.
[GenerateSerializer]
[Alias("ui.kit-card")]
public sealed record KitCardOffer(
    [property: Id(0)] string Kind,
    [property: Id(1)] string Name,
    [property: Id(2)] string Caption) : Synapse;
```

`Responded` gains `[property: Id(4)] KitCardOffer[]? Cards = null` as the LAST constructor parameter. `IChat` adds `IHandle<KitCardOffer>` to its interface list.

- [ ] **Step 4: Implement the Chat handler** — mirror the `Note` handler exactly (same guards, same `Remember` + `EmitAsync` pattern):

```csharp
public async Task HandleAsync(KitCardOffer synapse, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(synapse);
    cancellationToken.ThrowIfCancellationRequested();
    if (string.IsNullOrWhiteSpace(synapse.Kind) || string.IsNullOrWhiteSpace(synapse.Name))
    {
        throw new NeuronAuthorizationException($"Chat '{Id}' refuses an incomplete kit card.");
    }

    Remember(new ChatTurn(FromUser: false, synapse.Caption));
    await EmitAsync(new Responded(CommandId.New(), Id, synapse.Caption, Author: Id.Name, Cards: [synapse]))
        .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
}
```

Match the exception type/wording the `Note` handler actually uses.

- [ ] **Step 5: Project cards onto the SSE event** — in `MapChatStreams.cs`, add a `KitCardOffer[]? cards = null` parameter to the local `Turn(...)` helper and a `Cards` (serialized `cards`) property on the `ChatTurnEvent` record; the `Responded` arm passes `responded.Cards`. Keep the property type as the contract record — System.Text.Json serializes it camelCase (`kind`/`name`/`caption`).

- [ ] **Step 6: Run the suite** — `dotnet build DigitalBrain.slnx && dotnet test DigitalBrain.slnx --no-build`; expected: all green including the new fact.

- [ ] **Step 7: Commit** — `git add -A && git commit -m "feat: kit reference cards flow through chat journal to the turn stream"`

---

### Task 2: ChartEntity + IChart contract

**Files:**
- Create: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chart/ChartState.cs`
- Create: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chart/IChart.cs`
- Create: `src/Modules/UI/DigitalBrain.Modules.UI/Chart/ChartEntity.cs`
- Test: `tests/DigitalBrain.Simulation.Tests/EntityTests.cs` (extend — it already exercises `SurfaceEntity`-style entities)

**Interfaces:**
- Consumes: `Entity<TState>` base, `IEntity<TState>`, `DigitalBrainNames.DefaultGrainStorage`.
- Produces: `ChartState(string Title, string ChartKind, IReadOnlyList<ChartPoint> Points)`; `ChartPoint(string Label, double Value)`; `IChart : IEntity<ChartState>` with `Task Render(ChartState state)`; grain type `"chart"`.

- [ ] **Step 1: Failing test** (extend `EntityTests.cs`, reuse its host fixture):

```csharp
[Fact]
public async Task ChartEntityRendersAndReadsItsState()
{
    var chart = Fixture.GrainFactory.GetGrain<IChart>("test/chart-1");
    var state = new ChartState("Sales", "bar", [new ChartPoint("Q1", 10), new ChartPoint("Q2", 20)]);

    await chart.Render(state);
    var read = await chart.Read();

    Assert.NotNull(read);
    Assert.Equal("Sales", read.Title);
    Assert.Equal(2, read.Points.Count);
}
```

- [ ] **Step 2: Run to verify it fails** (missing types).

- [ ] **Step 3: Implement** — contracts mirror `SurfaceState`/`ISurface` exactly:

```csharp
// Contracts/Chart/ChartState.cs
namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.chart-state")]
public sealed record ChartState(
    [property: Id(0)] string Title,
    [property: Id(1)] string ChartKind,
    [property: Id(2)] IReadOnlyList<ChartPoint> Points);

[GenerateSerializer]
[Alias("ui.chart-point")]
public sealed record ChartPoint(
    [property: Id(0)] string Label,
    [property: Id(1)] double Value);
```

```csharp
// Contracts/Chart/IChart.cs
using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.UI;

// Same wall as ISurface: Read() is the client-facing query via IEntity<TState>;
// Render stays a same-silo grain call (kit tools drive it).
[Alias("ui.chart")]
public interface IChart : IEntity<ChartState>
{
    [Alias(nameof(Render))]
    Task Render(ChartState state);
}
```

```csharp
// UI/Chart/ChartEntity.cs
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType("chart")]
internal sealed class ChartEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ChartState> state)
    : Entity<ChartState>(state), IChart
{
    public async Task Render(ChartState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        await SaveAsync(state);
    }
}
```

- [ ] **Step 4: Run the suite; expected green.**
- [ ] **Step 5: Commit** — `feat: ChartEntity with typed render/read state`

---

### Task 3: ImageEntity + IKitImageStore (blob-backed + in-memory testing)

**Files:**
- Create: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Image/ImageState.cs`
- Create: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Image/IImage.cs`
- Create: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Image/IKitImageStore.cs`
- Create: `src/Modules/UI/DigitalBrain.Modules.UI/Image/ImageEntity.cs`
- Create: `src/Modules/UI/DigitalBrain.Modules.UI/Image/BlobKitImageStore.cs`
- Create: `src/Modules/UI/DigitalBrain.Modules.UI/Image/MemoryKitImageStore.cs`
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/UIModule.cs` (register store by mode — copy the testing-mode branch shape from `AIModule.Configure`)
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/DigitalBrain.Modules.UI.csproj` + `Directory.Packages.props` (add `Azure.Storage.Blobs` — check the latest stable with `dotnet package search Azure.Storage.Blobs --exact-match`; the type may already flow transitively from `Aspire.Azure.Storage.Blobs`, in which case only the explicit PackageReference + CPM PackageVersion pin are needed)
- Test: `tests/DigitalBrain.Simulation.Tests/EntityTests.cs` (extend)

**Interfaces:**
- Consumes: keyed `BlobServiceClient` (`DigitalBrainNames.GrainState`), `Entity<TState>`.
- Produces:
  - `ImageState(string Prompt, string Model, string MediaType, string BlobName)`
  - `IImage : IEntity<ImageState>` with `Task Describe(ImageState state)` (grain type `"image"`)
  - `IKitImageStore { Task SaveAsync(string blobName, ReadOnlyMemory<byte> content, string mediaType, CancellationToken ct); Task<(byte[] Content, string MediaType)?> ReadAsync(string blobName, CancellationToken ct); }`
  - Blob container name constant: `BlobKitImageStore.ContainerName = "kit-images"`.

- [ ] **Step 1: Failing test** — `ImageEntity` describe/read plus `MemoryKitImageStore` round-trip:

```csharp
[Fact]
public async Task ImageEntityDescribesAndReadsItsState()
{
    var image = Fixture.GrainFactory.GetGrain<IImage>("test/image-1");
    await image.Describe(new ImageState("a red fox", "gpt-image-1", "image/png", "test-image-1.png"));

    var read = await image.Read();
    Assert.Equal("a red fox", read!.Prompt);
}

[Fact]
public async Task MemoryImageStoreRoundTripsBytes()
{
    var store = new MemoryKitImageStore();
    await store.SaveAsync("x.png", new byte[] { 1, 2, 3 }, "image/png", CancellationToken.None);

    var read = await store.ReadAsync("x.png", CancellationToken.None);
    Assert.Equal(new byte[] { 1, 2, 3 }, read!.Value.Content);
}
```

(`MemoryKitImageStore` must be reachable from the test project — make it `public`, or `internal` if `InternalsVisibleTo` already exists for Simulation.Tests; check how `SimulationProbeNeurons` reaches internals and follow that.)

- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement.** `ImageEntity` mirrors `ChartEntity`. Stores:

```csharp
// BlobKitImageStore.cs
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.UI;

internal sealed class BlobKitImageStore([FromKeyedServices(DigitalBrainNames.GrainState)] BlobServiceClient blobs)
    : IKitImageStore
{
    internal const string ContainerName = "kit-images";

    public async Task SaveAsync(string blobName, ReadOnlyMemory<byte> content, string mediaType, CancellationToken cancellationToken)
    {
        var container = blobs.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await container.GetBlobClient(blobName)
            .UploadAsync(new BinaryData(content), overwrite: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(byte[] Content, string MediaType)?> ReadAsync(string blobName, CancellationToken cancellationToken)
    {
        var blob = blobs.GetBlobContainerClient(ContainerName).GetBlobClient(blobName);
        if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var download = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        return (download.Value.Content.ToArray(), download.Value.Details.ContentType ?? "application/octet-stream");
    }
}
```

`MemoryKitImageStore` is a `ConcurrentDictionary<string, (byte[], string)>`. `UIModule.Configure` registers `IKitImageStore`: testing mode → memory; production → blob. (Also register memory store's blob-name→media-type via the tuple; media type on `Upload` for the blob store: pass `new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = mediaType } }` — use the `UploadAsync(BinaryData, BlobUploadOptions, CancellationToken)` overload instead of `overwrite` if the simple overload can't carry the content type; the compiler will steer.)

- [ ] **Step 4: Suite green.**
- [ ] **Step 5: Commit** — `feat: ImageEntity and kit image store over grain-state blobs`

---

### Task 4: IAgentToolSource seam + Agent turn-bound tool wrapping

**Files:**
- Create: `src/Modules/AI/Contracts/IAgentToolSource.cs`
- Modify: `src/Modules/AI/AI/Agent.cs`
- Modify: `src/Modules/AI/AI/Assistant.cs`
- Test: `tests/DigitalBrain.Simulation.Tests/AgentToolTests.cs` (new)

**Interfaces:**
- Consumes: `TurnBoundFunction(AIFunction capability, TaskScheduler turnScheduler)` (exists), `Agent.Tools` virtual.
- Produces:

```csharp
// Contracts/IAgentToolSource.cs
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// Modules contribute AI tools without the AI module referencing them (UI → AI is
// the allowed direction; this seam inverts tool ownership).
public interface IAgentToolSource
{
    IReadOnlyList<AIFunction> ToolsFor(OwnerId owner);
}
```

(Verify `OwnerId`'s namespace by reading `src/Kernel/DigitalBrain.Abstractions` — `ChatTurnWorker.cs` imports `DigitalBrain.Abstractions.Identity` for it.)

- [ ] **Step 1: Failing test** — a fake tool source's function reaches the model call:

```csharp
[Fact]
public async Task AssistantOffersToolsFromRegisteredToolSources()
{
    // Boot shape: copy the simulation fixture from ChatTurnTests; register a probe
    // IAgentToolSource + a capturing IChatClient in the host's testing services, send
    // one chat message, then assert the captured ChatOptions.Tools contains "probe_tool".
}
```

Concretely: add a `ProbeToolSource` and a `CapturingChatClient` (records the last `ChatOptions` it saw, returns the canned reply) to `tests/DigitalBrain.Simulation.Tests/TestEntities.cs`-style support files; register them via the same silo-configuration hook the fixture already uses for `SimulationProbeNeurons`. The assertion: after `chat.Send(...)` completes a turn, `capturing.LastOptions!.Tools!.Any(t => t.Name == "probe_tool")`.

- [ ] **Step 2: Run to fail** (Assistant ignores tool sources today).
- [ ] **Step 3: Implement:**

In `Agent.cs`, wrap tools turn-bound when building options (replace the current `options.Tools = [.. tools]` line):

```csharp
if (tools.Count > 0)
{
    var turnScheduler = TaskScheduler.Current;
    options.Tools = [.. tools.Select(tool =>
        tool is AIFunction capability ? new TurnBoundFunction(capability, turnScheduler) : tool)];
}
```

In `Assistant.cs`:

```csharp
protected override IReadOnlyList<AITool> Tools =>
    [.. ServiceProvider.GetServices<IAgentToolSource>().SelectMany(source => source.ToolsFor(Id.Owner))];
```

and extend `Instructions` with one paragraph:

```
Your abilities are exactly your tools. When asked whether you can do something,
answer from the tools you actually have — never claim an ability without one,
and offer the tool-backed ability when you do have it.
```

- [ ] **Step 4: Suite green.**
- [ ] **Step 5: Commit** — `feat: modules contribute agent tools through IAgentToolSource`

---

### Task 5: SupportsTools on LLMModel + tool-stripping middleware

**Files:**
- Modify: `src/Modules/AI/Contracts/LLM/LLMModel.cs` (add `public virtual bool SupportsTools => true;`)
- Modify: `src/Modules/AI/Contracts/Ollama/IGemma4.cs` (descriptor `Gemma4` adds `public override bool SupportsTools => false;` — conservative: Ollama Gemma tool templates are unverified; flip to true after a live probe)
- Modify: `src/Modules/AI/AI/Clients/AIClients.cs` (`BuildChatPipeline`)
- Test: `tests/DigitalBrain.Simulation.Tests/ProductionLlmRegistrationTests.cs` (extend)

**Interfaces:**
- Produces: pipeline guarantee — a model with `SupportsTools == false` never receives `ChatOptions.Tools`.

- [ ] **Step 1: Failing test:**

```csharp
[Fact]
public void ToollessModelsNeverReceiveTools()
{
    Assert.False(LLMModel.FindByMarker(typeof(DigitalBrain.AI.Ollama.IGemma4))!.SupportsTools);
    Assert.True(LLMModel.FindByMarker(typeof(DigitalBrain.AI.OpenAI.IGpt54))!.SupportsTools);
}
```

(The behavioral strip is covered by the middleware unit below; this pins the catalog.)

- [ ] **Step 2: Run to fail** (`SupportsTools` missing).
- [ ] **Step 3: Implement** — in `BuildChatPipeline`, before `.UseFunctionInvocation()`:

```csharp
var pipeline = new ChatClientBuilder(Factories[model.Provider].CreateChatClient(model, configuration));
if (!model.SupportsTools)
{
    // Models that cannot emit tool calls must never be told about tools —
    // the assistant then answers capability questions honestly with "no".
    pipeline = pipeline.Use(static async (messages, options, next, cancellationToken) =>
    {
        if (options?.Tools is { Count: > 0 })
        {
            options = options.Clone();
            options.Tools = null;
            options.ToolMode = null;
        }

        await next(messages, options, cancellationToken).ConfigureAwait(false);
    });
}
return pipeline
    .UseFunctionInvocation()
    .UseOpenTelemetry(...)   // unchanged existing call
    .Build(provider);
```

(Order note: the strip middleware sits INSIDE the pipeline — after `UseFunctionInvocation` in wrapping order means function invocation sees tools and would loop; placing `.Use(strip)` on the builder BEFORE `.UseFunctionInvocation()` makes strip the inner client. Verify by the middleware execution order in Microsoft.Extensions.AI: builder calls wrap outward — the LAST added is outermost. Assert with the unit below rather than reasoning: add a test that resolves the Gemma-keyed client from `BuildProvider(AllProvidersConfigured)`, calls `GetResponseAsync` with a dummy `AIFunction` in options against a stubbed inner — if stubbing the inner is impractical through the factory path, keep the catalog test and move the strip verification to Task 9's end-to-end testing-mode run.)

- [ ] **Step 4: Suite green.**
- [ ] **Step 5: Commit** — `feat: tool support is a model capability; toolless models get tools stripped`

---

### Task 6: IImageGeneration service (OpenAI-backed, fake in testing)

**Files:**
- Create: `src/Modules/AI/Contracts/IImageGeneration.cs`
- Create: `src/Modules/AI/AI/Clients/OpenAIImageGeneration.cs`
- Modify: `src/Modules/AI/AI/AIModule.cs` + `AIClients.cs` (conditional registration), `AITestingClients.cs` (fake)
- Test: `tests/DigitalBrain.Simulation.Tests/ProductionLlmRegistrationTests.cs` (extend)

**Interfaces:**
- Produces:

```csharp
// Contracts/IImageGeneration.cs
namespace DigitalBrain.AI;

public sealed record GeneratedKitImage(byte[] Content, string MediaType, string Model);

public interface IImageGeneration
{
    Task<GeneratedKitImage> GenerateAsync(string prompt, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Failing test:**

```csharp
[Fact]
public void ImageGenerationRegistersOnlyWhenOpenAIIsConfigured()
{
    using var withKey = BuildProvider(AllProvidersConfigured);
    Assert.NotNull(withKey.GetService<IImageGeneration>());

    using var without = BuildProvider([]);
    Assert.Null(without.GetService<IImageGeneration>());
}
```

- [ ] **Step 2: Run to fail.**
- [ ] **Step 3: Implement** (API verified against OpenAI .NET 2.12 docs: `GetImageClient(model).GenerateImageAsync(prompt, options)`, `GeneratedImage.ImageBytes`, `ImageGenerationOptions.ResponseFormat = GeneratedImageFormat.Bytes`):

```csharp
// AI/Clients/OpenAIImageGeneration.cs
using System.ClientModel;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Images;

namespace DigitalBrain.AI;

internal sealed class OpenAIImageGeneration(IConfiguration configuration) : IImageGeneration
{
    private const string DefaultModel = "gpt-image-1";

    public async Task<GeneratedKitImage> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var model = configuration[$"{AIClients.ConfigurationRoot}:OpenAI:ImageModel"] ?? DefaultModel;
        var apiKey = configuration[$"{AIClients.ConfigurationRoot}:OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("Image generation requires DigitalBrain:AI:OpenAI:ApiKey.");

        var client = new OpenAIClient(new ApiKeyCredential(apiKey)).GetImageClient(model);
        var image = await client.GenerateImageAsync(
            prompt,
            new ImageGenerationOptions { ResponseFormat = GeneratedImageFormat.Bytes },
            cancellationToken).ConfigureAwait(false);

        return new GeneratedKitImage(image.Value.ImageBytes.ToArray(), "image/png", model);
    }
}
```

Registration: in `AIClients.Add`, `if (new OpenAIProviderFactory().IsConfigured(...))` is not available at Add-time (no IConfiguration) — register unconditionally-lazy instead is WRONG for the honesty gate. Correct approach: registration moves to `AIModule.Configure`, which HAS `builder.Configuration`:

```csharp
if (builder.Configuration[$"{AIClients.ConfigurationRoot}:OpenAI:ApiKey"] is { Length: > 0 })
{
    builder.Services.AddSingleton<IImageGeneration, OpenAIImageGeneration>();
}
```

Testing mode: `AITestingClients.Add` registers a `TestImageGeneration` returning a fixed 1×1 PNG (`Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")`, `"image/png"`, `"test-image"`).

- [ ] **Step 4: Suite green.**  (Note: `BuildProvider` in the test reflects over `AIClients.Add` — since registration moved to `AIModule.Configure`, either test through `AIModule` with an in-memory `ISiloBuilder` stub if one exists in the test project, or keep a small `AIClients.AddImageGeneration(IServiceCollection, IConfiguration)` internal helper called by `AIModule` and reflect over that. Prefer the helper — it keeps the reflection pattern the file already uses.)
- [ ] **Step 5: Commit** — `feat: OpenAI image generation service gated on configuration`

---

### Task 7: KitToolSource — render_chart + generate_image tools (UI module)

**Files:**
- Create: `src/Modules/UI/DigitalBrain.Modules.UI/Kit/KitToolSource.cs`
- Create: `src/Modules/UI/DigitalBrain.Modules.UI/Kit/KitInstanceNames.cs`
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/UIModule.cs` (register `IAgentToolSource`)
- Test: `tests/DigitalBrain.Simulation.Tests/KitToolTests.cs` (new)

**Interfaces:**
- Consumes: `IAgentToolSource` (Task 4), `IChart.Render` (Task 2), `IImage.Describe` + `IKitImageStore` (Task 3), `IImageGeneration` (Task 6), `IChat.HandleAsync(KitCardOffer)` (Task 1), `IGrainFactory`.
- Produces: tools `render_chart` and `generate_image`; entity naming helper `KitInstanceNames.Sibling(chatInstance, localName)`.

- [ ] **Step 1: Pin the naming rule with a test.** The chat grain key (e.g. what `PrincipalScoped.InstanceName(principal, "main")` yields) embeds the principal scope. A kit entity must live under the SAME scope so the kernel's `/kit` endpoints (Task 8) can resolve it from the caller's principal + local name. Read `PrincipalScoped.InstanceName` (find it via `git grep -n "class PrincipalScoped" src/Kernel`) and write:

```csharp
[Fact]
public void KitEntityNamesShareTheChatsPrincipalScope()
{
    var principal = new PrincipalId(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));
    var chat = PrincipalScoped.InstanceName(principal, "main");
    var chart = KitInstanceNames.Sibling(chat, "chart-abc12345");

    Assert.Equal(PrincipalScoped.InstanceName(principal, "chart-abc12345"), chart);
}
```

- [ ] **Step 2: Run to fail; implement `KitInstanceNames.Sibling`** by replacing the final local-name segment using the exact separator `PrincipalScoped` uses (read the source; do not guess the separator).

- [ ] **Step 3: Failing tool tests:**

```csharp
[Fact]
public async Task RenderChartToolCreatesTheEntityAndPostsACard()
{
    var tools = new KitToolSource(Fixture.GrainFactory, imageGeneration: null, imageStore: new MemoryKitImageStore());
    var render = tools.ToolsFor(Owner).Single(tool => tool.Name == "render_chart");

    var reply = await render.InvokeAsync(new AIFunctionArguments
    {
        ["chatName"] = ChatInstance,
        ["title"] = "Sales",
        ["chartKind"] = "bar",
        ["labels"] = new[] { "Q1", "Q2" },
        ["values"] = new[] { 10.0, 20.0 },
    }, CancellationToken.None);

    Assert.Contains("Sales", reply!.ToString());
    var transcript = await Fixture.GrainFactory.GetGrain<IChat>(ChatInstance).Read();
    Assert.Contains(transcript.Turns, turn => turn.Text == "Sales");
}

[Fact]
public async Task GenerateImageToolIsAbsentWithoutAnImageGenerator()
{
    var tools = new KitToolSource(Fixture.GrainFactory, imageGeneration: null, imageStore: new MemoryKitImageStore());
    Assert.DoesNotContain(tools.ToolsFor(Owner), tool => tool.Name == "generate_image");
}

[Fact]
public async Task GenerateImageToolStoresBytesEntityAndCard()
{
    var store = new MemoryKitImageStore();
    var tools = new KitToolSource(Fixture.GrainFactory, new TestImageGeneration(), store);
    var generate = tools.ToolsFor(Owner).Single(tool => tool.Name == "generate_image");

    var reply = await generate.InvokeAsync(new AIFunctionArguments
    {
        ["chatName"] = ChatInstance,
        ["prompt"] = "a red fox",
    }, CancellationToken.None);

    Assert.Contains("image", reply!.ToString(), StringComparison.OrdinalIgnoreCase);
    // One image entity + blob exist; the chat got a card turn.
}
```

(Adjust construction to the DI shape you actually give `KitToolSource` — if it takes `IServiceProvider`, build tests accordingly. `TestImageGeneration` from Task 6 must be visible to this test project; place it where `AITestingClients` internals are already reachable, or duplicate a tiny fake here.)

- [ ] **Step 4: Implement `KitToolSource`:**

```csharp
using System.ComponentModel;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using Microsoft.Extensions.AI;

namespace DigitalBrain.UI;

internal sealed class KitToolSource(
    IGrainFactory grains,
    IImageGeneration? imageGeneration,
    IKitImageStore imageStore) : IAgentToolSource
{
    public IReadOnlyList<AIFunction> ToolsFor(OwnerId owner)
    {
        var tools = new List<AIFunction>
        {
            AIFunctionFactory.Create(RenderChartAsync, "render_chart",
                "Render a chart for the owner. It appears as a live card in the chat and can "
                + "be shown on surfaces later. Use it whenever the owner asks to see data as a chart."),
        };

        if (imageGeneration is not null)
        {
            tools.Add(AIFunctionFactory.Create(GenerateImageAsync, "generate_image",
                "Generate an image from a text prompt and show it as a card in the chat. "
                + "Use it whenever the owner asks for a picture, illustration, or image."));
        }

        return tools;
    }

    private async Task<string> RenderChartAsync(
        [Description("The current chat's name, exactly as stated in the conversation context")] string chatName,
        [Description("Short chart title")] string title,
        [Description("bar or line")] string chartKind,
        [Description("Point labels, one per value")] string[] labels,
        [Description("Point values, one per label")] double[] values,
        CancellationToken cancellationToken)
    {
        if (labels.Length == 0 || labels.Length != values.Length)
        {
            return "labels and values must be non-empty and the same length.";
        }

        var name = $"chart-{Guid.NewGuid():N}"[..14];
        var instance = KitInstanceNames.Sibling(chatName, name);
        var points = labels.Zip(values, static (label, value) => new ChartPoint(label, value)).ToList();

        await grains.GetGrain<IChart>(instance).Render(new ChartState(title, chartKind, points));
        await grains.GetGrain<IChat>(chatName)
            .HandleAsync(new KitCardOffer(KitCardKinds.Chart, name, title), cancellationToken);

        return $"Chart '{title}' is now showing in the chat as card '{name}'.";
    }

    private async Task<string> GenerateImageAsync(
        [Description("The current chat's name, exactly as stated in the conversation context")] string chatName,
        [Description("What the image should depict")] string prompt,
        CancellationToken cancellationToken)
    {
        var generated = await imageGeneration!.GenerateAsync(prompt, cancellationToken);

        var name = $"image-{Guid.NewGuid():N}"[..14];
        var blobName = $"{name}.png";
        await imageStore.SaveAsync(blobName, generated.Content, generated.MediaType, cancellationToken);

        var instance = KitInstanceNames.Sibling(chatName, name);
        await grains.GetGrain<IImage>(instance)
            .Describe(new ImageState(prompt, generated.Model, generated.MediaType, blobName));
        await grains.GetGrain<IChat>(chatName)
            .HandleAsync(new KitCardOffer(KitCardKinds.Image, name, prompt), cancellationToken);

        return $"Image for '{prompt}' is now showing in the chat as card '{name}'.";
    }
}
```

`UIModule.Configure` adds: `builder.Services.AddSingleton<IAgentToolSource>(sp => new KitToolSource(sp.GetRequiredService<IGrainFactory>(), sp.GetService<IImageGeneration>(), sp.GetRequiredService<IKitImageStore>()));` — `GetService` (nullable) is the honesty gate.

- [ ] **Step 5: Suite green.**
- [ ] **Step 6: Commit** — `feat: kit tools render charts and generate images as chat cards`

---

### Task 8: Kernel /kit HTTP endpoints

**Files:**
- Create: `src/Kernel/DigitalBrain.Kernel/MapKitEntities.cs`
- Modify: `src/Kernel/DigitalBrain.Kernel/HttpSurfacePaths.cs` (add `KitChartPath = "/kit/charts/{chartName}"`, `KitImagePath = "/kit/images/{imageName}"`, `KitImageContentPath = "/kit/images/{imageName}/content"`)
- Modify: `src/Kernel/DigitalBrain.Kernel/Program.cs` (`app.MapKitEntities();` next to `MapChatStreams`)
- Test: `tests/DigitalBrain.E2E.Tests/KitSurfaceTests.cs` (new — copy `McpSurfaceTests`' fixture/HTTP shape but target the kernel's UI endpoint client; `UiEvidenceTests`/`BootSmokeTests` show how to get the kernel HTTP client from the fixture)

**Interfaces:**
- Consumes: `IChart.Read`, `IImage.Read`, `IKitImageStore.ReadAsync`, `PrincipalScoped.InstanceName`, `HttpActor.Current`, `brain.GetGrainProxy<T>` (exact pattern in `MapOwnerCommands.cs`/`MapChatStreams.cs`).
- Produces: `GET /kit/charts/{name}` → 200 `{"title","chartKind","points":[{"label","value"}]}` or 404; `GET /kit/images/{name}` → 200 `{"prompt","model","mediaType"}` or 404; `GET /kit/images/{name}/content` → 200 raw bytes with the stored content type, or 404.

- [ ] **Step 1: Failing E2E test** — boot fixture (testing mode), seed a chart through the grain factory, then:

```csharp
[Fact]
public async Task ChartStateIsReadableOverHttp()
{
    // seed: fixture grain factory → IChart Render (instance derived the same way the
    //       kernel will: PrincipalScoped.InstanceName(actor principal, "chart-e2e")).
    using var http = fixture.CreateHttpClient(/* kernel resource + endpoint names as UiEvidenceTests does */);
    var response = await http.GetAsync("/kit/charts/chart-e2e", TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Contains("\"title\"", await response.Content.ReadAsStringAsync());
}
```

Plus a 404 fact for an unknown name. (The E2E fixture's principal for HTTP calls: read how existing E2E tests authenticate — `ShellHostingExtensions.OwnerEnvironmentVariable`/`HttpActor` — and seed the chart under that same principal.)

- [ ] **Step 2: Run to fail (404/route missing).**
- [ ] **Step 3: Implement `MapKitEntities`** following `MapChatStreams`' endpoint shape: validate name, `PrincipalScoped.InstanceName(HttpActor.Current.PrincipalId, name)` (wrap `ArgumentException` → 400 like `TryPrincipalResource`), `await brain.GetGrainProxy<IChart>(instance).Read()`, null → 404, else `Results.Json`/`TypedResults.Ok`. Image content endpoint resolves `IImage.Read()` for the blob name, then `IKitImageStore.ReadAsync`, returns `Results.File(content, mediaType)`.
- [ ] **Step 4: Suite green (E2E included).**
- [ ] **Step 5: Commit** — `feat: kit entity state and image bytes readable over the kernel HTTP surface`

---

### Task 9: Tool-aware testing chat client + chart-in-chat E2E

**Files:**
- Modify: `src/Modules/AI/AI/Testing/TestChatClient.cs`
- Test: `tests/DigitalBrain.E2E.Tests/KitSurfaceTests.cs` (extend)

**Interfaces:**
- Produces: deterministic testing behavior — when the latest user message contains `"chart"` (ordinal, case-insensitive) AND `options.Tools` offers `render_chart`, the streaming response emits one `FunctionCallContent` for `render_chart` with canned args (`chatName` parsed from the system context line `chat '...'`; title `"Test chart"`; kind `"bar"`; labels `["A","B"]`; values `[1, 2]`); on the follow-up round (messages already contain a `FunctionResultContent`), it replies `"Rendered."`. All other inputs keep today's `"Test assistant reply."`.

- [ ] **Step 1: Failing E2E test:**

```csharp
[Fact]
public async Task AskingForAChartProducesACardOnTheTurnStream()
{
    // POST /owner/commands {kind:"chat.send", chatName:"kit", text:"show me a chart"}
    // then GET /chats/kit/events and read SSE lines until a data payload contains
    // "\"cards\"" and "\"kind\":\"chart\"" (bounded by the test cancellation token).
}
```

Copy the SSE-reading approach from whichever existing E2E test consumes `/chats/{name}/events` (see `UiEvidenceTests`); if none reads SSE directly, read the response stream line-by-line exactly like `ui_client.dart` does.

- [ ] **Step 2: Run to fail** (TestChatClient never calls tools, so no card ever appears).
- [ ] **Step 3: Implement the tool-aware branch in `TestChatClient`:**

```csharp
public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    await Task.Yield();

    var conversation = messages.ToList();
    if (conversation.Any(static m => m.Contents.OfType<FunctionResultContent>().Any()))
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Rendered.") { FinishReason = ChatFinishReason.Stop };
        yield break;
    }

    var lastUser = conversation.LastOrDefault(static m => m.Role == ChatRole.User)?.Text ?? "";
    var renderChart = options?.Tools?.OfType<AIFunction>()
        .FirstOrDefault(static tool => tool.Name == "render_chart");
    if (renderChart is not null && lastUser.Contains("chart", StringComparison.OrdinalIgnoreCase))
    {
        var chatName = ChatNameFromContext(conversation);
        yield return new ChatResponseUpdate(ChatRole.Assistant,
        [
            new FunctionCallContent("call-1", "render_chart", new Dictionary<string, object?>
            {
                ["chatName"] = chatName,
                ["title"] = "Test chart",
                ["chartKind"] = "bar",
                ["labels"] = new[] { "A", "B" },
                ["values"] = new[] { 1.0, 2.0 },
            }),
        ]) { FinishReason = ChatFinishReason.ToolCalls };
        yield break;
    }

    yield return new ChatResponseUpdate(ChatRole.Assistant, Reply) { FinishReason = ChatFinishReason.Stop };
}
```

`ChatNameFromContext` regexes `chat '([^']+)'` out of the first system message (that exact phrase is produced by `ChatTurnWorker`'s conversation-context line — keep them in sync, and fail loudly returning `"main"` only if absent). Update `GetResponseAsync` to delegate to the streaming path (`.ToChatResponseAsync()`), so both paths share the logic.

- [ ] **Step 4: Full suite green** — this closes UC1 end-to-end at the API level: send "show me a chart" → card on the SSE stream → `GET /kit/charts/{name}` returns the state (assert both in the E2E test).
- [ ] **Step 5: Commit** — `test: chart request produces a live card end to end in testing mode`

---

### Task 10: Flutter core — cards on the wire + kit entity reads

**Files:**
- Modify: `src/Modules/UI/Flutter/core/lib/src/models/chat_models.dart` (`KitCardRef` + `cards` on `ChatTurnEvent`)
- Modify: `src/Modules/UI/Flutter/core/lib/src/ui_client.dart` (`readChart`, `readImage`, `readImageBytes`)
- Test: `src/Modules/UI/Flutter/core/test/ui_models_test.dart` + `ui_client_test.dart` (extend, following their existing fake-HTTP style)

**Interfaces:**
- Produces (Dart):

```dart
final class KitCardRef {
  const KitCardRef({required this.kind, required this.name, required this.caption});
  final String kind; final String name; final String caption;
  factory KitCardRef.fromJson(Map<String, Object?> json) => KitCardRef(
    kind: json['kind'] as String? ?? '',
    name: json['name'] as String? ?? '',
    caption: json['caption'] as String? ?? '');
}
```

`ChatTurnEvent` gains `final List<KitCardRef> cards;` parsed from `json['cards']` exactly like the existing `buttons` block (absent → const []). `DigitalBrainUiClient` gains:

```dart
Future<ChatChartOffer?> readChart(String chartName)      // GET /kit/charts/{name} → reuse ChatChartOffer as the state model (title/points/chartKind match the C# JSON)
Future<Map<String, Object?>?> readImage(String imageName) // GET /kit/images/{name}
Future<Uint8List?> readImageBytes(String imageName)       // GET /kit/images/{name}/content
```

each returning null on 404 and throwing `StateError` on other non-200s (match `openScene`'s error style).

- [ ] **Step 1: Failing dart tests** — `ChatTurnEvent.fromJson` with a `cards` array yields refs; without it yields `const []`; `readChart` parses a canned 200 body and returns null on 404 (use the same fake-client harness `ui_client_test.dart` already uses).
- [ ] **Step 2: `flutter test` in `core/` to fail.**
- [ ] **Step 3: Implement; `flutter analyze` clean; `flutter test` green (all existing + new).**
- [ ] **Step 4: Commit** — `feat(flutter): kit card refs on chat turns and kit entity reads`

---

### Task 11: Flutter kit + shell — render chart and image cards in chat

**Files:**
- Modify: `src/Modules/UI/Flutter/kit/lib/src/models/kit_part.dart` (add `KitChartRefPart`, `KitImageRefPart` to the sealed hierarchy + `tryParse` arms)
- Create: `src/Modules/UI/Flutter/kit/lib/src/components/image/kit_image.dart` (renders provided bytes with caption; loading/error placeholders keyed `kit_image_loading` / `kit_image_error`)
- Modify: `src/Modules/UI/Flutter/shell/lib/chat/chat_contracts.dart` (`kitParts` extension: append `for (final card in cards) ...` mapping chart→`KitChartRefPart(name, caption)`, image→`KitImageRefPart(name, caption)`)
- Modify: the shell chat message builder that turns `KitPart`s into widgets (follow `kitParts` usage from `brain_chat_screen.dart` into `digitalbrain_ui_kit`'s `kit_chat_builders.dart` / `kit_message_factory.dart`): a `KitChartRefPart` renders a loader widget that calls a supplied `Future<ChatChartOffer?> Function(String name)` and then builds the existing `KitChart` from the fetched state; `KitImageRefPart` uses a supplied `Future<Uint8List?> Function(String name)` and `KitImage`. The shell wires both callbacks from `DigitalBrainUiClient` in `main.dart` → `BrainChatApp` → `BrainWorkspace` → `BrainChatScreen` (same prop-drilling as `onStream`).
- Test: `src/Modules/UI/Flutter/shell/test/` widget test: pump a `BrainChatScreen` (or the message list widget directly, matching existing chat widget tests) with a fake turn stream containing one chart card + a fake reader returning a canned `ChatChartOffer` → expect `find.byKey(Key('kit_chart_Test chart'))`; image card + canned 1×1 PNG bytes → expect the image widget key.

**Interfaces:**
- Consumes: `KitChart` (exists, takes `KitChartPart`), `KitCardRef`/`readChart`/`readImageBytes` (Task 10).
- Produces: chart & image cards visible in the chat transcript; loading placeholder while fetching.

- [ ] **Step 1: Write the failing widget tests** (fake stream + fake readers as above).
- [ ] **Step 2: `flutter test` in `shell/` to fail.**
- [ ] **Step 3: Implement parts, `KitImage`, builders, and the callback prop-drilling.**
- [ ] **Step 4: `flutter analyze` + `flutter test` in `kit/` and `shell/` — all green; also rerun `core/`.**
- [ ] **Step 5: Commit** — `feat(flutter): chart and image cards render live in the chat`

---

### Task 12: Wire-contract golden + docs + full verification

**Files:**
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/flutter-wire-contracts.golden.json` (add entries for `ui.kit-card`, `ui.chart-state`, `ui.chart-point` following the file's existing `types` schema — read it first)
- Modify: `src/Modules/UI/Flutter/core/test/wire_contract_golden_test.dart` (extend `containsAll` with the new aliases)
- Modify: `docs/ARCHITECTURE.md` — build-order line 3 gains `(template + Chart + Image shipped <date>)`
- Test: everything

- [ ] **Step 1: Update golden + dart golden test; run `flutter test` in `core/`.**
- [ ] **Step 2: Full verification** — `dotnet build DigitalBrain.slnx` (0 warnings) → `dotnet test DigitalBrain.slnx --no-build` (0 failed) → `flutter analyze`+`flutter test` in `core/`, `kit/`, `shell/`.
- [ ] **Step 3: Run `/code-review medium` per house rule; fix confirmed findings.**
- [ ] **Step 4: Commit** — `feat: chart and image kit cards complete end to end`

---

## Acceptance walkthrough (manual, after implementation)

1. `aspire run`, open the Flutter shell, type **"show me a chart of Q1=10 Q2=20"**.
   - Testing-free path needs a real model: set `Parameters:openai-api-key` (or another cloud key) in AppHost user secrets. The default model becomes that provider automatically.
   - Expected: assistant text + a live chart card in the transcript; `GET /kit/charts/{name}` returns its state.
2. Type **"can you generate an image?"**
   - With a cloud chat model + OpenAI key: assistant answers yes, asks/uses the prompt, an image card appears.
   - With local Gemma only (`SupportsTools == false` → tools stripped): assistant honestly answers it cannot.
3. Type **"generate an image of a red fox"** → image card renders from `/kit/images/{name}/content`.
