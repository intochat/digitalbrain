# DigitalBrain v2 — Slice 3: Workspace Substrate + Conformance + Web Connector

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Checkbox steps.

**Goal:** The two-tier UI vocabulary as data (Tier 2 blocks), script-writable Window Neurons, the feed compositor (every revision advance lands durably in a per-space feed neuron), a conformance suite every kind passes, and a bounded web-fetch connector — proven live: script renders a block window → MCP reads it; chat/llm/window revisions appear in the feed via cursor; `web.fetch.v1` against a real URL.

**Architecture:** Blocks are pure content (closed 11-primitive vocabulary, bounded depth/bytes, camelCase JSON). Windows are neurons whose journal is render history. The feed is a neuron: after persist, `NeuronGrain` fire-and-forgets `feed.append.v1` to `{owner}|{space}|feed/main` (idempotent by `{sourceKey}:{revision}`; failure never breaks the domain commit — EIAN projection rule). Web is the connector template: reads are bounded and journaled; it emits no effects (fetch is a read), and the effect-gate conformance case rides ProposerKind.

**Scope note:** Google/Salesforce deliberately move to Slice 4 (they drag OAuth machinery; the conformance suite built here receives them). Destinations (Today etc.) materialize as window neurons on first render — no eager creation until the UI gateway slice.

## Global Constraints

Zero comments · CPM (no Version attrs) · net11.0 · v1 untouched · framework primitives · root `dotnet test --logger "console;verbosity=minimal"` green zero skips pristine after every commit · bounded payloads everywhere (doc ≤ 65,536 bytes, depth ≤ 8, web response journal-truncated to 8,192 bytes UTF-8-safe) · camelCase wire.

---

### Task 1: Blocks — the Tier 2 vocabulary

**Files:** Create `modules/Brain.Modules.Workspace/Blocks.cs`; Test `tests/Brain.KernelTests/BlocksTests.cs`.

**Interfaces (consumed by Tasks 2, 5):**

```csharp
namespace Brain.Modules.Workspace;

public static class Blocks
{
    public static BlockDoc Doc(params Block[] blocks);
    public static Block Section(string title, params Block[] children);
    public static Block Columns(params Block[] children);
    public static Block Text(string value);
    public static Block Metric(string label, object value);
    public static Block Field(string label, string value);
    public static Block List(params string[] items);
    public static Block Table(string[] columns, params string[][] rows);
    public static Block Timeline(IEnumerable<Block> entries);
    public static Block Entry(string title, string detail);
    public static Block Media(string url, string alt);
    public static Block Progress(string label, double fraction);
    public static Block ActionRow(params BlockAction[] actions);
    public static BlockAction Action(string label, string contract, string inputJson);
}
public sealed record Block(string Kind, string Json);
public sealed record BlockAction(string Label, string Contract, string InputJson);
public sealed record BlockDoc(string Json)
{
    public const int MaxBytes = 65536;
    public const int MaxDepth = 8;
    public static BlockDoc Parse(string json);
}
```

- `Doc` serializes `{"version":1,"blocks":[...]}` camelCase; each block `{"kind":"text",...}`.
- `Parse` validates: well-formed JSON, version 1, every `kind` in the closed set {section, columns, text, metric, field, list, table, timeline, entry, media, progress, actionRow}, nesting depth ≤ 8, UTF-8 size ≤ 65,536 — violations throw `BrainException("input.invalid", …)`.

- [ ] Tests: doc round-trips through Parse; unknown kind rejected; depth 9 rejected (nested Sections); oversize rejected; ActionRow action carries contract+inputJson. RED → implement → GREEN → commit `feat(workspace): closed block vocabulary`.

---

### Task 2: WindowKind + IWindow

**Files:** Create `modules/Brain.Modules.Workspace/WindowKind.cs`, `IWindow.cs`; Test `tests/Brain.KernelTests/WindowKindTests.cs` (+ register in a `WorkspaceKindsConfigurator`).

**Interfaces:**

```csharp
public interface IWindow : INeuronContract
{
    [NeuronContract("window.render.v1")]
    Task<WindowReply> RenderAsync(BlockDoc doc);
}
public sealed record WindowReply(long Revision);
```

- Kind `"window"`, contract `window.render.v1`: input is the raw BlockDoc JSON (`InputJson` IS the doc); validate via `BlockDoc.Parse` (all its failures already `input.invalid`); event `("window.rendered", docJson)`; output `{"revision": context.Revision + 1}`. Projection `"document"` (and default): the latest `window.rendered` payload, or `{"version":1,"blocks":[]}` when none.

- [ ] Tests: render then read returns the doc; second render supersedes; invalid doc fails closed zero-state; replay idempotent. Commit `feat(workspace): window neurons render block documents`.

---

### Task 3: FeedKind + kernel post-persist append

**Files:** Create `modules/Brain.Modules.Workspace/FeedKind.cs`; Modify `kernel/Brain.Kernel/NeuronGrain.cs`; Test `tests/Brain.KernelTests/FeedTests.cs`.

