# Refined Architecture and Next Steps

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore DigitalBrain to a small neuron kernel with independently shipped, convention-driven modules, beginning with a typed AI module and module-owned Aspire hosting.

**Architecture:** Kernel routes incoming and outgoing synapses and knows nothing about AI, Flutter, Google, Salesforce, or Memory. Public namespaces and type names are the framework vocabulary. AppHost explicitly selects modules and their infrastructure; `silo.AddDigitalBrain()` uses a generated catalog to activate the selected runtime modules.

**Tech Stack:** .NET 10, Orleans 10, Aspire 13.4, `Microsoft.Extensions.AI`, OpenAI, OllamaSharp, xUnit v3, Roslyn incremental generation.

## Global Constraints

- This is a breaking hard cut. Do not add compatibility shims or obsolete forwarding types.
- Keep the kernel independent of provider SDKs and module-specific configuration.
- A module is selected exactly once in AppHost.
- The silo contains only `silo.AddDigitalBrain()` for DigitalBrain composition.
- `IChatClient` is private to concrete AI model neurons.
- Namespaces and type names are canonical architecture and future natural-language vocabulary.
- Runtime assembly scanning is forbidden. Generate catalogs during compilation.
- The AppHost module manifest is the sole activation authority.
- Vector search is a derived index over the typed catalog, never the source of truth.
- Memory is outside this implementation.
- Preserve a green root gate at every commit boundary.

---

## 1. Honest status on 2026-07-20

The repository starts this migration at commit `f5ae864651c8d37edbbf2827d893d8e6eac05219`.

Fresh baseline evidence:

```text
DigitalBrain.Tests:       111 passed
DigitalBrain.Simulations:  52 passed
DigitalBrain.HostTests:     7 passed
Total:                    170 passed, 0 failed, 0 skipped
```

Passing tests do not mean the product vision is complete.

| Area | Status | Evidence |
|---|---:|---|
| Durable neuron and synapse kernel | About 80% | Journals, bounded dedupe, owner checks, broadcast delivery, observation, and host restart tests exist |
| Approved framework architecture | About 40–45% | Kernel is useful, but module and AI work is provisional and contradictory |
| Typed AI module | About 10% | AI packages exist, but calls still route through tiers and Kernel |
| Module-owned Aspire integration | 0% | Current hosting owns `WithModel(ModelTier, provider, modelId, key)` |
| Generated module activation | 0% | Silo manually calls `AddModule<AIModule>()`, `AddAIModule()`, and `AddDigitalBrainModels()` |
| Natural-language-to-typed catalog | 0% | No canonical generated neuron catalog or semantic index exists |
| Script → proposal → approval → install → rollback | 0% end to end | Client surface is provisional; installation and governance rail do not exist |

The repository is not honestly in Phase 4. It is in a Phase 2b repair followed by a Phase 3 redesign.

## 2. Ratified architecture

### 2.1 Kernel

`DigitalBrain.Kernel.Neuron` owns only neuron mechanics:

- Receive and dispatch incoming synapses.
- Emit, send, and reply with outgoing synapses.
- Journal and observe traffic.
- Persist operational neuron state.
- Enforce owner, delivery, and concurrency invariants.

Kernel must not contain:

- `AskModelAsync`
- Model tiers or provider names
- `IChatClient`
- AI prompts or responses
- OAuth provider details
- Flutter contracts
- Semantic memory

### 2.2 Modules

Each domain is an independent package family:

```text
DigitalBrain.Modules.<Name>.Contracts
DigitalBrain.Modules.<Name>
DigitalBrain.Modules.<Name>.Aspire.Hosting   optional
```

Physical package names may contain `Modules` and `Contracts`. Public vocabulary does not:

```csharp
DigitalBrain.AI.ILLM
DigitalBrain.AI.Ollama.ILlama32
DigitalBrain.AI.OpenAI.IGpt56
DigitalBrain.Google.ICalendar
DigitalBrain.Google.IGmail
DigitalBrain.Salesforce.ISalesforce
DigitalBrain.Flutter.IFlutter
```

