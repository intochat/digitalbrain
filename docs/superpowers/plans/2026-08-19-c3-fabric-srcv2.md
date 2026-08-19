# C3: Fabric to the srcv2 Shape Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A first-class `DigitalBrainResource` in the Aspire model, the dead streams/pubsub fabric deleted, and the runtime/client glue at the srcv2 thin shape — opened by the three regression pins the C2 final review named.

**Architecture:** The fabric becomes exactly spec §4: one Azure storage emulator → `clustering`+`reminders` tables and `grainstate`+`journal` blobs, composed via `AddOrleans(name).WithClustering(...).WithReminders(...).WithGrainStorage("Default", grainState)`. A `DigitalBrainResource : Resource` is registered in the model as the dashboard parent of the fabric. Nothing consumes Orleans streams or the PubSubStore (verified: zero `IAsyncStream`/`GetStreamProvider`/`ImplicitStreamSubscription` hits in src) — the queues, pubsub tables, keyed queue clients, stream options, and the four names constants all die. Consumer surface stays `WithReference(brain)` / `WithReference(brain.AsClient())`.

**Tech Stack:** Aspire 13.5.0-preview.1.26376.5 (AppHost SDK + Aspire.Hosting.Azure.Storage + Aspire.Hosting.Orleans), Orleans 10.2.2 + Orleans.Journaling 10.2.2-rc.2.alpha.1, xUnit v3, Reqnroll (untouched).

**Spec:** `docs/superpowers/specs/2026-08-18-brain-core-refactor-design.md` — §4 (Fabric), §5 row C3, plus the "C2 final whole-branch review" subsection whose parked items and three test gaps this plan consumes.

## Global Constraints

- Frozen wire contracts are untouchable: chat wire (UserMessaged → TurnLifecycle Pending/Running → Responded+Completed | Failed/Cancelled, SSE chat-delta), journal semantics (Incoming/Outgoing, 512 entries/512KB, tallies, reset-snapshot), auth endpoints (`/auth/login`, `/auth/me`, `/auth/bootstrap`, `DigitalBrain.Auth` cookie), MCP tool names, `/brain/topology` + `/graph/events` shapes.
- `dotnet build DigitalBrain.slnx -warnaserror` green after every task; all three suites green per task: Simulation (baseline 27/27), Aspire (17/17), E2E (3/3, Docker Desktop running).
- NO `/// <summary>` doc comments; small inline comments only for invisible constraints; self-explanatory naming.
- NEVER access, read, or reference any path under `C:\Users`. Package APIs via Context7 MCP or dotnet-inspect against `E:\nuget` only.
- Known machine quirk: `dotnet test` may fail with an MTP handshake error before any test runs (oversized PATH). Fall back to the built test executables (same test host, same counts). Kill + report any test run past 8 minutes.
- Commits: exact subject given per task, two `-m` flags, second exactly `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. No here-strings.
- The HTTP endpoints named `MapChatStreams`/`MapShellStreams`/`MapBrainStreams` are SSE endpoints, NOT Orleans streams — they are out of scope and must not be touched by the streams deletion.

---

### Task 1: The net — three regression pins on the current fabric

These pin behavior that already works (they are the C2 review's named coverage gaps, landed BEFORE the fabric moves so the rewrite has a net). Expected first run: PASS. A FAIL is a discovered bug — stop and report it, do not "fix" the test.

**Files:**
- Create: `tests/DigitalBrain.Simulation.Tests/TestNeurons.cs`
- Create: `tests/DigitalBrain.Simulation.Tests/WireDeliveryTests.cs`
- Create: `tests/DigitalBrain.E2E.Tests/FabricSurfaceTests.cs`
- Modify: `src/Testing/DigitalBrain.Testing.E2E/BrainAppHostFixture.cs` (add a `GrainsAsync()` accessor next to `BrainFor`, ~line 103)

**Interfaces:**
- Consumes: `fixture.Sim.BrainFor(owner)` / `fixture.Sim.Grains` / `fixture.Sim.UniqueId(prefix)` (BrainSimulation); `JournalWait.ForAsync(brain, subject, kind, predicate)`; E2E `fixture.BrainFor(owner)`, `fixture.CreateHttpClient("kernel")` (cookie-carrying — the existing auth test proves login → me works on one client).
- Produces: `IPingerNeuron`/`IEchoNeuron` test neurons (grain types `pingerneuron`/`echoneuron`), `Pinged` synapse alias `test.pinged`; `Task<IGrainFactory> BrainAppHostFixture.GrainsAsync()`.

- [ ] **Step 1: Test neurons for the wire-delivery pin**

`tests/DigitalBrain.Simulation.Tests/TestNeurons.cs` — mirror `TestEntities.cs`'s precedent exactly (the test assembly is already registered as a grain assembly in `SimulationFixture`; `[GrainType]` must equal `GrainTypeNames.Of(...)` of the interface — `IPingerNeuron` → `pingerneuron`):

```csharp
using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Simulation.Tests;

