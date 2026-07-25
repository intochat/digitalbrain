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

The migration is one intentional alpha break. Old `*Async` neuron aliases and duplicate compatibility
surfaces are deleted rather than maintained indefinitely.

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

## 6. Testing product and tier boundaries

`DigitalBrain.Testing` remains one packable, development-only testing product.

```text
L0  compiler and shape contracts
L1  real in-process multi-silo DigitalBrainFixture + method TestBrain
L2  real exclusive Aspire DigitalBrainAppHostFixture + RunningAppHost
L3  user interface surface; never the owner of domain truth
```

L0 proves package boundaries, generated capsules, aliases, public API shape, and forbidden
dependencies. L1 is the default for module semantics, durability, authorization, routing, clocks,
and reminders (including in-process silo restart via `TestNeuron.RestartHostAsync`). L2 is reserved
for AppHost composition, real resource readiness, and HTTP endpoints — not product silo restart via
AppHost resource commands.

There is no fake in-process kernel. Neurons, routing, journals, reminders, filters, and module logic
remain real at L1.

---

## 7. L1 fixture and lifecycle

A test assembly declares exactly one concrete fixture:

```csharp
[assembly: AssemblyFixture(typeof(QuickstartFixture))]

public sealed class QuickstartFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        brain.AddModule<QuickstartModule>();
    }
}
```

`DigitalBrainFixture`:

- starts a real multi-silo Orleans cluster lazily;
- activates the fixture's generated module capsules;
- owns the cluster until assembly teardown;
- admits one active `TestBrain` lease;
- resets method-scoped clock, observations, edge scripts, and faults before returning that lease;
- stops and verifies the cluster during xUnit assembly-fixture disposal.

Tests receive the concrete fixture through normal xUnit constructor injection:

```csharp
public sealed class GreetingTests(QuickstartFixture fixture)
{
    [Fact]
    public async Task Greets_customer()
    {
        await using var test = await fixture.CreateBrainAsync(
            TestContext.Current.CancellationToken);

        // ...
    }
}
```

One active `TestBrain` is an intentional correctness boundary. The shared test clock and silo
topology cannot honestly be method-local while multiple methods mutate them concurrently. The
fixture enforces serialization itself; module authors do not need collection names or
`DisableTestParallelization` attributes. Separate test assemblies own separate fixtures and may run
in parallel.

`TestBrain.DisposeAsync` disarms and verifies faults, releases observers and Orleans object
references, checks scenario-owned registries for leaks, finalizes diagnostics, and releases the
fixture lease. Disposing a method handle never stops the assembly cluster.

---

## 8. Production client versus test control

`TestBrain` does not implement `IDigitalBrain`. It exposes the real production client:

```csharp
await using TestBrain test = await fixture.CreateBrainAsync(cancellationToken);

IDigitalBrain client = test.Client;
var greeter = client.Get<IGreeter>();
await greeter.Greet(...);
```

Privileged operations remain visibly test-only:

```csharp
TestNeuron<IGreeter> greeter = test.Neuron<IGreeter>();

await test.Clock.AdvanceAsync(TimeSpan.FromMinutes(5), cancellationToken);
await greeter.RestartHostAsync(cancellationToken);
```

This separation prevents the test harness from mirroring every future client method and makes it
clear whether a line exercises a production entry point or controls the test environment.

---

## 9. Test owners and identities

Every `TestBrain` creates an opaque unique owner namespace. Its default `Client` and `Neuron<T>` use
the default logical owner within that namespace.

Authorization and collaboration tests create additional logical owners:

```csharp
TestOwner alice = test.Owner("alice");
TestOwner bob = test.Owner("bob");

await alice.Client.SendAsync<IInboxNeuron>(...);
TestNeuron<IInboxNeuron> inbox = bob.Neuron<IInboxNeuron>();
```

`TestOwner` exposes only:

- its resolved `OwnerId`;
- `Client : IDigitalBrain`;
- `Neuron<TNeuron>(name)`.

The strings `"alice"`, `"bob"`, and neuron instance names are local runtime labels, not coordination
protocols. Their actual owner identities are derived from the unique method namespace, so retries,
shuffle runs, and other test assemblies cannot address the same durable neurons accidentally.

Public L1 entry points do not accept an arbitrary `OwnerId`. Durability across a silo restart is
proved inside the same `TestBrain`, which preserves its generated identities. Exact externally
configured owner behavior belongs at L2.

---

## 10. Closed typed neuron surface

`TestNeuron<TNeuron>` is the only public neuron test handle:

```csharp
TestNeuron<IGreeter> greeter = test.Neuron<IGreeter>();

await greeter.Reference.Greet(...);

ObservedSynapse<Greeted> greeted =
    await greeter.Outgoing.NextAsync<Greeted>(cancellationToken);
```

It owns:

