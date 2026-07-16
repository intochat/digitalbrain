# DigitalBrain v2 — Slice 2: modules/ai — Intelligence as a Peer Module

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Brain.Modules.Ai` — ILlm Neurons with a Fast/Balanced/Reasoning tier catalog, bounded journaled completions, and Ollama + AzureOpenAI adapters — proven live: `MCP neuron_invoke llm.complete.v1 → Ollama → journaled completion`, and a behavior script calling `Get<ILlm>`.

**Architecture:** The ModelFacet seam IS the kind registry plus neuron addressing — no special kernel code. The ai module registers an `llm` kind whose handler resolves a keyed `IChatClient` per tier; every completion is a journal event (auditable AI). Other kinds and scripts use models by invoking ILlm neurons (`…|llm/{tier}`) — neuron-to-neuron, same pipeline, same grants. v1's `InoOperationWorkerGrain`/`AgentFrameworkWorkflowRunner` are NOT modified — they die in demolition (Slice 7); the coupling is severed by the rebuild, not by migration surgery.

**Tech Stack:** Microsoft.Extensions.AI 10.7.0 (`IChatClient`), OllamaSharp (via CommunityToolkit.Aspire.OllamaSharp 13.4.0), Azure.AI.OpenAI 2.1.0 — all already in `Directory.Packages.props`. Reference implementations: `src/DigitalBrain.Kernel/Llm/DigitalBrainChat.cs` (provider wiring, READ ONLY) and `E:\IAW\src\Core\AI\LLMModel.cs` + `ModelTiers.cs` (tier catalog).

## Global Constraints

- Zero comments in tracked source. Central package management (no Version attributes). net11.0, Nullable + ImplicitUsings.
- v1 untouched (additive-only AppHost/config allowed, mirroring Slice 1).
- Framework primitives over custom abstractions: `IChatClient` is the provider abstraction; keyed DI binds tiers; no agent framework, no workflow engine in this slice.
- Exact root gate after every commit: `dotnet test --logger "console;verbosity=minimal"` green, zero skips.
- Bounded model execution: every completion carries a token budget (default 1024, hard cap 4096) and a 60s timeout; prompt hard cap 32 KB UTF-8; journaled response payload truncated to 8 KB.
- Exactly two providers: Ollama, AzureOpenAI. Adding any other is out of scope.

## File structure this slice creates

```text
modules/Brain.Modules.Ai/Brain.Modules.Ai.csproj
modules/Brain.Modules.Ai/ILlm.cs
modules/Brain.Modules.Ai/ModelCatalog.cs
modules/Brain.Modules.Ai/LlmKind.cs
modules/Brain.Modules.Ai/AiHosting.cs
modules/Brain.Modules.Sdk/FakeChatClient.cs
tests/Brain.KernelTests/AiKindsConfigurator.cs
tests/Brain.KernelTests/LlmKindTests.cs
tests/Brain.KernelTests/ModelCatalogTests.cs
behaviors/smoke/AiSmoke.cs (second top-level program NOT allowed in one project — see Task 5: replaces ChatSmoke content behind an args switch)
hosts/Brain.Kernel.Host (modified: AddBrainAi + LlmKind)
hosts/DigitalBrain.AppHost (modified additively: Ollama endpoint env for brain-kernel)
```

---

### Task 1: ILlm contract + model tier catalog

**Files:**
- Create: `modules/Brain.Modules.Ai/Brain.Modules.Ai.csproj` (refs `kernel/Brain.Contracts`; packages `Microsoft.Extensions.AI`), `ILlm.cs`, `ModelCatalog.cs`
- Test: `tests/Brain.KernelTests/ModelCatalogTests.cs`
- Modify: solution via `dotnet sln Brain.slnx add`

**Interfaces (produced, consumed by every later task):**

```csharp
namespace Brain.Modules.Ai;

public interface ILlm : INeuronContract
{
    [NeuronContract("llm.complete.v1")]
    Task<LlmReply> CompleteAsync(LlmRequest request);
}
public sealed record LlmRequest(string Prompt, int? MaxOutputTokens = null);
public sealed record LlmReply(string Text, string Model, long Revision);

