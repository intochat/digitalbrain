# Architecture

DigitalBrain is a small durable neuron kernel plus independently shipped domain modules.

## Kernel

`DigitalBrain.Kernel.Neuron` handles incoming and outgoing synapses, journals both directions,
enforces owner and delivery invariants, and recovers durable operational state. It has no AI,
provider, UI, integration, or memory concepts.

*Status: built and covered by contract, simulation, and hosted tests.*

## Modules

Modules own vocabulary and implementation:

```text
DigitalBrain.Modules.<Name>.Contracts
DigitalBrain.Modules.<Name>
DigitalBrain.Modules.<Name>.Aspire.Hosting
```

Physical package names carry packaging detail. Public namespaces carry meaning:

```text
DigitalBrain.AI.Ollama.ILlama32
DigitalBrain.AI.OpenAI.IGpt56
DigitalBrain.Google.ICalendar
DigitalBrain.Salesforce.ISalesforce
```

Package reference means available. AppHost selection means active. The silo catalog and composition
are generated at compile time; runtime assembly scanning is not part of the framework.

*Status: the module marker, generated catalog, activation validation, and AI module are built.*

## AppHost and silo

Infrastructure is explicit in AppHost:

```csharp
var brain = builder.AddBrain("brain")
    .WithDevelopmentStores();

brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain);
```

The silo stays boring:

```csharp
builder.UseOrleans(silo => silo.AddDigitalBrain());
```

AppHost projects selected module identities and provider resource expressions. The generated silo
composition activates only those modules and fails if AppHost selected a module absent from the
silo's compiled catalog.

*Status: built. The production AppHost currently selects local `Llama32`.*

## AI

Concrete model types are the identity:

```csharp
public sealed class Llama32(
    [Llm<Llama32>] IChatClient chatClient)
    : LLM(chatClient), ILlama32;
```

`LLM` is a neuron. Provider `IChatClient` instances are keyed by the concrete neuron type. The AI
runtime owns Microsoft.Extensions.AI, OpenAI, and OllamaSharp. AI's Aspire package owns Ollama and
OpenAI resources, model resources, parameters, and projection into the silo.

There is no routing tier, balancing layer, provider enum, model descriptor, or fallback catalog.
Namespaces and type names are the architecture.

*Status: typed Ollama and OpenAI neurons plus provider-owned Aspire integration are built. Agent and
group-chat implementations are next.*

## Client programming

`DigitalBrainClient` is the only public client facade:

```csharp
var brain = DigitalBrainClient.Connect(grains, "acme");
await brain.SendAsync<IAnalyst>(
    "incident-42",
    new SummaryRequested("Summarize the incident."));
```

Owner identity is ambient to the client. `SendAsync<TNeuron>()` enters through the owner-bound
session and derives the target neuron type from the interface name. `EmitAsync()` broadcasts a fact
through the same deliberate entry point. The client does not return raw neuron proxies.

Inside the brain, one neuron calls another typed capability directly:

```csharp
public sealed class Analyst(ILlama32 llama) : Neuron, IAnalyst, IHandle<SummaryRequested>
{
    public Task HandleAsync(SummaryRequested request, CancellationToken cancellationToken)
        => llama.AskAsync(request.Prompt);
}
```

Authentication remains an edge responsibility; an Orleans client is a trusted cluster peer.

Future runtime behavior installation must pass through a human-approved proposal with a journaled,
reversible decision. Generated code does not receive a path around that rail.

*Status: the typed facade exists. Runtime behavior installation is designed and not yet built.*

## Future modules

Google, Salesforce, Flutter, and Memory will repeat the same package and hosting pattern. Each module
owns its typed neurons, dependencies, authentication, and Aspire resources. Google and Salesforce do
not depend on AI; application agents compose their typed neurons with a concrete LLM neuron.

A future semantic index may translate natural language such as “Google Calendar” to
`DigitalBrain.Google.ICalendar`. The generated typed catalog remains the source of truth.

*Status: designed and not yet built. Memory is deliberately outside the current implementation.*

## Rejected

- AI logic in Kernel
- provider routing tiers and balancing
- public model metadata definitions
- runtime module scanning or loading
- raw MCP clients crossing module boundaries
- a second client facade
- compatibility shims for the rejected architecture

The decision record and ordered work are in
[`REFINED-ARCHITECTURE-AND-NEXT-STEPS.md`](https://github.com/digitalbraintech/brain/blob/master/REFINED-ARCHITECTURE-AND-NEXT-STEPS.md).