- `Reference : TNeuron`, the real production neuron proxy;
- the stable typed `NeuronId`;
- typed incoming and outgoing journal observations;
- the closed neuron-targeted fault controls;
- the supported host-restart operation.

The package exposes no `IGrainFactory`, raw `GrainId`, silo object, public cluster singleton, or
general "get internal service" method. Kernel mechanics requiring lower-level access remain internal
to the kernel's own tests and do not puncture the module-author API.

---

## 11. Observations, synchronization, and assertions

DigitalBrain.Testing provides evidence and synchronization, not an assertion dialect:

```csharp
ObservedSynapse<Greeted> observed =
    await greeter.Outgoing.NextAsync<Greeted>(
        TestContext.Current.CancellationToken);

Assert.Equal("Ada", observed.Synapse.Name);
Assert.Equal(greeter.Id, observed.Source);
```

An observation includes the typed synapse plus the durable metadata needed to explain it: sequence,
timestamp, correlation, sender, receiver, and journal direction.

`NextAsync<T>` and related reads observe committed journal state. They accept cancellation and
produce a bounded diagnostic on failure. They do not use `Task.Delay` as semantic synchronization,
and the public surface contains no arbitrary sleep or "settle" helper.

xUnit, Shouldly, Awesome Assertions, or another assertion library owns equality, collection,
exception, and business predicates. DigitalBrain does not grow `Expect`, `Should`, or matcher APIs
that duplicate those libraries.

---

## 12. Time, faults, and restart

Every `TestBrain` receives a controllable `TimeProvider` starting from a known instant. Advancing it
drives the test clock and the framework's due-work driver without waiting for wall time:

```csharp
await test.Clock.AdvanceAsync(TimeSpan.FromHours(1), cancellationToken);
```

The clock is reset for the next method before its `TestBrain` is returned. Fixture serialization
makes that shared cluster registration deterministic.

The initial closed fault catalog contains only faults with existing consumers:

- fail a selected neuron's journal commit after a specified number of completed writes;
- restart the silo currently hosting a selected neuron.

Journal faults are scoped to the method and target neuron. Arming returns a disposable handle, and
leaving a fault armed or never exercising it fails cleanup with a diagnostic. Silo restart is an
operation rather than sticky fault state.

There is no extensible public `FaultPoint`, arbitrary exception callback, transport interceptor, or
fake journal provider. The catalog grows only when a second real test requires another stable fault
concept.

---

## 13. Test composition and external edges

Fixture composition is production-shaped:

```csharp
protected override void Configure(DigitalBrainTestBuilder brain)
{
    brain.AddModule<QuickstartModule>();
    brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());
}
```

`DigitalBrainTestBuilder` consumes the same generated capsules as AppHost. It does not expose
`IServiceCollection`, `ISiloBuilder`, service-provider access, or an arbitrary delegate.

Only true external edges may be substituted. The initial closed set remains:

- `IChatClient`;
- southbound MCP transport;
- OAuth/secret parameter input;
- the shared test `TimeProvider`.

Typed edge controls are owned beside the module vocabulary that declares the edge:

```csharp
test.Chat<Llama32>().ReplyWith(...);
```

They are reset with the method scope. A module-specific testing package is introduced only when the
same reusable edge control has a real external consumer; otherwise a test project may keep its typed
helper local. Neurons, journals, filters, durable dictionaries, and routing are never replaced with
test doubles.

---

## 14. L2 AppHost testing

L2 names the thing it actually owns:

```csharp
[assembly: AssemblyFixture(typeof(QuickstartAppHostFixture))]

public sealed class QuickstartAppHostFixture
    : DigitalBrainAppHostFixture<Projects.Quickstart_AppHost>;
```

```csharp
await using RunningAppHost host =
    await fixture.StartAsync(TestContext.Current.CancellationToken);

HostedResource silo = host.Resource("silo");

await silo.WaitUntilHealthyAsync(TestContext.Current.CancellationToken);
using HttpClient http = silo.CreateHttpClient();
```

`DigitalBrainAppHostFixture<TAppHost>` serializes full AppHost ownership in-process.
`RunningAppHost` is method-scoped and starts one graph. `HostedResource` binds one resource name once
and exposes readiness and HTTP client creation. AppHost resource-restart commands are not part of
the public L2 product surface; silo restart proofs use L1.

The public API does not expose `DistributedApplication`, a static exclusivity semaphore, default
process-name lists, or raw resource-command strings. Cleanup tracks processes and resources created
by that specific graph and fails if they survive disposal.

L2 waits on Aspire resource notifications and declared health. It does not treat a running process
as ready, start two full AppHosts concurrently, or inflate timeouts to hide missing dependencies.

---

## 15. Behavior (and retired Gherkin)

`Behavior` is user-authored product vocabulary: one approved C# file that composes installed neurons
and synapses. It is not a neuron, fixture, test context, or test-runner interface.