public enum ModelTier { Fast, Balanced, Reasoning }

public sealed record ModelBinding(ModelTier Tier, string Provider, string Model);

public sealed class ModelCatalog
{
    readonly IReadOnlyDictionary<ModelTier, ModelBinding> _bindings;
    public ModelCatalog(IEnumerable<ModelBinding> bindings) { ... }
    public ModelBinding Resolve(ModelTier tier) => ...;
    public static ModelTier ParseTier(string neuronId) => ...;
    public static ModelCatalog FromConfiguration(IConfiguration config) => ...;
}
```

- `ParseTier("llm/balanced")` → `ModelTier.Balanced`; unknown tier segment → `BrainException("input.invalid", …)`.
- `FromConfiguration` reads section `Brain:Ai` — `Provider` (`ollama` | `azureopenai`), `Fast`/`Balanced`/`Reasoning` model ids with defaults `llama3.1:8b` for all three when the section is absent (dev default). `Resolve` on a missing binding throws `BrainException("kind.unknown", …)`? No — use `BrainErrors.UnknownContract`? Neither fits; add const `BrainErrors.ModelUnavailable = "model.unavailable"` to `kernel/Brain.Contracts/BrainErrors.cs`.

- [ ] **Step 1: Failing tests** (`ModelCatalogTests` — plain xunit, no cluster):

```csharp
public class ModelCatalogTests
{
    [Fact]
    public void Parses_tier_from_neuron_id()
    {
        Assert.Equal(ModelTier.Balanced, ModelCatalog.ParseTier("llm/balanced"));
        Assert.Equal(ModelTier.Fast, ModelCatalog.ParseTier("llm/fast"));
    }

    [Fact]
    public void Unknown_tier_fails_closed()
    {
        var exception = Assert.Throws<BrainException>(() => ModelCatalog.ParseTier("llm/galaxy"));
        Assert.Equal("input.invalid", exception.Code);
    }

    [Fact]
    public void Configuration_defaults_bind_all_tiers_to_ollama()
    {
        var config = new ConfigurationBuilder().Build();
        var catalog = ModelCatalog.FromConfiguration(config);
        var binding = catalog.Resolve(ModelTier.Reasoning);
        Assert.Equal("ollama", binding.Provider);
        Assert.Equal("llama3.1:8b", binding.Model);
    }