[GenerateSerializer]
[Alias(AliasName)]
public sealed record EmitPing([property: Id(0)] string Note) : Synapse
{
    public const string AliasName = "test.emit-ping";
}

[GenerateSerializer]
[Alias(AliasName)]
public sealed record Pinged([property: Id(0)] string Note) : Synapse
{
    public const string AliasName = "test.pinged";
}

[Alias("test.pinger")]
public interface IPingerNeuron : INeuron, IHandle<EmitPing>;

[Alias("test.echo")]
public interface IEchoNeuron : INeuron, IHandle<Pinged>;

// Emits Pinged when poked; the wire-delivery pin routes that emission through the Brain.
[GrainType("pingerneuron")]
public sealed class PingerNeuron : Neuron, IPingerNeuron
{
    public Task HandleAsync(EmitPing synapse, CancellationToken cancellationToken)
        => EmitAsync(new Pinged(synapse.Note));
}

[GrainType("echoneuron")]
public sealed class EchoNeuron : Neuron, IEchoNeuron
{
    public Task HandleAsync(Pinged synapse, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
```

If `Neuron`/`EmitAsync` visibility or an abstract member blocks this exact shape, read `src/Modules/UI/DigitalBrain.Modules.UI/Surface/SurfaceBoot.cs` and mirror it; the emission must go through `EmitAsync` (the Brain-routed path), not `SendAsync`.

- [ ] **Step 2: The wire-delivery pin**

`tests/DigitalBrain.Simulation.Tests/WireDeliveryTests.cs`:

```csharp
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Brain;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Client;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class WireDeliveryTests(SimulationFixture fixture)
{
    // The C2 review's named gap: nothing anywhere wired a connection and observed the
    // emission land. This is the end-to-end delivery path a brain wire promises:
    // Connect -> source EmitAsync -> Brain.Route -> target Deliver -> target Incoming journal.
    [Fact]
    public async Task AConnectedWireDeliversTheEmissionToTheTargetsIncomingJournal()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("wire-owner"));
        var cancellationToken = TestContext.Current.CancellationToken;
        var pinger = NeuronId.For<IPingerNeuron>(brain.Owner, fixture.Sim.UniqueId("pinger"));
        var echo = NeuronId.For<IEchoNeuron>(brain.Owner, fixture.Sim.UniqueId("echo"));
        var grain = fixture.Sim.Grains.GetGrain<IBrain>(
            EntityId.For<IBrain>(brain.Owner, DigitalBrainNames.DefaultBrain).ToGrainId());

        await grain.Connect(new Connection(pinger, Pinged.AliasName, echo));
        await brain.FireAsync<IPingerNeuron>(pinger.Name, new EmitPing("across-the-wire"), cancellationToken);

        var delivered = await JournalWait.ForAsync(
            brain,
            echo,
            JournalKind.Incoming,
            static d => d.Synapse is Pinged { Note: "across-the-wire" });

        Assert.IsType<Pinged>(delivered.Synapse);
    }
}
```

- [ ] **Step 3: Run the Simulation suite** — expected 28/28 (27 baseline + this pin, all PASS). If the new pin FAILS, the wire path is broken in production code: STOP and report, per the task preamble.

- [ ] **Step 4: E2E fixture grain-factory accessor**

In `src/Testing/DigitalBrain.Testing.E2E/BrainAppHostFixture.cs`, next to `BrainFor` (~line 103): expose the script host's `IGrainFactory` (the host `ConnectScriptHostAsync` builds — reuse the same lazy host `BrainFor` uses; do not build a second host):

```csharp
public async Task<IGrainFactory> GrainsAsync()
{
    var host = await ScriptHostAsync().ConfigureAwait(false); // the same lazy accessor BrainFor awaits
    return host.Services.GetRequiredService<IGrainFactory>();
}
```

Adapt the one line to the fixture's actual lazy-host member name (read `BrainFor`'s body); add nothing else.

- [ ] **Step 5: E2E durability + topology pins**

`tests/DigitalBrain.E2E.Tests/FabricSurfaceTests.cs` (add a ProjectReference to `DigitalBrain.Modules.UI.Contracts` if the test project lacks one — check first):

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.UI;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class FabricSurfaceTests(AppHostFixture fixture)
{
    // C2 review gap 1: nothing pinned state recovery on the REAL Default blob provider —
    // EntityTests round-trip inside one activation in memory. Deactivate everything idle,
    // then prove the chart re-reads its points from grainstate blobs.
    [Fact]
    public async Task RendererWrittenChartStateSurvivesActivationCollection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var brain = fixture.BrainFor($"dur{Guid.NewGuid():N}"[..12]);
        var name = "chart-durability";

        await brain.FireAsync<IUIRenderer>(name, new ChartPoint("series", "before", 41), cancellationToken);
        var written = await brain.GetEntity<IChart>(name).Read();
        Assert.NotNull(written);
        Assert.Single(written!.Points);

        var management = (await fixture.GrainsAsync()).GetGrain<IManagementGrain>(0);
        await management.ForceActivationCollection(TimeSpan.Zero);

        var survived = await brain.GetEntity<IChart>(name).Read();
        Assert.NotNull(survived);
        var point = Assert.Single(survived!.Points);
        Assert.Equal(41, point.Value);
    }

    // C2 review gap 3: /brain/topology and /graph/events are shell-consumed, were rewritten
    // twice in C2, and had zero coverage. Smoke them over real HTTP with the real auth cookie.
    [Fact]
    public async Task BrainTopologyAndGraphEventsServeTheShellWire()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var http = fixture.CreateHttpClient("kernel");

        var login = await http.PostAsJsonAsync(
            "/auth/login", new { username = "owner", password = "ownerowner" }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var topology = await http.GetAsync("/brain/topology", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, topology.StatusCode);
        var snapshot = await topology.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(JsonValueKind.Array, snapshot.GetProperty("neurons").ValueKind);
        Assert.Equal(JsonValueKind.Array, snapshot.GetProperty("connections").ValueKind);
        Assert.Equal(JsonValueKind.Array, snapshot.GetProperty("modules").ValueKind);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/graph/events?afterSequence=0");
        var events = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, events.StatusCode);
        Assert.Equal("text/event-stream", events.Content.Headers.ContentType?.MediaType);
    }
}
```