Ordinary test classes may naturally be named `MorningDigestBehaviorTests`, but the framework defines
no `IBehavior`, `IBehaviorTest`, `BehaviorFixture`, or alternative Behavior runtime.

Natural-language Gherkin over `TestBrain` was part of this design and was later deleted with the
ModuleDriver / Features surface. Executable product proof today is C# on the public L1/L2 testing
APIs. A generated specification from author-facing scenarios may return only when those scenarios
exist again as durable product vocabulary — not as a second DigitalBrain implementation inside step
bindings.

---

## 16. Failure evidence

Each method maintains a bounded structured artifact as it runs. L1 evidence contains:

- fixture and selected module identities;
- test-scope and logical owner mappings;
- clock origin and advances;
- neuron identities and journal cursors;
- the ordered incoming/outgoing synapse timeline;
- armed, triggered, and disarmed faults;
- relevant silo placement and restart events;
- cleanup and leak-check results.

L2 adds:

- AppHost resource state and health;
- resolved endpoint identity;
- bounded relevant resource logs;
- restart commands and transitions;
- tracked process/resource cleanup.

Framework operation failures, cancellations, timeouts, and cleanup violations carry this artifact
without replacing the original exception. The xUnit adapter attaches the bounded artifact through
the supported xUnit v3 test-context mechanism. Evidence collection is always active; a caller does
not have to remember to enable diagnostics after a failure.

Artifacts never contain secrets, OAuth tokens, state-protection keys, or unbounded model/provider
payloads.

---

## 17. Required deletion

The migration is complete only when the old paths are gone, not wrapped:

- public `Simulation`, `Simulations`, `Scenario`, and `SimulationCluster`;
- public `ScenarioClock`, `ScenarioStages`, and Scenario-named diagnostics;
- public raw `Grains`, cluster start/stop, and serializer mutation;
- `ISimulationNeuron` and other public test-driver neuron vocabulary;
- reflection/AppDomain `NeuronCatalog`;
- process-global observation dictionaries, gates, and counters;
- thick Gherkin steps with their own string catalog or direct cluster access;
- `HostedApplication`, `HostedScenario`, raw `DistributedApplication`, and public exclusivity state;
- hard-coded process-name cleanup;
- AppHost `AddBrain`, `WithAzureStorage`, and `WithDevelopmentStores`;
- public storage-profile selection and incomplete memory durability;
- runtime module scanning and string module manifests;
- neuron contract method names and aliases ending in `Async`;
- compatibility shims whose only consumer is the pre-migration repository.

Content strings, diagnostic prose, local logical-owner labels, and runtime resource/instance names
remain valid. Coordination identities must be typed, generated, or owned once at the external
boundary.

---

## 18. First external proof

Quickstart is the first hard acceptance proof and must look like a third-party module:

1. A Contracts package contains only neuron interfaces and synapses.
2. A runtime package contains the module marker and neuron implementations.
3. Aspire composition uses `AddDigitalBrain("quickstart")` and selects the module explicitly.
4. The compiled host references the runtime and generated catalog.
5. L0 proves package boundaries, aliases, capsule generation, and the public surface.
6. L1 uses only `DigitalBrain.Testing` public APIs to prove a typed command, emitted fact, durable
   journal evidence, and survival across hosting-silo restart.
7. No manual localhost silo, raw `NeuronId`, raw grain factory, empty hosting package, or test-only
   static probe remains.

After that authoring seam is proven, the next module slice is Time's already-settled
`ICountdown` capability. Open calendar scheduling shapes remain out of scope until their semantics
are separately designed.

The same authoring and testing pattern then deepens AI, Tasks, Salesforce, and Gmail/Google without
moving their vocabulary into the kernel.

---

## 19. Non-goals

- No central brain aggregate or general supervisory control plane.
- No generic durability-provider abstraction before a second complete provider exists.
- No generic runtime module loader or assembly scanning.
- No custom assertion framework.
- No replacement for xUnit lifecycle abstractions.
- No parallel L1 methods while clock and topology are assembly-shared.
- No substitution of internal neurons or durability mechanics.
- No Behavior runtime implementation as part of the testing refactor.
- No broad Time/calendar design beyond `ICountdown`.
- No compatibility layer for the alpha Simulation/Scenario API.

---

## 20. Acceptance

The design is implemented only when:

- a third-party-shaped Quickstart module composes, hosts, and tests through the intended public
  packages;
- AppHost contains no caller-visible storage setup;
- a module test references neither Orleans nor Aspire;
- the same generated module capsule drives AppHost, silo, and L1 test composition;
- every L1 method gets a unique owner namespace, controllable clock, closed faults, and structured
  evidence;
- every L2 method owns exactly one exclusive AppHost graph and leaves no process/resource leak;
- repository searches find none of the required-deletion public APIs or reflection catalogs;
- L0, L1, L2, documentation, package, Release build, and full solution tests pass from a clean
  checkout.