- FeedKind: kind `"feed"`, contract `feed.append.v1` input `{sourceKey, revision, kind}` (camelCase), event `("feed.record", same)`, output `{"sequence": context.Revision + 1}`; projection `"recent"`: last 50 records newest-first.
- NeuronGrain: after `WriteStateAsync()` and before returning the receipt, when `_address.Kind != "feed"`, call `{owner}|{space}|feed/main` with `feed.append.v1`, commandId `$"{grainKey}:{receipt.Revision}"`, caller = own grain key, wrapped in try/catch swallowing ALL exceptions (projection failure must never fail the domain commit). No await-ordering games: await inside the try.
- [ ] Tests: chat post lands one feed record with matching sourceKey+revision; two posts → two records in order; a fixture WITHOUT feed kind registered still completes invocations successfully (append failure tolerated); feed neuron itself does not self-append (no recursion). Commit `feat(workspace): feed compositor records every revision durably`.

---

### Task 4: Web connector — bounded fetch

**Files:** Create `modules/Brain.Modules.Web/Brain.Modules.Web.csproj` (refs Brain.Contracts; no packages beyond framework), `WebKind.cs`, `IWeb.cs`, `WebHosting.cs` (`AddBrainWeb(this ISiloBuilder)`: registers `AddBrainKind("web", sp => new WebKind(sp.GetRequiredService<IHttpClientFactory>()))` + `services.AddHttpClient()`); Test `tests/Brain.KernelTests/WebKindTests.cs` with a stub `HttpMessageHandler`.

- Add `public const string ProviderTimeout = "provider.timeout";` and `public const string ProviderError = "provider.error";` to BrainErrors.
- `web.fetch.v1` input `{url, maxBytes?}`: guards — absolute http/https URL only else `input.invalid`; maxBytes clamp [1, 262144] default 65536; 30s timeout → `provider.timeout`; network failure → `provider.error`; response body read up to clamp; event `("web.fetched", {urlSha256, status, bytes, body(≤8192 UTF-8-safe truncated)})`; output `{"status":…, "body": fullBoundedBody, "revision": …+1}`. `IWeb.FetchAsync(WebFetch(string Url, int? MaxBytes)) → WebReply(int Status, string Body, long Revision)`.
- [ ] Tests (stub handler): success journals event with status; non-http scheme fails closed; handler throwing HttpRequestException → provider.error zero-state; replay idempotent (handler called once — count on the stub). Commit `feat(web): bounded journaled fetch connector`.

---

### Task 5: Conformance suite

**Files:** Create `tests/Brain.ConformanceTests/Brain.ConformanceTests.csproj` (refs Sdk + all module projects + Brain.Client; xunit + TestingHost + Microsoft.NET.Test.Sdk), `KindConformance.cs`, per-kind classes; add to solution.

```csharp
public abstract class KindConformance<TConfigurator> : BrainTest<TConfigurator> where TConfigurator : ISiloConfigurator, new()
{
    protected abstract string KindName { get; }
    protected abstract string SampleContract { get; }
    protected abstract string SampleInputJson { get; }
    protected abstract string NeuronId { get; }
}
```

Shared `[Fact]`s in the base: unknown contract fails closed with `contract.unknown` and zero state; duplicate commandId replays byte-identical receipt with no second event; describe reports the kind and non-empty contracts; malformed input JSON fails closed without state (skippable via `virtual bool AcceptsRawJson` for kinds whose sample IS raw JSON, e.g. window). Concrete classes: ChatConformance, LlmConformance (FakeChatClient configurator), WindowConformance, FeedConformance, WebConformance (stub handler configurator) — each ~10 lines of overrides reusing existing configurators where possible.
- [ ] RED (project skeleton compiles, tests fail against missing overrides) → implement → GREEN → root gate → commit `test(conformance): every kind passes one suite`.

---

### Task 6: Host wiring + live proof

- `hosts/Brain.Kernel.Host/Program.cs`: `AddBrainKernel(new ChatKind(), new WindowKind(), new FeedKind()).AddBrainAi(config).AddBrainWeb()` (WindowKind/FeedKind constructors are parameterless — keep instance registration; Web needs the factory form).
- `behaviors/smoke/ChatSmoke.cs`: after the llm call, render a window:

```csharp
var board = brain.Get<IWindow>("local-owner|actor/mcp-dev|window/inbox-brief");
var window = await board.RenderAsync(Blocks.Doc(
    Blocks.Metric("Chat revision", reply.Revision),
    Blocks.Timeline([Blocks.Entry("smoke", DateTimeOffset.UtcNow.ToString("O"))])));
Console.WriteLine($"window revision {window.Revision}");
```

- Controller live proof: script runs (chat + llm + window) → MCP `neuron_read` window `document` shows the blocks; MCP `neuron_read` feed/main `recent` shows chat, llm, window records; MCP `ReadEventsAsync`-backed catch-up via `neuron_read` cursor semantics deferred to edge slice (events tool does not exist — read the `recent` projection); `web.fetch.v1` via MCP against `https://example.com` returns status 200 and journals; replay of the fetch commandId does not re-fetch. Root suite green zero skips.
- [ ] Commit `feat(v2): slice 3 complete — workspace substrate, conformance, web connector`.

## Self-review notes

- Spec v2 coverage: §6 Tier 2 vocabulary (Task 1-2; Tier 1 governed kinds are Flutter-side views arriving with the app slice), feed durable-then-visible (Task 3 — durability inherent: records are journal events), §4.4 connector template minus OAuth (Task 4; OAuth + google/salesforce = Slice 4), §10 ConformanceTests (Task 5).
- The kernel feed-append is the only kernel change; it must remain fail-open on projection (swallow) and fail-closed on domain (existing pipeline untouched before persist).
- Type consistency: BlockDoc.Parse used by both Blocks tests and WindowKind; feed commandId format `{grainKey}:{revision}` pinned in Task 3 tests.