Adjust ONLY mechanical mismatches found by reading the real types (`ChartState`'s points collection property name, the exact `IManagementGrain.ForceActivationCollection` overload — verify via dotnet-inspect against `E:\nuget` on the pinned Orleans version; the fixture's actual class name `AppHostFixture` vs `BrainAppHostFixture` in the collection definition — copy from `BootSmokeTests.cs`). Do not change what each test observes. If the durability read after collection needs a brief poll (re-activation latency), poll bounded ≤10s like `UIRendererTests` does.

- [ ] **Step 6: Run E2E** — expected 5/5 (3 baseline + these two, all PASS; same STOP-and-report rule on failure). Run Aspire (17/17) and build `-warnaserror` too.

- [ ] **Step 7: Commit**

```bash
git add tests/ src/Testing/
git commit -m "Pin the net: wire delivery, blob durability, topology wire" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: DigitalBrainResource — the fabric gets a first-class parent

**Files:**
- Create: `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/DigitalBrainResource.cs`
- Rename: `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/ClientDigitalBrainReference.cs` → `DigitalBrainClientReference.cs` (git mv; type + all usages — the C2 tree-accuracy note pinned this exact mismatch)
- Modify: `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/DigitalBrainHostingExtensions.cs:10-44` (AddDigitalBrain), `Brain/DigitalBrainBuilder.cs` (ctor + `Resource` property)
- Modify: `src/Modules/AI/…/Aspire.Hosting`, `src/Modules/Memory/…/Aspire.Hosting`, `src/Modules/UI/…/Aspire.Hosting` — one `.WithParentRelationship(brain.Resource)` line where each creates its container/executable (Ollama, Qdrant, shell host; find the creation sites by grepping `AddOllama|AddQdrant|AddExecutable|AddContainer` in those three projects)
- Test: `tests/DigitalBrain.Aspire.Tests/TopologyConformanceTests.cs`

**Interfaces:**
- Consumes: Aspire `Resource`, `builder.AddResource(...)`, `WithInitialState(CustomResourceSnapshot)`, `ExcludeFromManifest()`, `WithParentRelationship(...)` — all verified against Aspire's custom-resource docs; `ResourceRelationshipAnnotation` for the pin (verify the annotation type/fields via dotnet-inspect against `E:\nuget` if the name differs).
- Produces: `public sealed class DigitalBrainResource(string name) : Resource(name);`, `public IResourceBuilder<DigitalBrainResource> Resource { get; }` on `DigitalBrainBuilder`, type name `DigitalBrainClientReference`.

- [ ] **Step 1: Write the failing conformance pins** (append to `TopologyConformanceTests.cs`):

```csharp
[Fact]
public void BrainResourceExistsAndParentsTheFabric()
{
    var brain = fixture.Model.Resource(ProductSurfaceResourceNames.Brain);
    var storage = fixture.Model.Resource(DigitalBrainNames.Storage);

    Assert.IsType<DigitalBrainResource>(brain);
    Assert.Contains(
        storage.Annotations.OfType<ResourceRelationshipAnnotation>(),
        relationship => relationship.Resource == brain && relationship.Type == "Parent");
}
```

Add `Brain` to the test-side `ProductSurfaceResourceNames` with the same value `ProductSurfaceResources.Brain` uses in AppHost (read it; it is the brain name, expected `"brain"`). Add the `DigitalBrain.Aspire.Hosting` using/reference the test needs.

- [ ] **Step 2: Run it — expect FAIL** ("resource 'brain' not found" or the type assert).

- [ ] **Step 3: Implement**

`DigitalBrainResource.cs` (srcv2 verbatim):

```csharp
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainResource(string name) : Resource(name);
```

In `AddDigitalBrain`, before the storage block:

```csharp
var resource = builder.AddResource(new DigitalBrainResource(name))
    .ExcludeFromManifest()
    .WithInitialState(new CustomResourceSnapshot
    {
        ResourceType = "DigitalBrain",
        CreationTimeStamp = DateTime.UtcNow,
        State = KnownResourceStates.Running,
        Properties = [new(CustomResourceKnownProperties.Source, "DigitalBrain fabric")],
    });
