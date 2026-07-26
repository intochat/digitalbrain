# DigitalBrain hosting and testing design: AppHost evidence

This companion to the [hosting and testing design index](./2026-07-24-digitalbrain-hosting-and-testing-design.md)
owns sections 14–19.

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

## 17. Must not return

These public paths stay deleted (not wrapped):

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
- compatibility shims whose only consumer is a retired surface.

Content strings, diagnostic prose, local logical-owner labels, and runtime resource/instance names
remain valid. Coordination identities must be typed, generated, or owned once at the external
boundary.

---

## 18. External author shape

Quickstart is the third-party-shaped module pattern:

1. Contracts package: neuron interfaces and synapses only.
2. Runtime package: module marker and neuron implementations.
3. Aspire: `AddDigitalBrain("quickstart")` plus explicit `AddModule`.
4. Compiled host references the runtime and generated catalog.
5. L0 package/alias/capsule surface; L1 only `DigitalBrain.Testing` for command, fact, journal, and
   hosting-silo restart.
6. No manual localhost silo, raw `NeuronId`, raw grain factory, empty hosting package, or test-only
   static probe.

Time's built surface is `ICountdown` only. Calendar/`IReminder` stay designed until semantics are
approved. AI, Tasks, Salesforce, Google, and Flutter deepen the same authoring and testing pattern
without moving vocabulary into the kernel.

---

## 19. Non-goals

- No central brain aggregate or general supervisory control plane.
- No generic durability-provider abstraction before a second complete provider exists.
- No generic runtime module loader or assembly scanning.
- No custom assertion framework.
- No replacement for xUnit lifecycle abstractions.
- No parallel L1 methods while clock and topology are assembly-shared.
- No substitution of internal neurons or durability mechanics.
- No Behavior runtime, `IBehavior`, or `IBehaviorTest` as part of hosting/testing.
- No broad Time/calendar product surface beyond `ICountdown`.
- No compatibility layer for the retired Simulation/Scenario API.

Foundation status: hosting, compiled modules, L1/L2 testing, Quickstart, and `ICountdown` are in
tree. Behavior install rail and calendar Time remain designed/unbuilt.

**Honesty footnote (2026-07-25, residual):** “L2 testing is in tree” means the fixture surface and
TestingAppHost silo Healthy proof exist. It does **not** mean product AppHost OS surface
(`WithUiEdge` / `WithFlutterHost` → `digitalbrain-ui` / Flutter host) has a green L2 readiness or
that live `aspire start` / `aspire run` topology Healthy is proven. Module-owned Flutter hosting is
Built as projection API + L0 pins; live product topology Healthy remains residual until quoted.
Do not claim green Aspire product topology from this design doc alone.