`.Contracts` references only `DigitalBrain.Abstractions`. Runtime packages own neurons and vendor adapters. Aspire hosting packages own resources, parameters, authentication setup, and projection into the silo.

### 2.3 AppHost and silo

AppHost is explicit:

```csharp
var brain = builder.AddBrain("brain");

brain.AddModule<AIModule>(ai => ai
    .WithLlm<Llama32>()
    .WithLlm<Gpt56>());
```

Each module is added once. Repeated `AddModule<AIModule>` calls are composition errors.

The silo is intentionally boring:

```csharp
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration)
    .AddDigitalBrainDevTools(builder.Environment));
```

Compilation generates the module catalog from referenced module types. AppHost projects the selected module manifest. Startup fails when AppHost selects a module absent from the silo catalog.

Package reference means available. `AddModule<T>()` means selected and configured.

### 2.4 AI vocabulary and implementation

The contracts package owns DigitalBrain-native conversation contracts:

```csharp
namespace DigitalBrain.AI;

public interface ILLM : INeuron
{
    Task<string> AskAsync(string prompt);
}

public interface IAgent : INeuron
{
    Task<string> AskAsync(string prompt);
}

public interface IGroupChat : IAgent;
```

Concrete model contracts live in provider namespaces:

```csharp
namespace DigitalBrain.AI.Ollama;

public interface ILlama32 : ILLM;
```

```csharp
namespace DigitalBrain.AI.OpenAI;

public interface IGpt56 : ILLM;
```

The runtime adapts `IChatClient` exactly once:

```csharp
namespace DigitalBrain.AI;

public abstract class LLM(IChatClient chatClient) : Neuron, ILLM
{
    public async Task<string> AskAsync(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            options: null,
            CancellationToken.None);

        return response.Text;
    }
}
```

Concrete neurons carry identity, not configuration objects:

```csharp
namespace DigitalBrain.AI.Ollama;

public sealed class Llama32(
    [Llm<Llama32>] IChatClient chatClient)
    : LLM(chatClient), ILlama32;
```

```csharp
namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt56(
    [Llm<Gpt56>] IChatClient chatClient)
    : LLM(chatClient), IGpt56;
```

There is no `ILlmDefinition`, `ModelDescriptor`, provider enum, tier, capability score, balancing router, or fallback router. Provider and model identity come from the canonical namespace and type name. The generator/analyzer rejects names outside the supported grammar.

Only concrete `LLM` neurons may receive `[Llm<TModel>] IChatClient`. Agents consume `ILlama32`, `IGpt56`, or another concrete model contract.

### 2.5 AI Aspire hosting

AI-specific configuration lives in `DigitalBrain.Modules.AI.Aspire.Hosting`:

```csharp
brain.AddModule<AIModule>(ai => ai
    .WithLlm<Llama32>()
    .WithLlm<Gpt56>());
```

`WithLlm<Gpt56>()`:

- Creates one shared `openai-api-key` secret parameter per `AIModule`.
- Adds the Markdown description:
  `Get your key at [platform.openai.com/api-keys](https://platform.openai.com/api-keys)`.
- Adds one OpenAI provider resource and one model resource.
- Reuses the provider and parameter for additional OpenAI models.
- Projects resource references, never secret literals.

`WithLlm<Llama32>()`:

- Creates one shared Ollama resource.
- Adds the `llama3.2` model to that resource.
- Creates no API-key parameter.
- Projects the Ollama endpoint and model identity to the silo.

The first implementation supports one provider connection per provider. Named accounts, failover, cost balancing, and per-model credentials are deliberately excluded.

### 2.6 Agents and group chat

`IAgent` is an LLM-powered conversational neuron, not a universal base interface.

Application agents compose typed capabilities:

```csharp
public sealed class MailAssistant(
    ILlama32 llama,
    IGmail gmail)
    : Agent, IMailAssistant;
```

`IGroupChat` coordinates `IAgent` neurons. It owns only bounded operational state:

- Participant identities
- Roles and turn order
- Current bounded transcript
- Pending turn and retry state
- Completion or cancellation status