```

then `.WithParentRelationship(resource)` on the `storage` builder, pass `resource` into the `DigitalBrainBuilder` ctor (new first-position parameter after `name`, srcv2 ordering), expose it as `public IResourceBuilder<DigitalBrainResource> Resource { get; }`, and add the one-line `.WithParentRelationship(brain.Resource)` at each module resource creation site (only where the module builder already holds the `DigitalBrainBuilder`; if a site cannot see it, leave that site alone and note it in the report). Do the rename with `git mv` + type rename + the two usages in `DigitalBrainHostingExtensions` (`AsClient()` return type and the `WithReference` overload parameter).

- [ ] **Step 4: Run the Aspire suite — expect 18/18** (17 baseline + the new pin). Then Simulation 28/28, E2E 5/5, build `-warnaserror`.

- [ ] **Step 5: Commit**

```bash
git add src/ tests/
git commit -m "The brain is a first-class resource: DigitalBrainResource parents the fabric" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Delete the streams/pubsub fabric — nothing consumes it

Every deletion site is enumerated below (verified by grep before this plan was written). The suites are the net: the frozen SSE endpoints (`Map*Streams.cs`) are HTTP, not Orleans streams — do not touch them.

**Files:**
- Modify: `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/DigitalBrainHostingExtensions.cs` (lines 24-25, 30, 32, 41-42, 68-69, 88), `Brain/DigitalBrainBuilder.cs` (streams/pubsub ctor params, fields, null-checks)
- Modify: `src/Aspire/DigitalBrain.Aspire/DigitalBrainRuntimeHostingExtensions.cs` (lines 13-19 consts + comment, 31-32, 45-50; prune dead usings)
- Modify: `src/Aspire/DigitalBrain.Aspire/DigitalBrainClientHostingExtensions.cs` (line 65; `RequireStorage` lines 18-35 → returns `string`)
- Modify: `src/Testing/DigitalBrain.Testing/BrainSimulation.cs` (lines 59, 61 — keep `AddMemoryGrainStorage(DefaultGrainStorage)` and the volatile journal)
- Modify: `src/Testing/DigitalBrain.Testing.E2E/BrainAppHostFixture.cs` (lines 385-397 — drop the streams connection mirroring; `RequireStorage` call site adapts to the new `string` return)
- Modify: `src/Kernel/DigitalBrain.Abstractions/DigitalBrainNames.cs` (delete `Streams`, `PubSub`, `StreamProvider`, `PubSubStore`; the header comment notes "no Streams/PubSub" per the spec tree)
- Modify: `tests/DigitalBrain.Aspire.Tests/TopologyConformanceTests.cs:17-18` and `NamesConformanceTests.cs:19-20` (remove the four InlineData rows)
- Modify: `src/Kernel/DigitalBrain.Kernel/Dockerfile` header env inventory (rider: remove any streams/pubsub connection lines; ADD the missing `ConnectionStrings__grainstate` line — the parked C2 debt item)
- Modify: csproj package references that become unused (`Aspire.Azure.Storage.Queues` in DigitalBrain.Aspire if present; queues coverage in Aspire.Hosting csproj) — remove only what `dotnet build -warnaserror` proves unused

