---
title: Quickstart
---

# Quickstart

The checked-in quickstart runs a typed neuron on a local Orleans silo.

::: warning Nothing is published yet
Build packages with `./eng/pack.ps1`. The sample under `samples/DigitalBrain.Quickstart` restores only
from `artifacts/packages` and NuGet.org.
:::

## Declare a typed neuron

```csharp
[Alias("quickstart.greeter")]
internal interface IGreeter : INeuron;

[GenerateSerializer]
[Alias("quickstart.say-hello")]
internal sealed record SayHello : Synapse;

[GenerateSerializer]
[Alias("quickstart.greeted")]
internal sealed record Greeted : Synapse;

internal sealed class Greeter : Neuron, IGreeter, IHandle<SayHello>, IEmit<Greeted>
{
    public Task HandleAsync(SayHello synapse, CancellationToken cancellationToken)
        => EmitAsync(new Greeted());
}
```

The interface names the neuron capability. `SayHello` is its incoming fact and `Greeted` is its
emitted fact.

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
```

`DigitalBrainClient` is owner-bound. `SendAsync<TNeuron>()` derives neuron identity from the
interface type and routes through the session neuron, the kernel's deliberate external entry point.

## Add AI through AppHost

The production distributed AppHost configures local Ollama without changing the silo code:

```csharp
var brain = builder.AddBrain("brain")
    .WithDevelopmentStores();

brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain);
```

The AI module owns the Ollama resource and projects its endpoint and model into the silo. Switching
to OpenAI is a typed AppHost choice:

```csharp
brain.AddModule<AIModule>(ai => ai.WithLlm<Gpt56>());
```

That selection creates a secret `openai-api-key` Aspire parameter with a direct link to the OpenAI
Platform. Do not add both `AddModule<AIModule>` calls; configure a module exactly once and chain its
models in one callback.