    [Fact]
    public void Missing_binding_reports_model_unavailable()
    {
        var catalog = new ModelCatalog([new ModelBinding(ModelTier.Fast, "ollama", "x")]);
        var exception = Assert.Throws<BrainException>(() => catalog.Resolve(ModelTier.Reasoning));
        Assert.Equal(BrainErrors.ModelUnavailable, exception.Code);
    }
}
```

- [ ] **Step 2: fail → implement → pass → root build → commit** `feat(ai): ILlm contract and tier catalog`

---

### Task 2: LlmKind — bounded journaled completion

**Files:**
- Create: `modules/Brain.Modules.Ai/LlmKind.cs`
- Create: `modules/Brain.Modules.Sdk/FakeChatClient.cs` (test double: `IChatClient` returning configured text + usage; ~30 lines, mirrors IAW MockChatClient)
- Test: `tests/Brain.KernelTests/AiKindsConfigurator.cs`, `tests/Brain.KernelTests/LlmKindTests.cs`

**Interfaces:**
- `LlmKind(ModelCatalog catalog, IServiceProvider services)` : `INeuronKind`; Kind `"llm"`; Contracts `["llm.complete.v1"]`.
- Handler: parse input `{prompt, maxOutputTokens?}` (camelCase); guards: empty/whitespace prompt or > 32,768 UTF-8 bytes → `input.invalid`; clamp `maxOutputTokens` to [1, 4096], default 1024. Tier = `ModelCatalog.ParseTier(context.Address.NeuronId)`; binding = `catalog.Resolve(tier)`; client = `services.GetKeyedService<IChatClient>(binding.Provider)` (null → `model.unavailable`). Call with `ChatOptions { MaxOutputTokens = clamped }` under a 60s linked `CancellationTokenSource`; timeout → `BrainException("model.timeout", …)` (add const `BrainErrors.ModelTimeout = "model.timeout"`) with zero state.
- Event: `("llm.completed", {promptSha256, response(≤8,192 UTF-8 bytes, truncated), model, tier})`. Output: `{"text": fullResponse, "model": binding.Model, "revision": context.Revision + 1}`.
- Projection `"usage"`: fold journal → `{"completions": count, "model": lastModel}`.

- [ ] **Step 1: Failing tests** (`LlmKindTests : BrainTest<AiKindsConfigurator>`; `AiKindsConfigurator : ISiloConfigurator` registers `AddBrainKernel(new LlmKind(...))` with a default `ModelCatalog` and `siloBuilder.Services.AddKeyedSingleton<IChatClient>("ollama", new FakeChatClient("fake-reply"))`):

```csharp
public class LlmKindTests(BrainClusterFixture<AiKindsConfigurator> fixture) : BrainTest<AiKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Completion_journals_and_returns_text()
    {
        var llm = Neuron("llm", "balanced");
        var receipt = await llm.InvokeAsync(new("llm.complete.v1", """{"prompt":"say hi"}""", "cmd-1", OwnerSession));
        Assert.Contains("fake-reply", receipt.OutputJson);
        var events = await llm.ReadEventsAsync(0, 10);
        Assert.Single(events.Events);
        Assert.Equal("llm.completed", events.Events[0].Kind);
    }

    [Fact]
    public async Task Duplicate_command_does_not_call_model_twice()
    {
        var llm = Neuron("llm", "balanced-replay");
        var first = await llm.InvokeAsync(new("llm.complete.v1", """{"prompt":"one"}""", "cmd-dup", OwnerSession));
        var second = await llm.InvokeAsync(new("llm.complete.v1", """{"prompt":"two"}""", "cmd-dup", OwnerSession));
        Assert.Equal(first, second);
        Assert.Single((await llm.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Empty_prompt_fails_closed()
    {
        var llm = Neuron("llm", "guard");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            llm.InvokeAsync(new("llm.complete.v1", """{"prompt":""}""", "cmd-1", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Equal(0, (await llm.ReadAsync("usage")).Revision);
    }

    [Fact]
    public async Task Unknown_tier_address_fails_closed()
    {
        var llm = Neuron("llm", "galaxy");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            llm.InvokeAsync(new("llm.complete.v1", """{"prompt":"x"}""", "cmd-1", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
    }
}
```

Note: tier parse uses the full NeuronId (`llm/balanced-replay` → strip after `-`? NO — keep it honest: ParseTier accepts `llm/{tier}` or `llm/{tier}-{suffix}`? Simplest: tier = segment after `llm/` up to first `-`; document in ParseTier tests in Task 1 by adding `Assert.Equal(ModelTier.Balanced, ModelCatalog.ParseTier("llm/balanced-replay"));`).

- [ ] **Step 2: fail → implement → pass (expect 25/25 total) → root build → commit** `feat(ai): bounded journaled llm kind`

---

### Task 3: Provider adapters — AiHosting

**Files:**
- Create: `modules/Brain.Modules.Ai/AiHosting.cs`
- Modify: `modules/Brain.Modules.Ai/Brain.Modules.Ai.csproj` (add packages `CommunityToolkit.Aspire.OllamaSharp`, `Azure.AI.OpenAI`, `Microsoft.Extensions.AI.OpenAI`)
- Test: extend `tests/Brain.KernelTests/ModelCatalogTests.cs` with configuration-binding cases

**Interfaces:**
- `AiHosting.AddBrainAi(this ISiloBuilder silo, IConfiguration config)`: builds `ModelCatalog.FromConfiguration(config)`, registers it as singleton; registers keyed `IChatClient`s: `"ollama"` → `new OllamaApiClient(new Uri(config["Brain:Ai:OllamaEndpoint"] ?? "http://localhost:11434"), defaultModel)` (OllamaApiClient implements IChatClient — mirror `DigitalBrainChat.BuildOllama`, READ `src/DigitalBrain.Kernel/Llm/DigitalBrainChatClients.cs` for the exact construction); `"azureopenai"` → registered ONLY when `Brain:Ai:AzureOpenAIEndpoint` is present (AzureOpenAIClient → GetChatClient(model).AsIChatClient(), key/DefaultAzureCredential per the v1 pattern). Returns silo with `new LlmKind(...)` registered via `AddBrainKernel`.
- Provider selection is per-catalog-binding, so mixed configurations (fast=ollama, reasoning=azureopenai) work by keying clients by provider name.

- [ ] **Step 1: Failing configuration tests** (bind `Brain:Ai:Provider=azureopenai`, `Brain:Ai:Balanced=gpt-4o-mini` via in-memory config; assert catalog bindings; no live client construction in tests).
- [ ] **Step 2: fail → implement → pass → root build → commit** `feat(ai): ollama and azure openai adapters behind keyed chat clients`

---

### Task 4: Host wiring

**Files:**
- Modify: `hosts/Brain.Kernel.Host/Program.cs` — `AddBrainAi(builder.Configuration)` replaces the bare `AddBrainKernel(new ChatKind())` (AddBrainAi composes: it must accept the other kinds — change signature to `AddBrainAi(config, params INeuronKind[] moreKinds)` or call `AddBrainKernel` once with all kinds; keep ONE AddBrainKernel call site).
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs` — additive only: `brainKernel.WithEnvironment("Brain__Ai__OllamaEndpoint", <ollama endpoint reference>)` if the existing Ollama resource is reachable from `ctx`; if `ctx` does not expose it, fall back to no env (localhost default covers dev) and note it in the report.
- Gates: root `dotnet build`; kernel tests still green; `aspire doctor`.

- [ ] **Step 1: wire → build → test → commit** `feat(ai): kernel host serves llm neurons`

---

### Task 5: Live proof — MCP → Ollama, script → ILlm

Controller-driven (mirrors Slice 1's proof):

1. Start Ollama (the AppHost's existing Ollama resource via `aspire run`, or a locally running Ollama with `llama3.1:8b`).
2. Start `Brain.Kernel.Host` + `Brain.Mcp`.
3. MCP: `neuron_invoke(address: "local-owner|actor/mcp-dev|llm/balanced", contract: "llm.complete.v1", inputJson: {"prompt":"Reply with exactly: BRAIN-OK"}, commandId: "ai-proof-1")` → receipt with model text; replay same commandId → identical receipt, single journal event.
4. Script: extend `behaviors/smoke/ChatSmoke.cs` — after the chat post, `brain.Get<ILlm>("local-owner|actor/mcp-dev|llm/balanced")` → `CompleteAsync(new("Reply with exactly: SCRIPT-OK"))` → print text (skip gracefully with exit 0 + message when the model endpoint is unreachable, so the script remains runnable without Ollama).
5. `neuron_read(llm/balanced, "usage")` → completions ≥ 2.
6. Exit gate: exact root `dotnet test --logger "console;verbosity=minimal"` green, zero skips; ledger records the proof.

- [ ] **Steps: script edit (implementer) → live proof (controller) → root gate → commit** `feat(v2): slice 2 complete — intelligence is a module`

## Self-review notes

- Spec v2 §4.3 coverage: ILlm ✓, tiers ✓, two adapters ✓, ModelFacet-as-kind-registry ✓, coupling severed by non-migration (v1 untouched, dies in Slice 7) ✓. Agent abstractions and the self-healing compile loop are deliberately deferred to the behaviors slice (they serve authoring, not model serving).
- Type consistency: `LlmRequest/LlmReply` camelCase over the wire matches the Slice-1 proxy fix; `BrainErrors.ModelUnavailable`/`ModelTimeout` added once in Task 2 prerequisites (Task 1 adds ModelUnavailable, Task 2 adds ModelTimeout).
- No placeholders; the one execution-time verification is OllamaApiClient's IChatClient surface — verified against v1's working `DigitalBrainChatClients.BuildOllama` (READ ONLY).