**Interfaces:**
- Consumes: Task 2's `DigitalBrainBuilder` ctor shape (resource, orleans, grainState, journal — srcv2 ordering).
- Produces: `public static string RequireStorage(IConfiguration configuration)` returning the clustering connection string only (same refusal message); `DigitalBrainNames` without the four constants.

- [ ] **Step 1: Update the conformance pins FIRST** (remove the four InlineData rows) — with the rows present the deletion cannot go green; this is the intentional-pin-change the ledger records.
- [ ] **Step 2: Hosting layer** — remove queues/pubsub provisioning, `WithGrainStorage(PubSubStore, ...)`, `WithStreaming(...)`, the two health-gate lines, the two `WithReference` projections and the client-side `Streams` reference; `DigitalBrainBuilder` loses both members. Result must read like srcv2's `AddDigitalBrain` (the srcv2 file is the target shape: resource → storage → clustering/reminders/grainState/journal → AddOrleans with clustering+reminders+Default grain storage → health gates on exactly those five).
- [ ] **Step 3: Runtime + client glue** — remove the keyed queue/pubsub clients, the `HashRingStreamQueueMapperOptions`/`AzureQueueOptions` blocks, the `StreamQueueCount`/`StreamMessageVisibilityTimeout` consts and their layout comment; client glue loses the queue client; `RequireStorage` returns the single clustering string.
- [ ] **Step 4: Testing SDK** — sim loses `AddMemoryGrainStorage(PubSubStore)` + `AddMemoryStreams`; E2E fixture loses the streams mirroring lines and adapts the `RequireStorage` call.
- [ ] **Step 5: Names + Dockerfile rider** — delete the four constants; fix the Dockerfile env inventory (both directions: no streams/pubsub, add grainstate).
- [ ] **Step 6: Full gates** — build `-warnaserror` (this proves no dangling package/using), Simulation 28/28, Aspire 18/18, E2E 5/5. The E2E run is the real proof: the silo boots against a fabric with no queues or pubsub tables.
- [ ] **Step 7: Commit**

```bash
git add -u
git add src/ tests/
git commit -m "No streams, no pubsub: the fabric is tables, blobs, and Orleans natives" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Spec sync + C3 outcome + recorded gates

**Files:**
- Modify: `docs/superpowers/specs/2026-08-18-brain-core-refactor-design.md` (a `### C3 outcome` subsection after §4, mirroring the C1/C2 outcome style)
- Check: `docs/JOURNALS.md` for streams/pubsub wording drift (fix drift only)

**Steps:**
- [ ] **Step 1:** Write the `### C3 outcome` section: the resource + parenting shape as landed; the streams/pubsub deletion with the `RequireStorage` signature change; the three C2-named pins landed in Task 1 (name each test); glue confirmed at the srcv2 thin shape; the Dockerfile inventory fix; final LOC for src and tests (single-total method: `find src -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*" -print0 | xargs -0 cat | wc -l`) vs C2's 16,589+fix baseline; any deviation discovered during Tasks 1-3. Every factual claim re-verified against source, not copied from this plan.
- [ ] **Step 2:** JOURNALS.md drift check; record the verdict either way.
- [ ] **Step 3:** Full gates once more, counts + durations recorded in the report.
- [ ] **Step 4: Commit**

```bash
git add docs/
git commit -m "C3 complete: fabric spec sync" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

## Self-Review

- Spec coverage: §4 resource → Task 2; §4 fabric composition + no-streams/no-pubsub → Task 3; §5 C3 gate (Tier 1 conformance updated) → Tasks 2-3 pin edits; C2 review's three test gaps → Task 1; parked Dockerfile item → Task 3 rider; srcv2 glue shape → Task 3 steps 2-3 (post-deletion the glue IS the thin shape — verified against both files during planning).
- Placeholders: none — every step carries exact code or exact line-anchored deletions.
- Type consistency: `DigitalBrainClientReference` rename (Task 2) precedes Task 3's `WithReference` edits, which reference the renamed type; `RequireStorage(IConfiguration) → string` is consumed by the single fixture call site named in the same task.
- Deliberate omission: no `DigitalBrainNames`-consolidation with srcv2's separate `DigitalBrainResourceNames` — this repo's single names class is already the conformance-pinned single source of truth.
