---
title: Quickstart
---

# Quickstart

The checked-in quickstart runs a typed neuron on a local Orleans silo.

::: warning Nothing is published yet
Produce the packages locally with `dotnet pack DigitalBrain.slnx -o artifacts/packages`. The sample
under `samples/DigitalBrain.Quickstart` restores only from `artifacts/packages` and NuGet.org.
:::

## Declare a typed neuron

```csharp
public partial interface IGreeter : INeuron
{
    [Alias(nameof(Greet))]
    Task<string> Greet(string name);
}

[GenerateSerializer]
[Alias("quickstart.say-hello")]
internal sealed record SayHello : Synapse;

[GenerateSerializer]
[Alias("quickstart.greeted")]
internal sealed record Greeted : Synapse;

internal sealed class Greeter : Neuron, IGreeter, IHandle<SayHello>, IEmit<Greeted>
{
    public Task<string> Greet(string name)
        => Task.FromResult($"Hello, {name}!");

    public Task HandleAsync(SayHello synapse, CancellationToken cancellationToken)
        => EmitAsync(new Greeted());
}
```

The partial interface names the neuron capability; its generated identity is the fully qualified
interface name. Capability methods use ordinary non-`Async` domain verbs and
`[Alias(nameof(Method))]`. Explicit string aliases remain on durable synapse and state records:
`SayHello` is an incoming fact and `Greeted` is an emitted fact.

## Start the silo

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleans(silo => silo
    .UseLocalhostClustering()
    .UseInMemoryReminderService()
    .AddDigitalBrain()
    .AddDevelopmentJournalStorage());

using var host = builder.Build();
await host.StartAsync();
```

`AddDigitalBrain()` composes the kernel and all AppHost-selected modules known to this silo.
`AddDevelopmentJournalStorage()` is an in-memory development store.

## Send through the owner-bound client

```csharp
var grains = host.Services.GetRequiredService<IGrainFactory>();
var brain = DigitalBrainClient.Connect(grains, "quickstart");

await brain.SendAsync<IGreeter>("first", new SayHello());
var greeting = await brain.Get<IGreeter>("first").Greet("Ada");
```

`IDigitalBrain` is the owner-scoped client contract and `DigitalBrainClient` is its implementation.
`SendAsync<TNeuron>()` derives neuron identity from the interface type and routes through the session
neuron, the kernel's deliberate external entry point. There is no concrete brain neuron or
addressable root neuron; the hosting brain and the owner-bound client are separate concepts.

## Add AI through AppHost

The production distributed AppHost configures local Ollama without changing the silo code:

```csharp
var brain = builder.AddDigitalBrain("brain");

brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain);
```

`AddDigitalBrain("brain")` is one-call durable hosting: it owns the Azure Storage resource used for
Orleans clustering and reminders and for Blob-backed journals. Aspire run mode starts Azurite for
that same durable profile; a publish uses Azure Storage. The silo project remains explicit because
its compiled executable contains the generated typed module catalog. The AI module owns the Ollama
resource and projects its endpoint and model into that silo. Switching to OpenAI is a typed AppHost
choice:

```csharp
brain.AddModule<AIModule>(ai => ai.WithLlm<Gpt56>());
```

That selection creates a secret `<brain-name>-ai-openai-api-key` Aspire parameter with a direct link
to the OpenAI Platform. Do not add both `AddModule<AIModule>` calls; configure a module exactly once
and chain its models in one callback.
