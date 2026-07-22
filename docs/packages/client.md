---
title: DigitalBrain.Client
---

# DigitalBrain.Client

The package exposes one facade: `DigitalBrainClient`.

```csharp
var brain = DigitalBrainClient.Connect(grains, "acme");
await brain.SendAsync<IAnalyst>("incident-42", new SummaryRequested("What changed?"));
await brain.EmitAsync(new DeploymentObserved("production"));
```

Its public programming verbs are `Connect`, `SendAsync`, and `EmitAsync`. Owner identity is ambient
and never accepted by either send method. All traffic enters through `ISessionNeuron`; the facade
does not return raw Orleans neuron proxies and does not retain disconnected callback delegates.

This is **not** an authentication boundary. An Orleans client is a trusted cluster peer. Authenticate
the user at the application edge and bind the resulting principal to the owner supplied to
`Connect`.

The package references Abstractions and Orleans client APIs, not Kernel or provider SDKs.
