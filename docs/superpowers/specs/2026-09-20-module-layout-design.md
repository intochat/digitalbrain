# Module layout, naming and neuron-model port

Date: 2026-09-20. Status: design approved; refactor in progress.

## 1. Why

The product moved to the neuron model (`DigitalBrain.Contracts`: `INeuron`, `Signal`,
`IDigitalBrain`; `DigitalBrain.Core`: `Neuron`, `IModule`). The old
`DigitalBrain.Abstractions` model (`Command`, `CommandId`, `Accepted<T>`, `CommandId`,
`NeuronRuntime`, `INeuronInvoker`, `MethodDescriptor`, string-offset signal vocabulary) was
deleted. Only `Time`, the live slice of `Google`, and the live slice of `Flutter` compile.
`AI`, `ClickHouse`, `Coding`, `Excel`, `Memory`, `Microsoft`, `Salesforce`, `Supabase` and
`DigitalBrain.Mcp` are dead on the removed model and must be ported. This document is the
single target contract: one tree shape, one naming scheme, one port pattern, applied to every
module.

## 2. Target module tree

```
src/Modules/<Name>/
  README.md                                  # optional
  Contracts/
    DigitalBrain.Modules.<Name>.Contracts.csproj
    <Feature>/
      I<Thing>.cs                            # grain interface: public interface I<Thing> : INeuron
      Signals/
        <Signal>.cs                          # public sealed record <Signal>(...) : Signal
      <DataRecord>.cs                        # serialized data: state, queries, results, edits
  <Name>/
    DigitalBrain.Modules.<Name>.csproj
    <Name>Module.cs                          # public sealed class <Name>Module : IModule
    Configuration/
      <Name>ModuleOptions.cs                 # public, non-secret module settings
    <Feature>/
      <Thing>Neuron.cs                       # [GrainType("<thing>")] internal sealed class ... : Neuron, I<Thing>
  Aspire.Hosting/                            # only when the module owns infrastructure
    DigitalBrain.Modules.<Name>.Aspire.Hosting.csproj
    <Name>HostingExtensions.cs
  Tests/
    DigitalBrain.Modules.<Name>.Tests.Unit.csproj
    DigitalBrain.Modules.<Name>.Tests.Integration.csproj
    <Feature>/
      <Thing>Facts.cs
```

Rules:

- A feature folder groups one grain interface, its signals subfolder, and its data records.
  Signals always live under `<Feature>/Signals/`.
- Unit tests live in `Tests/<Feature>/` of the `.Tests.Unit` project. Integration tests use
  the same folder shape in `.Tests.Integration`.
- `<Name>Module` is the only public composition type. It exposes `Define(...)` returning a
  `ModuleDefinition` and `Configure(ISiloBuilder)` registering the module's services.
- A module with no HTTP surface still gets a `.Tests.Unit` project; its `.Tests.Integration`
  project exists only when the module maps endpoints or owns external infrastructure.

Reference modules: `src/Modules/Time` (clean, in-process only) and the live slice of
`src/Modules/Google` (clean, HTTP).

## 3. Project naming

| Layer | File name |
|---|---|
| Contracts | `DigitalBrain.Modules.<Name>.Contracts.csproj` |
| Implementation | `DigitalBrain.Modules.<Name>.csproj` |
| Infrastructure | `DigitalBrain.Modules.<Name>.Aspire.Hosting.csproj` |
| Unit tests | `DigitalBrain.Modules.<Name>.Tests.Unit.csproj` |
| Integration tests | `DigitalBrain.Modules.<Name>.Tests.Integration.csproj` |

Core framework projects stay `DigitalBrain.Contracts`, `DigitalBrain`, `DigitalBrain.Sdk`,
`DigitalBrain.Behavior`, `DigitalBrain.Aspire`, `DigitalBrain.Aspire.Hosting`. The kernel
tests project is `DigitalBrain.Runtime.Tests` (root lane, not a module).

All test projects import `Testing/TestRunner.props` (unit) or `Testing/Integration.Tests.props`
(integration).

## 4. Contracts pattern

```csharp
// <Feature>/I<Thing>.cs
[Alias("<thing>")]
public interface I<Thing> : INeuron
{
    Task<Result> DoWork(WorkRequest request);      // replaces a Command
    [ReadOnly] Task<State> Read();                 // read-only is annotated
}

// <Feature>/Signals/<Thing>Changed.cs
[GenerateSerializer, Alias("<name>.<thing>-changed")]
public sealed record <Thing>Changed(
    [property: Id(0)] string ThingId,
    [property: Id(1)] long Version) : Signal;      // replaces string-offset signal bodies
```

- Typed methods replace every `Command`; no `CommandId`, no `Accepted<T>`. A method returns
  the domain result directly (for example the new version) and publishes a Signal.