Durability belongs to the neuron. Long-term facts, vector recall, cross-conversation knowledge, and provenance belong to a future Memory module.

### 2.7 Integrations

Integration modules expose typed neurons:

```csharp
DigitalBrain.Google.IGmail
DigitalBrain.Google.ICalendar
DigitalBrain.Salesforce.ISalesforce
```

Official MCP clients, OAuth, token refresh, transport schemas, and reconnect behavior stay inside the owning module. Raw MCP clients and tool dictionaries never cross the module interface.

Google and Salesforce do not depend on AI. Application agents compose integration neurons with concrete LLM neurons.

### 2.8 Canonical registry and semantic discovery

The generated catalog is the canonical registry. Its entries derive from:

- Public namespace and contract type name
- XML documentation
- Method names and parameter types
- Handled and emitted synapse types
- Owning module

Future natural-language programming follows this path:

```text
"Ask Google Calendar for tomorrow's events"
                      ↓
derived vector search over the generated catalog
                      ↓
DigitalBrain.Google.ICalendar
                      ↓
exact typed neuron proxy
```

Vector search may rank candidates. It may never execute an invented type or bypass exact catalog resolution.

## 3. Hard deletion manifest

Delete these concepts without shims:

```text
ModelTier
ModelProviders
IModelCompletionService
Neuron.AskModelAsync
BrainService.WithModel
ModelDescriptor
ModelCatalog
ProviderFactory
AddDigitalBrainModels
AddAIModule
ChatModelNeuron
IChatModel
ScriptedModelCompletion
Models.feature
the duplicate BrainClient interface
the Probe module/template
the unused connection lifecycle scaffold
IAnswer
```

Delete or rewrite every test, sample, host, public API baseline entry, and documentation page whose only purpose is to preserve those concepts.

Delete the superseded planning stack after this file becomes canonical:

```text
ARCHITECTURE-REVIEW.md
PLAN.md
GOAL.md
```

Git history is the archive. Contradictory live plans are not documentation.

## 4. Implementation plan

### Task 1: Prove the rejected architecture is gone

**Files:**

- Create: `tests/DigitalBrain.Tests/ArchitectureCutContracts.cs`
- Delete: `tests/DigitalBrain.Tests/ProviderAdapterContracts.cs`
- Delete: `tests/DigitalBrain.Simulations/Models.feature`

**Interfaces:**

- Consumes: compiled framework assemblies and the repository project graph
- Produces: an executable deletion gate for forbidden types, methods, package references, and registration names

- [ ] Write a test asserting Kernel exposes no method containing `Model`, no framework assembly defines `ModelTier`, and Kernel reaches no AI SDK.
- [ ] Write a repository search test rejecting the exact legacy identifiers in production `.cs` and `.csproj` files.
- [ ] Run `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`; verify it fails against the existing tier architecture.
- [ ] Perform the deletion slice.
- [ ] Run the owning tests and the root gate.

### Task 2: Replace the provisional module descriptor

**Files:**

- Modify: `src/DigitalBrain.Abstractions/IModule.cs`
- Delete: `src/DigitalBrain.Kernel/ModuleComposition.cs`
- Delete: `src/DigitalBrain.Kernel/ModuleWiring.cs`
- Modify: `src/DigitalBrain.SourceGeneration/DispatchManifestGenerator.cs`
- Modify: `src/DigitalBrain.Kernel/DigitalBrainSiloBuilderExtensions.cs`
- Modify: `tests/DigitalBrain.Tests/ModuleContracts.cs`

**Interfaces:**

- Consumes: canonical `IModule` marker types and referenced assemblies at compilation
- Produces: generated `AddDigitalBrain()` composition with the available module catalog

- [ ] Write a generated-catalog test proving the test assembly sees `AIModule` from its project reference.
- [ ] Verify the test fails because no generated module catalog exists.
- [ ] Reduce `IModule` to a marker.
- [ ] Generate the `AddDigitalBrain()` extension in the consuming compilation.
- [ ] Move Kernel’s fixed runtime setup behind the generated extension.
- [ ] Validate AppHost-selected module names against generated available module names at startup.
- [ ] Run generator tests, the owning test project, and the root gate.

