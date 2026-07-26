# DigitalBrain hosting and testing design

**Date:** 2026-07-24
**Status:** approved design. Hosting, compiled modules, L1/L2 testing, Quickstart, and
`ICountdown` landed in tree; Behavior install rail and calendar Time remain designed/unbuilt.
Gherkin Features were later retired (2026-07-25 cut) — see architecture-aligned mass deletion and
authored `docs/specification.md`.

DigitalBrain is an AI-native operating system. It ships ready-to-use neurons and synapses; users
describe Behaviors in natural language and compose the typed vocabulary supplied by installed
modules. Hosting and testing must make that model easier to extend, not expose Orleans, Aspire
storage plumbing, reflection catalogs, or a second test-only runtime.

This design deepens the architecture in `docs/architecture.md`. It preserves its kernel, module,
Behavior, durability, testing-tier, and generated-catalog decisions while superseding two public
shapes:

1. AppHost uses `builder.AddDigitalBrain("name")`; callers no longer select a storage profile.
2. The testing product uses `DigitalBrainFixture` / `TestBrain` and
   `DigitalBrainAppHostFixture<TAppHost>` / `RunningAppHost`, not Simulation or Scenario vocabulary.

---

## 1. Decisions

1. `IDigitalBrain` remains the owner-scoped production client contract.
2. There is no central `DigitalBrain : Neuron`.
3. AppHost's `AddDigitalBrain(name)` owns one complete durable infrastructure profile.
4. Modules are selected explicitly and activated through source-generated compiled capsules.
5. Public neuron capability methods omit the `Async` suffix; infrastructure lifecycle methods keep
   normal .NET async naming.
6. Neuron method aliases use `nameof`; durable payload aliases remain explicit stable wire names.
7. L1 tests use an assembly-owned real multi-silo cluster and one method-scoped `TestBrain`.
8. One `DigitalBrainFixture` permits one active `TestBrain`; independent test assemblies may run in
   parallel.
9. Production calls enter through `TestBrain.Client : IDigitalBrain`. Test controls do not implement
   or imitate `IDigitalBrain`.
10. `TestNeuron<TNeuron>` is the closed typed neuron test surface. Raw Orleans is not public.
11. Tests receive typed observations and use their chosen assertion library. DigitalBrain does not
    ship a second assertion language.
12. Test identities are generated inside a unique method scope. Logical cross-owner tests use
    `TestOwner`, not arbitrary raw `OwnerId` values.
13. Test composition uses the same compiled module capsules as production. Raw DI and silo builder
    callbacks are not public.
14. L2 uses an exclusive `DigitalBrainAppHostFixture<TAppHost>`, a method-scoped `RunningAppHost`,
    and typed `HostedResource` handles.
15. `Behavior` remains a product term. There is no testing-framework `IBehavior` or
    `IBehaviorTest`.

---

## 2. Why there is no root DigitalBrain neuron

The word "brain" currently names three different things:

- an Aspire deployment resource such as `"mybrain"`;
- an owner-scoped production client represented by `IDigitalBrain`;
- the distributed collection of durable neurons belonging to that owner.

Turning `DigitalBrain` into a neuron would collapse those identities. Module selection is an
AppHost/compiled-manifest fact, resource health is an Aspire fact, and domain state belongs to the
individual neurons that own it. Copying those facts into one root activation would create a second
source of truth, a hot routing point, and an attractive home for unrelated responsibilities.

The existing per-owner session neuron remains the internal command and journal gateway. It is not
promoted into the product programming model. A future supervisory neuron requires a named durable
invariant and a real consumer before it may be introduced; "current brain status" alone is not such
an invariant.

`IDigitalBrain` therefore stays a local client facade:

```csharp
IDigitalBrain brain = services.GetRequiredService<IDigitalBrain>();

var greeter = brain.Get<IGreeter>();
await greeter.Greet(...);
```

`DigitalBrainClient` implements the facade over Orleans. Consumers receive `IDigitalBrain` from DI;
they do not acquire grain factories or construct neuron identifiers.

---

## 3. Neuron names and serialized aliases

DigitalBrain is asynchronous by construction. Capability verbs on neuron contracts do not repeat
that fact:

