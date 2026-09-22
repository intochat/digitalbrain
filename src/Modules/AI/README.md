# AI neurons

The AI module exposes callable models and durable conversational agents through
`INeuron`. Provider SDK objects and live MCP connections stay inside the runtime.
The agent interface follows the conversational actor model: an agent key owns its
configuration and history. Separate keys have separate conversations.

## Contracts

| Interface | Responsibility |
| --- | --- |
| `ILLM` | One inference, returning content or proposed tool calls without executing them |
| Provider interfaces such as `OpenAI.IGpt56Sol` | The same inference contract constrained to one catalog model |
| `Agents.IAgent` | Instructions, tools, history, responses, streaming, metadata, usage and cancellation |
| `Conversations.IConversation` | Existing lower-level conversation coordination, retained for IntoChat |
| `Media.IImageGenerator` | Image generation through the configured image provider |
| `Media.IEmbeddingModel` | Embeddings through the configured embedding provider |
| `Media.ISpeechRecognizer` | Audio transcription |
| `Media.ISpeechSynthesizer` | Text-to-speech through an explicitly registered transport |

The existing model marker catalog and `Ask(AgentRequest)` remain supported.
Existing `IAgentTurnRunner` consumers share the same bounded tool loop as the new
agent methods. Legacy `ConversationalAgent` and native SDK tool factories remain
available for existing consumers; new neuron requests do not contain SDK types.

## Configure a model profile

Profiles identify deployments, including provider/model IDs and endpoint. Keep
credentials in host configuration or secret parameters, never neuron state.

```csharp
services.Configure<AIOptions>(options =>
{
    options.Default.Profile = "coding";
    options.ModelProfiles["coding"] = new()
    {
        Provider = "OpenAI",
        Model = "your-provider-model-id",
        Capabilities = LlmCapabilities.Tools | LlmCapabilities.StructuredOutput,
        // Declare these from the deployed model's verified documentation.
        SupportsTemperature = false,
        SupportsTopP = false,
        AllowedReasoning = ["none", "low"],
    };
});
```

`ContextWindowTokens` and `MaximumOutputTokens` are optional declared limits.
Unknown capabilities/limits remain unknown. Explicit temperature/top-p settings
require declared support. Model-specific reasoning and output limits are checked
before external calls. A request cannot promote its own model capabilities.
The context-window descriptor is metadata, not a tokenizer or automatic context
compaction service; agents separately enforce a message-count history budget.

Common settings use `InferenceOptions`. Provider settings are typed:

```csharp
new InferenceOptions(
    MaxOutputTokens: 2048,
    Provider: new DigitalBrain.AI.OpenAI.OpenAIInferenceOptions(
        ParallelToolCalls: false));

new InferenceOptions(
    Reasoning: "high",
    Provider: new DigitalBrain.AI.Ollama.OllamaInferenceOptions(
        ContextWindowTokens: 6144));
```

Ollama context settings require a declared context ceiling. Its thinking setting
or reasoning effort reaches the provider without being overwritten by defaults.
Provider settings cannot be applied to another provider. Unsupported extension
keys fail; the dictionary extension surface currently accepts only OpenAI
`parallel_tool_calls`. Structured output uses `ResponseSchemaJson` and requires
declared structured-output support.

## Call a model directly

```csharp
var llm = brain.Get<ILLM>("coding"); // Named profile; "default" selects host defaults.
var descriptor = await llm.Describe();
var result = await llm.Generate(new(
    Messages: [new AiMessage("user", [new AiText("Explain this function.")])]), ct);
```

An explicit `InferenceRequest.Model` overrides the key's selection. A typed neuron
such as `brain.Get<DigitalBrain.AI.OpenAI.IGpt56Sol>("coding")` verifies that a
configured profile with that key points to its catalog model. Without a profile,
the typed neuron uses its catalog model. Unknown generic profile names fail.

`AiMessage` preserves text, image/audio references, reasoning content and structured
tool calls/results with call IDs. Current chat adapters accept images only when
vision is declared; chat audio input fails explicitly, so transcribe audio first.
Data URI media is capped at 8 MiB before decoding. `GenerateStreaming` returns typed
content updates with provider finish/usage data when supplied. Inference publishes
typed started/completed/failed signals with an operation ID.

