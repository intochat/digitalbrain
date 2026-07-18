# Modules

A module adds one or more neuron kinds and their supporting services without creating another execution runtime.

## Implemented composition

Modules use **explicit host composition**. `Brain.Kernel.Host/Program.cs` registers the kernel with Workspace kinds and calls the hosting extensions for AI, Web, Connections, Google, Salesforce, and Behaviors.

There is no runtime module marketplace or manifest loader. A module is present because the host references and registers it.

## Current module ingredients

Repository modules commonly contain:

- An `INeuronContract` typed façade when clients need one.
- An `INeuronKind` implementation with accepted contract names.
- Commands, results, and deterministic domain logic.
- Dependency-injection registration for required services.
- Connector code when the capability reaches an external system.
- Conformance coverage for shared invariants.

The repository does not enforce a five-package anatomy. Contracts, runtime, connector, UI, and hosting packages remain a possible future packaging convention, not current fact.

## Registration

Workspace kinds are registered with the kernel:

```csharp
silo.AddBrainKernel(new ChatKind(), new WindowKind(), new FeedKind());
```

Other modules expose hosting extensions:

```csharp
silo.AddBrainAi(configuration);
silo.AddBrainWeb();
silo.AddBrainConnections(configuration);
silo.AddBrainGoogle(configuration);
silo.AddBrainSalesforce(configuration);
silo.AddBrainBehaviors();
```

## Trust boundary

All current modules are first-party and run in process. Semantic compatibility rules, manifests, dynamic loading, and community-code isolation are **Decisions** still to be made.

Use the [first module tutorial](/build/first-module) to extend the implemented path.