### Task 3: Build typed AI contracts and runtime

**Files:**

- Replace: `modules/DigitalBrain.Modules.AI.Contracts/IChatModel.cs`
- Create: `modules/DigitalBrain.Modules.AI.Contracts/ILLM.cs`
- Create: `modules/DigitalBrain.Modules.AI.Contracts/IAgent.cs`
- Create: `modules/DigitalBrain.Modules.AI.Contracts/IGroupChat.cs`
- Create: `modules/DigitalBrain.Modules.AI.Contracts/Ollama/ILlama32.cs`
- Create: `modules/DigitalBrain.Modules.AI.Contracts/OpenAI/IGpt56.cs`
- Replace: `modules/DigitalBrain.Modules.AI/AIModule.cs`
- Delete: `modules/DigitalBrain.Modules.AI/ModelBinding.cs`
- Delete: `modules/DigitalBrain.Modules.AI/ModelConfiguration.cs`
- Create: `modules/DigitalBrain.Modules.AI/LLM.cs`
- Create: `modules/DigitalBrain.Modules.AI/LlmAttribute.cs`
- Create: `modules/DigitalBrain.Modules.AI/Ollama/Llama32.cs`
- Create: `modules/DigitalBrain.Modules.AI/OpenAI/Gpt56.cs`
- Modify: both AI project files

**Interfaces:**

- Consumes: `INeuron`, Kernel `Neuron`, and provider `IChatClient` implementations
- Produces: `ILLM`, `ILlama32`, `IGpt56`, `IAgent`, `IGroupChat`, `LLM`, and `[Llm<TModel>]`

- [ ] Write a test that constructs `Llama32` from an `IChatClient` keyed by `typeof(Llama32)`.
- [ ] Verify it fails because the typed model and key attribute do not exist.
- [ ] Implement the contracts and base `LLM`.
- [ ] Implement convention-driven OpenAI and Ollama client registration keyed by the concrete model type.
- [ ] Add an architecture test rejecting `IChatClient` constructor injection outside concrete `LLM` subclasses.
- [ ] Run AI tests, package guards, and the root gate.

### Task 4: Give AI its own Aspire hosting package

**Files:**