## Use an agent

```csharp
var agent = brain.Get<DigitalBrain.AI.Agents.IAgent>("invoice-assistant");
var state = await agent.GetState(ct);
await agent.Configure(new()
{
    DisplayName = "Invoice assistant",
    Instructions = "Review invoice information and explain the next action.",
    Model = new AgentModelSelection(Profile: "coding"),
    Tools = ["lookup_invoice"],
    MaxModelCalls = 8,
    Timeout = TimeSpan.FromMinutes(2),
}, state.Revision, ct);

var answer = await agent.GetResponse("Review invoice 123.", ct);
await foreach (var delta in agent.GetResponseStream("Explain your recommendation.", ct))
{
    Console.Write(delta);
}
var history = await agent.GetHistory(ct);
```

`GetRichResponse` accepts text or a user `AiMessage`, and returns structured output,
run ID and nullable usage. `GetMetadata` reports agent purpose/routing labels;
`GetCapabilities` describes the selected model. Purpose labels do not confer tools
or model capabilities. The last run stores the definition and resolved descriptor
used for execution. `GetEventLog` returns the last 256 lifecycle/tool events.

One operation runs per agent key. Overlapping calls or mutations are rejected.
Status/history reads and `Cancel` interleave with inference. Cancellation is
cooperative; a run that has reached its commit point may complete. Disposing a
response stream cancels execution and disposes its live tool connections.
Only completed turns enter history. History is never silently truncated: exceeding
`MaxHistoryMessages` fails and requires clearing history or increasing the limit.

History and configuration survive reactivation. A run left active by a crashed
activation is marked interrupted; it is not automatically replayed because an
external tool may already have acted. Last-run state is authoritative. Signals are
live observer notifications, not durable replay. Agent notification failures do
not turn an already committed response into a failed call.

## Tools and MCP

Agents select exact registered tool names. Native tools can be registered with
`AddNativeTool` or contributed through `IAgentToolFactory`; typed neuron methods
can be wrapped explicitly in those factories. No blanket reflection exposure of
all neuron methods is enabled.

```csharp
services.AddNativeTool("lookup_invoice", sp =>
    Microsoft.Extensions.AI.AIFunctionFactory.Create(
        (string id) => sp.GetRequiredService<InvoiceReader>().Read(id), "lookup_invoice"));

services.AddMcpAgentTools("accounts", new Uri("https://your-server.example/mcp"));
// AgentDefinition.Tools = ["mcp_accounts_lookup_invoice"];
```

The MCP transport-factory overload supports host-owned authentication. A run opens
only selected servers, discovers schemas and closes clients/transports afterward.
Missing/ambiguous tools, provider error results and duplicate call IDs fail the run.
The agent records model selection and tool-start before allowing external work.
Tool calls execute sequentially and are never retried automatically. An interrupted
tool-start without a recorded result must be reconciled before retrying side effects.

## Media and stage two

See [media details](AI/Media/README.md) for inline payload limits and explicit OpenAI
speech synthesis setup. Media operations currently use configured default providers;
per-request media model selection, image editing and realtime duplex voice sessions
are not implemented. Inline bytes keep results usable until a shared artifact store
exists. Provider adapters can allocate buffers before the media output limit is checked.

Scheduling, UI callbacks, workspace operations, generated C# compilation and behavior
deployment belong to stage two. This change does not add those methods to `IAgent`.

## Verification

```powershell
dotnet test --project src/Modules/AI/Tests/Unit/DigitalBrain.Modules.AI.Tests.Unit.csproj
dotnet test --project src/Modules/AI/Tests/E2E/DigitalBrain.Modules.AI.Tests.E2E.csproj
```

Unit tests use deterministic external providers and local HTTP fixtures, including
real OpenAI/Ollama SDK request mapping. E2E starts a separate Aspire host with
disposable storage and calls a local provider fixture; Docker is required. These
tests do not certify availability or current capabilities of hosted cloud models.
# Behavior authoring composition

IntoChat composes `IAgent` with Coding drafts/checks and Behavior lifecycle tools to author single-source C# applications. AI retains ownership of models and agent runs; validation, artifacts and worker supervision live in their own modules. See [programmable behaviors](../Behavior/README.md) for configuration, command contracts, recovery and tests.
