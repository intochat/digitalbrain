# Journal Format Spike — Finding

Date: 2026-07-02

## Confirmed API

Discovered by compilation probing (throwaway scratch file, deleted after use) and reflection over the
already-referenced, already-built `Microsoft.Orleans.Journaling` 10.2.1-preview.1.alpha.1 assembly — not
by browsing the NuGet cache:

- `Orleans.Journaling.JournaledStateManagerOptions.JournalFormatKey` — a plain `string` property. Setting
  it to `"orleans-binary"` selects the native/legacy binary format; this is the exact string Context7's
  `dotnet/orleans` docs (`src/Orleans.Journaling/README.md`) also document for this purpose.
- `Orleans.Journaling.Json.JsonJournalExtensions.UseJsonJournalFormat(ISiloBuilder, ...)` is the **only**
  format-selection extension method that exists in the Journaling assemblies. There is no
  `UseOrleansBinaryJournalFormat` or equivalent — selecting the native format is done purely by setting
  `JournalFormatKey`, with no companion "register these types" call of any kind.
- `Orleans.Journaling.HostingExtensions.AddJournalStorage(ISiloBuilder)` wires the durable-collection DI
  scaffolding (this is what production's `AddAzureBlobJournalStorage(...)` builds on top of).
- `Orleans.Journaling.VolatileJournalStorageProvider` / `VolatileJournalStorage` are a built-in in-memory
  `IJournalStorageProvider` whose `AppendAsync`/`ReadAsync` operate on real `ReadOnlySequence<byte>` —
  i.e. it exercises actual format encode/decode, unlike `NeuronTestSiloConfigurator`'s
  `InMemoryDurableList<T> : List<T>` fake (which never serializes anything). This is what the spike test
  uses instead of a hand-rolled `MemoryStream` adapter — Orleans already ships the equivalent, and using
  the shipped type is more representative of the real API surface than reinventing it.

Context7 (`/dotnet/orleans`, `src/Orleans.Journaling/README.md`) frames `"orleans-binary"` as the
**legacy** default — used for reading pre-existing unlabeled journal data, with the documented migration
path pointing *toward* JSON, not away from it:

> "This is useful for maintaining compatibility with existing data while planning a migration to JSON."

That is the opposite of this plan's hoped-for direction, so the spike did not stop at reading docs — it
ran the actual round trip.

## What was tested

`DigitalBrain.Tests/Spikes/JournalFormatSpikeTests.cs`, `NativeFormatSiloConfigurator`, configures a real
`TestCluster` silo with:

```csharp
siloBuilder
    .AddJournalStorage()
    .ConfigureServices(services =>
    {
        services.AddScoped<NeuronJournals>();
        services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
        services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = "orleans-binary");
    });
```

No reference to `DigitalBrain.Kernel.JournalJsonContext` and no call to `UseJsonJournalFormat` anywhere in
this file or its silo configurator.

The test:
1. Fires a `DemoMessageSynapse("spike-payload")` at a real `IDemoNeuron` grain and confirms it lands in
   `GetTimelineAsync()` — proves **serialization on write** works with zero manual type registration.
2. Forces the grain's activation to be collected (`IManagementGrain.ForceActivationCollection` + a short
   `GrainCollectionOptions.CollectionQuantum`/`CollectionAge`) and polls until a fresh `NeuronActivated`
   appears in the timeline, proving the grain actually reactivated rather than just being re-read from a
   live in-process object.
3. Re-reads the timeline on the reactivated grain and confirms `DemoMessageSynapse("spike-payload")` is
   still present — proves **deserialization on read** (reconstructing polymorphic `Synapse` subtypes from
   raw bytes in `VolatileJournalStorage`) also works with zero manual type registration.

Both the write path and the read/reactivation path matter: `JournalJsonContext` today has to cover both
directions (`JsonSerializer.Serialize`/`Deserialize`), so a finding that only proved the write side would
be incomplete evidence for Task 8.

## Result

