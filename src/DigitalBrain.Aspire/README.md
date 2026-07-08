# DigitalBrain.Aspire

Aspire hosting package for the DigitalBrain **Kernel** — the minimal Orleans substrate required for the system to function.

## Layering

- **Core** (DigitalBrain.Core): stable abstractions — INeuron, Synapse (with SynapseId/CausationId/CorrelationId), dual journals, Checkpoint/Branch/Restore, UiSurface/RfwCard as first-class synapses, and universal runtime messages. This is the non-negotiable center. Everything is expressed through neurons and synapses.
- **Pack contracts** (DigitalBrain.Pack.Contracts): connector configuration and UI kit contracts used by in-repo experiences. Marketplace distribution is intentionally not part of the active product path.
- **Kernel** (this package + DigitalBrain.Kernel base): the Aspire-orchestrated Orleans kernel runtime + built-in INO, automation, connector, task, status, journal, and UI-surface features. AddDigitalBrain wires clustering, durable journals (blobs), LLM, etc.
- **INO / integrations / domain features**: wired as in-repo capabilities and automations for now. INO can stage automations directly; distribution is out of scope until there is a real product need.

The kernel starts 3 instances by default (see DigitalBrainOptions.KernelReplicas) so the substrate remains available while packs are embodied, behaviors updated, or resources restarted.

## Usage

```csharp
var ctx = builder.AddDigitalBrain("digitalbrain", options =>
{
    options.WithLLM<Qwen25Coder1_5B>().AsBalanced();
    // options.WithEmbedding<YourEmbeddingModel>();
    // options.WithVoice2Text<YourVoiceModel>();
    // options.WithVectorDatabase(DigitalBrainProviderIds.Qdrant, "documents");
    // KernelReplicas defaults to 3 for HA during updates/self-improvement
});

// Wire the kernel resource using the context — this provides the cool built-in kernel features
// (journals with causation, UI surfaces, tasks, self-status, 3-replica HA) out of the box.
var kernel = builder.AddProject<Projects.DigitalBrain_Kernel>("kernel");
ctx.WireKernelSilo(kernel);

var clientApp = builder.AddProject<Projects.YourClient>("client")
    .WithReference(ctx.OrleansClient);
```

See DigitalBrain.AppHost/AppHost.cs for the canonical example (Flutter UI client, Telegram transport, and MCP wired as consumers of the kernel surfaces and synapses).

The returned context + resource enable future With* extensions while keeping the core model pure (UI events and surface updates remain Synapses delivered to neurons).

## Built-in kernel features (always present substrate)
- Dual durable journals + full causation on every Fire/Deliver.
- Checkpoint / Branch / Restore (time-travel and safe simulation inside the kernel).
- KernelTask with journal-derived progress.
- SystemStatus + self-diagnosis (MCP to own Aspire + checkpoint-based simulation).
- HomeFeedBus + RfwCard/UiSurface streaming + bidirectional UiGateway (EngageUiSession) for dynamic UI.

Distribution is deliberately removed from the active architecture. Keep INO, Gmail, Salesforce, automations, and UI surfaces in the repo until marketplace distribution has a concrete design and tests again.
