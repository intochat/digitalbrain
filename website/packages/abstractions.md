---
title: DigitalBrain.Abstractions
---

# DigitalBrain.Abstractions

The leaf contract package. It defines `INeuron`, `Synapse`, `NeuronId`, owner identity, journal
contracts, handler/emitter declarations, the session entry point, and the marker `IModule`.

It references no Kernel, hosting package, or provider SDK. Module contract packages depend on this
package and remain equally provider-free.

```csharp
public interface IHandle<TSynapse>
{
    Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}

public interface IEmit<TSynapse>;
```

`OwnerId` is the tenancy identity. `NeuronId` is `(type, owner, name)`.
