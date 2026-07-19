---
title: Quickstart
---

# Quickstart

A neuron that answers a synapse, running on a real Orleans silo, in five minutes.

::: warning Nothing is published yet
DigitalBrain packages are not on NuGet. Until they are, build the packages locally with
`./eng/pack.ps1` and point your project at `artifacts/packages` as a local feed — that is exactly what
the samples in `samples/` do, and what CI proves against an empty package cache.
:::

## 1. Reference the framework

This walkthrough hosts a silo *and* talks to it from the same process, so it needs the runtime, the
client, and the development journal store:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="DigitalBrain.Abstractions" Version="0.1.0-alpha.1" />
  <PackageReference Include="DigitalBrain.Client" Version="0.1.0-alpha.1" />
  <PackageReference Include="DigitalBrain.DevTools" Version="0.1.0-alpha.1" />
  <PackageReference Include="DigitalBrain.Kernel" Version="0.1.0-alpha.1" />
  <PackageReference Include="Microsoft.Orleans.Server" Version="10.2.2-rc.2" />
</ItemGroup>
```

`ORLEANSEXP005` must be suppressed because journaling is still an experimental Orleans API — see
[Status](/status).

In a real deployment these split. The silo process references `DigitalBrain.Kernel`; a service that
only *talks to* a brain references the [`DigitalBrain`](/packages/metapackage) metapackage, which
deliberately excludes the kernel so provider SDKs and credentials never reach the client side.

## 2. Declare a synapse and a neuron

A synapse is an immutable typed record. A neuron declares what it handles and what it emits, and the
compiler-checked interfaces are what the source generator turns into a dispatch manifest.

```csharp
using Orleans;

namespace DigitalBrain.Quickstart;

[GenerateSerializer]
[Alias("quickstart.hello")]
internal sealed record Hello : Synapse;

[GenerateSerializer]
[Alias("quickstart.greeted")]
internal sealed record Greeted : Synapse;

internal sealed class Greeter : Neuron, IHandle<Hello>, IEmit<Greeted>
{
    public Task HandleAsync(Hello synapse, CancellationToken cancellationToken) => ReplyAsync(new Greeted());
}
```

`Neuron` is an Orleans journaled grain. `ReplyAsync` addresses whoever sent the synapse being handled,
and the reply is written to this neuron's outgoing journal in the same durable turn as the handling
itself — either both survive a crash or neither does.

## 3. Host a silo

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

`AddDigitalBrain()` is the single wiring call: journaling, the owner boundary, placement, and dispatch.
`AddDevelopmentJournalStorage()` comes from `DigitalBrain.DevTools` and keeps journals in memory. It
exists so a first run needs no storage account. A production silo calls
`AddDigitalBrainJournalStorage(configuration)` instead, and **refuses to start** without a durable
`journal` connection string — a neuron's journals are its durability, so a silo that would silently
forget them is a silo that should not boot.

## 4. Fire a synapse

```csharp
var brain = new BrainClient(host.Services.GetRequiredService<IGrainFactory>(), new OwnerId("quickstart"));

await brain.FireAsync(nameof(Greeter), "first", new Hello());

var fired = await brain.Session.ReadJournalAsync(JournalKind.Outgoing);

Console.WriteLine($"the session durably recorded {fired.Count} fired synapse(s)");
```

Every client acts as an owner. `BrainClient` fires through an owner-bound session neuron, so the
session's own outgoing journal is a durable record of what that owner asked for, and an attempt to
address another owner's neuron is refused rather than quietly routed.

## 5. Run it

```powershell
dotnet run
```

The complete, compiling version of this walkthrough is `samples/DigitalBrain.Quickstart`. It is not a
snippet in a document that may have rotted: CI restores, builds and runs it from an empty package
cache on every change, so if this page stops working the build goes red.

## What to read next

- [Concepts](/concepts) — what neurons, synapses and simulations actually mean.
- [Specification](/specification) — the behaviours the framework guarantees, as executable scenarios.
- [Packages](/packages/) — which package to reference, and the security boundary between them.
- `samples/DigitalBrain.Multiagent` — several neurons broadcasting to each other.

## Known limitations

A client can fire and read journals, but cannot yet *observe* a brain: there is no timeline stream, so
the samples poll a journal rather than subscribing. See [Status](/status) for the full list of open
debts before you build on this.