```csharp
public interface IGreeter : INeuron
{
    [Alias(nameof(Greet))]
    Task Greet(Greet command);
}
```

This rule applies to methods declared by neuron contracts. Infrastructure and test lifecycle methods
retain conventional .NET names such as `StartAsync`, `CreateBrainAsync`, `NextAsync`,
`WaitUntilHealthyAsync`, and `DisposeAsync`.

Aliases have three different compatibility roles:

- **Neuron method aliases** use `nameof(Method)` so a method and its alias cannot drift inside one
  source revision.
- **Neuron and contract type identities** are generated from fully-qualified type identity. Bare
  `nameof(T)` is insufficient because external modules may declare the same short name.
- **Persisted facts, state, and protocol payloads** keep explicit stable aliases. Renaming a CLR type
  must not silently rename durable data that an older silo wrote.

Old `*Async` neuron aliases and duplicate compatibility surfaces stay deleted rather than maintained.

---

## 4. One AppHost call owns infrastructure

The application-facing entry point is:

```csharp
var brain = builder.AddDigitalBrain("mybrain");
```

That call creates and owns the complete durable profile:

- the Orleans service model;
- one Azure Storage account resource;
- Azurite behavior for local run mode and Azure Storage for deployment;
- brain-scoped clustering and reminder tables;
- the Blob-backed neuron journal;
- state-protection material required by durable confidential module state;
- storage projection to the compiled silo only;
- readiness dependencies for every resource the silo needs;
- stable brain-scoped resource names.

Storage is an implementation detail of a brain resource. The public surface no longer contains:

- `AddBrain`;
- `WithAzureStorage`;
- `WithDevelopmentStores`;
- a storage-profile abstraction;
- a caller-provided journal, clustering store, or reminder store.

There is one complete profile because only one complete durable provider exists. Local development
uses the emulator for that same profile; it does not switch to memory stores and thereby test
different durability semantics.

`AddDigitalBrain` returns an opaque typed brain resource supporting only application composition:

```csharp
var brain = builder.AddDigitalBrain("mybrain");

brain.AddModule<QuickstartModule>();
brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());

builder.AddProject<Projects.Quickstart_Host>("host")
    .WithReference(brain);

builder.AddProject<Projects.Quickstart_Client>("client")
    .WithReference(brain.AsClient());
```

The compiled host remains explicit. A generic prebuilt silo cannot contain arbitrary third-party
module implementations without runtime loading or scanning. AppHost must therefore identify the
executable whose compiled catalog contains the selected modules. `AsClient()` remains a security
boundary and never projects storage, module secrets, or the state-protection key.

The silo continues to be deliberately boring: its generated composition performs
`AddDigitalBrain()` once and consumes the manifest projected by AppHost.

---

## 5. Compiled module capsules

Package reference means a module is available; `AddModule<TModule>` means it is selected.

Each module marker has one source-generated compiled capsule containing:

- stable module identity;
- neuron and synapse vocabulary;
- serialization registrations;
- runtime activation;
- optional module configuration projection;
- the test composition entry point.

Production AppHost, the compiled silo, and `DigitalBrain.Testing` consume that same capsule. None of
them discovers modules by scanning loaded assemblies, searching for method names, or reconstructing
type names from strings.

Both no-configuration and configured modules are supported:

```csharp
brain.AddModule<QuickstartModule>();
brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());
```

Adding the same module twice is a composition error. Selecting a module absent from the host's
compiled catalog is a startup error with the brain and module identities in the diagnostic.

The generator's own ABI may be public for compilation purposes but hidden from normal IntelliSense
and documented as compiler infrastructure. Module authors implement a marker and typed
configuration; they do not implement the capsule ABI by hand.

---

## Read by responsibility

- [Testing product and L1 boundaries](./2026-07-24-digitalbrain-hosting-and-testing-design-testing-product.md)
  contains §§6–13: tiers, fixtures, test ownership, typed test controls, observations, faults, and
  external edges.
- [AppHost evidence and author boundaries](./2026-07-24-digitalbrain-hosting-and-testing-design-apphost-evidence.md)
  contains §§14–19: L2 AppHost proof, Behavior/Gherkin boundaries, failure evidence, retired paths,
  external author shape, and non-goals.