**PASS** — native format (`JournalFormatKey = "orleans-binary"`) round-trips a `Synapse` subtype through a
real (non-fake) Orleans.Journaling pipeline, across an actual grain deactivation/reactivation, with **no
manual per-type registration** of any kind. `DemoMessageSynapse` and `NeuronActivated` were never listed
anywhere for this format — unlike `JournalJsonContext`, which currently lists all 126 `Synapse` subtypes by
hand for `UseJsonJournalFormat`. This is consistent with `orleans-binary` reusing Orleans' own
`[GenerateSerializer]`-based codec pipeline (already present on every `Synapse` subtype for grain-to-grain
messaging) rather than System.Text.Json source generation, which is what actually needs the manual
`[JsonSerializable(typeof(X))]` list.

TDD evidence — 5 consecutive runs, all green:

```
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 980 ms - DigitalBrain.Tests.dll (net11.0)
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 996 ms - DigitalBrain.Tests.dll (net11.0)
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 978 ms - DigitalBrain.Tests.dll (net11.0)
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 973 ms - DigitalBrain.Tests.dll (net11.0)
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 973 ms - DigitalBrain.Tests.dll (net11.0)
```

## Decision

Task 8 proceeds with **native-format deletion of `JournalJsonContext`**: switch production's
`AddAzureBlobJournalStorage(...)` wiring from `.UseJsonJournalFormat(JournalJsonContext.Default)` to
`.ConfigureServices(s => s.Configure<JournaledStateManagerOptions>(o => o.JournalFormatKey = "orleans-binary"))`,
then delete `DigitalBrain.Kernel/JournalJsonContext.cs` and its dedicated coverage test
(`DigitalBrain.Tests/Protocol/JournalJsonContextTests.cs`).

## Caveats for Task 8 to carry forward

- **"Legacy" framing.** Context7's official Orleans docs call `orleans-binary` the legacy/compat format
  and steer new adopters toward JSON. This spike only proves it *works* for our shape of data today, not
  that it is a supported long-term direction upstream. Task 8 should note this risk explicitly (e.g. a
  comment at the `JournalFormatKey` call site) rather than treating it as a permanent guarantee.
- **Format is per-silo config, not per-entry.** `JournalFormatKey` is set once on `JournaledStateManagerOptions`
  for the whole silo. Existing Azure Blob journal data written with the JSON format will not automatically
  convert; per the docs, a grain that reads data written in a different format than its configured write
  format is forced to a full-snapshot rewrite on its next write. Task 8 needs a plan for any already-persisted
  JSON journal data (fresh environments are unaffected; this prototype currently has no production users, but
  worth a one-line note).
- **This spike used `VolatileJournalStorage`, not Azure Blob.** The storage *provider* (Azure Blob vs.
  volatile/in-memory) is orthogonal to the format question tested here — `JournalFormatKey` is read by the
  generic `IJournaledStateManager`/format-resolution layer, not by the storage provider. Task 8 should still
  do one real run against Azurite/Azure Blob storage as a sanity check before calling the migration done,
  since this spike deliberately avoided that dependency for speed.
- **Unrelated gap noticed in passing, not touched here:** `DigitalBrain.Kernel/Program.cs`'s Aspire-hosted
  (cloud) path calls `siloBuilder.AddAzureBlobJournalStorage(...).UseJsonJournalFormat(...)` but never
  registers keyed `IDurableList<Synapse>` services for `"in-journal"`/`"out-journal"` the way
  `ConfigurePrototypeJournals()` does for the fast path — `NeuronJournals`'s `[FromKeyedServices(...)]`
  constructor parameters must be resolving some other way in that path (this spike's own
  `NativeFormatSiloConfigurator` also does not register them manually and resolves fine, so `.AddJournalStorage()`
  evidently supplies keyed `IDurableList<T>` for arbitrary keys automatically). Flagging this only because it
  was observed while probing the API; it is out of scope for Task 1 and not a blocker for Task 8.