- Create: `modules/DigitalBrain.Modules.AI.Aspire.Hosting/DigitalBrain.Modules.AI.Aspire.Hosting.csproj`
- Create: `modules/DigitalBrain.Modules.AI.Aspire.Hosting/AIHostingExtensions.cs`
- Modify: `src/DigitalBrain.Aspire.Hosting/BrainHosting.cs`
- Modify: `src/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `Directory.Packages.props`
- Modify: `DigitalBrain.slnx`

**Interfaces:**

- Consumes: `BrainService`, `BrainModuleBuilder<AIModule>`, concrete model types, Aspire OpenAI, and Aspire Ollama resources
- Produces: `brain.AddModule<AIModule>(ai => ai.WithLlm<TModel>())`

- [ ] Write AppHost model tests for one module declaration, duplicate rejection, shared OpenAI parameter, Markdown description, and no Ollama secret.
- [ ] Verify those tests fail against `WithModel`.
- [ ] Implement generic module selection in core Aspire hosting.
- [ ] Implement AI’s `WithLlm<TModel>()` convention and resources.
- [ ] Project only parameter/resource expressions into the silo.
- [ ] Prove publish output contains no secret literal.
- [ ] Run hosting tests, publish-manifest tests, and the root gate.

### Task 5: Remove the duplicate client and model test path

**Files:**

- Delete: `src/DigitalBrain.Client/BrainClient.cs`
- Modify: `src/DigitalBrain.Client/DigitalBrainClient.cs`
- Modify: `src/DigitalBrain.Aspire/BrainClientIntegration.cs`
- Modify: `src/DigitalBrain.Testing/Simulation.cs`
- Modify: `src/DigitalBrain.Testing/SimulationCluster.cs`
- Modify: `src/DigitalBrain.Testing/NeuronSteps.cs`
- Delete: `src/DigitalBrain.Testing/ScriptedModel.cs`
- Delete: `src/DigitalBrain.Testing/ScriptedModelCompletion.cs`
- Modify: `hosts/DigitalBrain.ProbeHost/Neurons.cs`
- Modify: `hosts/DigitalBrain.ProbeHost/Program.cs`
- Delete: `tests/DigitalBrain.Simulations/ThinkerNeuron.cs`

**Interfaces:**

- Consumes: owner-bound Orleans client and session neuron
- Produces: one `DigitalBrainClient` programming interface

- [ ] Change client contract tests to require one public client type.
- [ ] Verify they fail while `BrainClient` exists.
- [ ] Move still-consumed observation support behind `DigitalBrainClient` or testing-only helpers.
- [ ] Migrate hosts, samples, and simulations.
- [ ] Delete the tier-driven scripted model path.
- [ ] Run simulations, host tests, and the root gate.

### Task 6: Delete provisional modules and stale documents

**Files:**

- Delete: `modules/DigitalBrain.Modules.Probe.Contracts/**`
- Delete: `modules/DigitalBrain.Modules.Probe/**`
- Delete: `src/DigitalBrain.Abstractions/ConnectionHealth.cs`
- Delete: `src/DigitalBrain.Kernel/ConnectionLifecycle.cs`
- Delete: `tests/DigitalBrain.Tests/ConnectionHealthContracts.cs`
- Delete: `ARCHITECTURE-REVIEW.md`
- Delete: `PLAN.md`
- Delete: `GOAL.md`
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `website/architecture.md`
- Modify: `website/index.md`
- Modify: `website/quickstart.md`
- Modify: `website/status.md`
- Modify: `website/packages/abstractions.md`
- Modify: `website/packages/aspire-hosting.md`
- Modify: `website/packages/client.md`
- Modify: `website/packages/kernel.md`
- Modify: public API baseline files

**Interfaces:**

- Consumes: the refined architecture in this file
- Produces: one live plan and documentation matching the compiled public surface

- [ ] Remove no-consumer scaffolding and its self-referential tests.
- [ ] Point `CLAUDE.md` to this file as the plan of record.
- [ ] Rewrite the README quickstart around AppHost module selection and `silo.AddDigitalBrain()`.
- [ ] Remove all website claims about tiers, `AskModelAsync`, and `BrainClient`.
- [ ] Regenerate or edit public API baselines to the compiled surface.
- [ ] Run `node tools/render-specification.mjs`.
- [ ] Run `node --test tests/*.test.mjs` from `website/`.
- [ ] Run the root gate.

## 5. Acceptance gates

The hard cut is complete only when all commands are fresh and green:

```powershell
rg -n "ModelTier|ModelProviders|IModelCompletionService|AskModelAsync|WithModel\(|AddAIModule|AddDigitalBrainModels|ChatModelNeuron|class BrainClient" src modules hosts samples tests website
```

Expected: no matches.

```powershell
dotnet test --logger "console;verbosity=minimal"
```

Expected: zero failures and zero skips.

```powershell
Set-Location website
node tools/render-specification.mjs
node --test tests/*.test.mjs
```

Expected: rendering succeeds and all website tests pass.

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; only intentional architecture changes before commit and a clean tree after commit.

## 6. Deferred in dependency order

After the AI foundation is proven:

1. Complete owner-safe client scripting and the proposal/approval/install/rollback rail.
2. Generate the canonical neuron catalog from public contracts and method/synapse vocabulary.
3. Add semantic/vector discovery as a disposable index over that catalog.
4. Add `DigitalBrain.Google` with typed `IGmail` and `ICalendar` neurons over official MCP.
5. Add `DigitalBrain.Salesforce` with typed `ISalesforce` over official MCP.
6. Add `DigitalBrain.Flutter` containing only Flutter neurons and its contract drift guard.
7. Design `DigitalBrain.Memory` separately around `IMemory`; do not infer its architecture from AI.

No deferred item justifies retaining a rejected abstraction today.