- One typed `Signal` record replaces each `*Signals` string constant + `*Body` record. Signals
  carry data; no `JsonSourceGeneration` context is needed because Orleans serializes them and
  the framework never parses signal JSON.
- Durable state records live in Contracts only when they cross the grain boundary
  (`Read()`); otherwise keep state internal to the implementation.

## 5. Implementation pattern

```csharp
[GrainType("<thing>")]
internal sealed class <Thing>Neuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<<Thing>State> state)
    : Neuron, I<Thing>
{
    public async Task<Result> DoWork(WorkRequest request)
    {
        // validate with ArgumentException/InvalidOperationException
        state.State = ...;
        await state.WriteStateAsync();
        await PublishAsync(new <Thing>Changed(this.GetPrimaryKeyString(), state.State.Version));
        return ...;
    }

    [ReadOnly] public Task<State> Read() => Task.FromResult(state.State.Snapshot);
}
```

- Extend `Neuron`, implement the contract interface, tag with `[GrainType]`.
- Durable state uses Orleans `[PersistentState]` directly. The old `NeuronRuntime`/
  `SnapshotEnvelope<T>`/`SaveAsync` stack is gone.
- Reads are `[ReadOnly]` and return the state or a projection.
- Publish typed Signals with `PublishAsync`. Cross-neuron interaction uses typed interface
  methods, never command routing.

## 6. Removed with the old model

Do not carry any of these into the port:

- `using DigitalBrain.Abstractions.*`
- `Command`, `CommandId`, `Accepted<T>`, `CommandRejectedException`, `SignalRejectedException`
- `NeuronRuntime`, `INeuronInvoker`, `MethodDescriptor`, `NeuronId`, `SignalId`, `SignalDelivery`
- `Signal.FromJson`, `Schedule(...)`, `Announce(...)`, `ReceiveAsync(SignalDelivery)`, `SaveAsync`
- string `*Signals` vocabularies and `*Body` records, and `*Json` source-gen contexts
- `*NativeTools` classes: they were built on the removed invoker/capability system. Module
  tools are re-introduced later against the new agent boundary, not ported mechanically.
- `Compile Remove` blocks in csproj: the port deletes the legacy files instead.

## 7. Module composition

```csharp
public sealed class <Name>Module : IModule
{
    public static ModuleDefinition Define(<Name>ModuleOptions options) => new(typeof(<Name>Module), ...);
    public void Configure(ISiloBuilder silo) { /* register clients, options, hosted services */ }
    public void Configure(IEndpointRouteBuilder endpoints) { /* map module HTTP, if any */ }
}
```

- `Define` maps public, non-secret settings to explicit configuration keys. Credentials never
  enter `Define` or a `ModuleDefinition`.
- Modules register only their own grain/services; storage providers are chosen by the host
  (production Aspire or the test harness), never by the module.
- Cross-module references are minimized. If a module needs another module's contract, reference
  that module's `.Contracts` project only.

## 8. Tests

Unit (`DigitalBrainSimulation.StartAsync`):

```csharp
await using var brain = await DigitalBrainSimulation.StartAsync(
    new() { Modules = [new <Name>Module()] }, ct);
var thing = brain.Get<I<Thing>>("id");
await using var changed = await brain.Observe<<Thing>Changed>(thing, ct);
await thing.DoWork(new(...));
Assert.Equal(1, (await changed.NextAsync(ct: ct)).Version);
```

Integration (`ModuleDigitalBrainSimulation.StartAsync`), only when the module owns HTTP:

```csharp
await using var brain = await ModuleDigitalBrainSimulation.StartAsync(
    new() { Modules = [<Name>Module.Define(new())] }, ct);
using var response = await brain.HttpClient.PostAsJsonAsync("/<name>/...", payload, ct);
```

## 9. Execution order

1. `Excel`, `Memory` (small, no cross-module deps) prove the pattern.
2. `Supabase`, `ClickHouse` (drop the removed `Flutter.Table` coupling; own their table contracts).
3. `Microsoft`, `Salesforce` (keep provider/connection policy; drop the command/agent plumbing).
4. `Coding` (Roslyn workspace as a neuron; drop command receipts).
5. `AI` (model catalog + clients as a neuron; drop descriptor/invoker/native-tool plumbing).
6. `Flutter` (delete the dead legacy slice; keep the live inbox; re-own table/chart later).
7. `DigitalBrain.Mcp` (replace `INeuronInvoker` with `IDigitalBrain.Get` + typed contracts).
8. Rename `Time`/`Google`/`Flutter` tests to `.Tests.Unit`/`.Tests.Integration`.
9. One `DigitalBrain.slnx` only; delete `DigitalBrain.Foundation.slnx` and `DigitalBrain.Testing.slnx`.
