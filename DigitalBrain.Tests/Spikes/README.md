# Journal Format Spike - Finding

Date: 2026-07-06

## Current decision

Use Orleans' documented JSON Lines journal format for the Aspire-hosted kernel:

- Aspire continues to model and inject the Orleans storage resources. The AppHost owns the Azure Storage/Azurite resources, including the journal blob container.
- The kernel continues to use Orleans' journal storage provider (`AddAzureBlobJournalStorage(...)`) instead of a custom storage adapter.
- The journal format is selected with `UseJsonJournalFormat(JournalJson.Configure)`.
- `JournalJson` supplies polymorphic `Synapse` metadata through Orleans' `JsonJournalOptions.AddTypeInfoResolver(...)` hook.

This replaces the previous `JournalFormatKey = "orleans-binary"` decision. The native binary path was a useful spike, but it is not the proper long-term default for this codebase.

## Why CI failed

The GitHub Actions run from 2026-07-06 failed in `JournalFormatSpikeTests.Orleans_Native_Format_Round_Trips_A_Synapse_Without_JournalJsonContext`. After forced reactivation, the replayed timeline contained only `NeuronActivated` entries and did not contain the fired `DemoMessageSynapse("spike-payload")`.

The slow part came from the old spike's `IManagementGrain.ForceActivationCollection(...)` plus a 40-second polling window. At the time of failure, the deploy workflow also ran that spike inside the broad `dotnet test Brain.slnx -c Release -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E"` step, so the failure made an already-heavy test phase wait instead of failing quickly. The workflow now uses `-p:SkipDeployBuild=true --filter "FullyQualifiedName!~E2E&Category!=cluster"`, which keeps the fast default loop out of real Orleans cluster specs.

The actual bug was not only the `orleans-binary` format. Proper replay testing with `Orleans.TestingHost.InProcessTestCluster.DeactivateAsync(...)` showed that `Neuron.OnActivateAsync` was calling `FireAsync(new NeuronActivated(Self))` during activation. `FireAsync` self-delivers and dispatches while the activation lifecycle is rebuilding durable state, which left the replayed outgoing timeline with activation markers but without the original payload. A timing rerun also showed that even direct durable activation writes can race journal replay. The Orleans-compatible fix is to avoid durable journal writes during activation for real Orleans journaled state; prototype in-memory journals can opt into activation markers separately.

## Testing best practice

The journal spike and self-evolution durability tests are real Orleans runtime tests, so they still use a test cluster. Per the current Orleans testing guidance, they share one `InProcessTestCluster` through an xUnit collection fixture instead of starting a cluster per test. The fixture starts one silo because these specs validate journal serialization and activation replay, not multi-silo placement.

`DigitalBrain.Tests/TestSupport/OrleansJournalClusterFixture.cs` configures the shared silo with:

```csharp
siloBuilder
    .AddJournalStorage()
    .UseJsonJournalFormat(JournalJson.Configure)
    .ConfigureServices(services =>
    {
        services.AddScoped<NeuronJournals>();
        services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
        services.AddSingleton<ISelfEvolutionApplyHandler, DurableRecordingApplyHandler>();
    });
```

`VolatileJournalStorageProvider` is Orleans' byte-sequence-backed in-process test storage. It avoids Azurite, Aspire AppHost startup, and Azure Storage containers while still exercising journal format encode/decode and activation replay. This is intentionally not `NeuronTestSiloConfigurator`'s `InMemoryDurableList<T>` fake, which bypasses serialization entirely.

The tests are tagged `[Trait("Category", "cluster")]`. They are deliberate validation, not part of the default edit loop.

## What the spike tests now

The journal format spike:

1. Fires `DemoMessageSynapse("spike-payload")` into a real neuron grain.
2. Captures a test-only activation instance id.
3. Calls `InProcessTestCluster.DeactivateAsync(...)`, the Orleans testing-host API that deactivates the current activation and waits for completion.
4. Confirms the next call creates a new activation.
5. Confirms the original `DemoMessageSynapse` is replayed from the JSON Lines journal.

The self-evolution durability tests use the same real Orleans journaling configuration to verify incoming/outgoing `Synapse` replay across `DeactivateAsync`, including proposal and decision state. Each test uses a unique grain key so shared cluster reuse does not hide state coupling.

## Evidence

Focused local verification after the shared fixture change:

```text
dotnet test DigitalBrain.Tests\DigitalBrain.Tests.csproj -c Release --no-build -p:SkipFlutterBuild=true -p:SkipDeployBuild=true --filter "FullyQualifiedName~JournalFormatSpikeTests|FullyQualifiedName~SelfEvolutionDurabilityTests"
Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 246 ms
```
