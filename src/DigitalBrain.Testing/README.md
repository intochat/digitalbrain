# DigitalBrain.Testing

Test helpers for DigitalBrain neurons and modules. No runtime is referenced.

## Fakes

- `NeuronHarness.Create<TNeuron>(id)` attaches a `FakeNeuronContext` to a new neuron and runs its activation.
- `FakeNeuronContext` records published signals (`Published`) and exposes the fakes below.
- `InMemoryNeuronState`, `ManualTimers`, `ManualReminders`, `RecordingSynapses`, `FakeOperations`,
  `FakeNeuronProxies`, `FakeCapabilityBroker` and `ManualTime` stand in for the runtime services.
- `harness.AssertPublished<TSignal>(predicate)` fails with the list of recorded signals.

## In-process host

`SiloHost.Local()` starts a whole brain host inside the test process. The host itself ships in the
`DigitalBrain.Runtime.Orleans` package; a test project that calls `SiloHost.Local()` must reference it.
Without that reference the call fails with a message naming the package.

## TestOS

`TestOS.Local()` hosts neurons and apps in the test process, with no Orleans, no network and a clock that moves only when told
([TestOS.cs](TestOS/TestOS.cs)). Turns run one at a time per neuron; `[On]` reactions arrive ordered per source and deduped.

```csharp
await using var os = TestOS.Local()
    .With<IStorageLike>("drive", new FakeStorage("drive"))
    .With<IStorageLike>("c", new FakeStorage("c", used: 10, total: 100));
var files = await os.Install<Files>();

await files.Request("open");
await os.Idle();

Assert.Contains(files.Side.Entries, e => e.Id == "c" && e.Trailing == "10 GB of 100 GB");
```

### Capability fakes

- `With<TCapability>(name, fake)` binds a fake to a `[Capability(name, version)]` interface; `With<TCapability>(fake)` uses the
  capability name. A second binding of the same contract and name throws, and so does a contract without `[Capability]`.
- Neurons and apps take bound fakes through their constructors: `TCapability` for the only binding, `IReadOnlyList<TCapability>`
  for all of them. `ICapabilityBroker` resolves from the same fakes.
- Nothing resolves unless it is bound: an unbound capability throws `CapabilityNotBoundException` naming `name@version`
  ([CapabilityFakes.cs](TestOS/Capabilities/CapabilityFakes.cs)).
- Capability-specific helpers such as `WithStorage` ship with their contracts as extension methods over `With<TCapability>`.

### Installing an app

- `os.Install<TApp>()` reads the app's manifest: its static `AppManifest Manifest` property, or else the `[App]` id, the capability
  contracts its constructor takes and the `[Neuron]` classes in its namespace. A required capability with no bound fake fails the
  install with the capability named, before anything is registered ([TestOSAppExtensions.cs](TestOS/Apps/TestOSAppExtensions.cs)).
- `app.Request(operation, argument)` runs the `[On]` method of that name (case-insensitive) as a turn. A null argument is the empty
  input. `app.Operations` lists the operations, so a baseline test can request each one.
- `os.Idle()` completes when no request, turn, reaction or deferred work is left.

### Presentation

An app publishes through the `DigitalBrain.UI.IPresenter` it takes in its constructor, or through public `Side` and `Main` properties
that are read after every turn. The latest of each wins ([PresentationAssertions.cs](TestOS/Presentation/PresentationAssertions.cs)).

- `app.Side` and `app.Main` are `PresentedView`s: `IsSet`, `Title`, `Entries`, `Texts`, `Fields`, `Ids`, `Find(id)` and `Entry(id)`.
- `app.Toasts` lists the toast messages; `app.Presentation` is the raw `AppPresentation`; `app.PresentationError` holds what a
  `Side` or `Main` property threw.
- `Find` throws `PresentationAssertionException` listing the ids that exist. The accessors return plain values, so any assertion
  library works.

## App baseline

`AppBaseline` is the generated baseline test of an app (D-04): a theory with one row per `[On]` operation, named `app/operation`.

```csharp
public static IEnumerable<TheoryDataRow<AppOperationCase>> Operations => AppBaseline.Cases(typeof(Notes));

[Theory, MemberData(nameof(Operations))]
public Task Baseline(AppOperationCase operation) => AppBaseline.RunAsync(operation);
```

Each row installs the app into a fresh `TestOS`, binds a fake for every required capability (`storage@1` and `identity@1` have defaults; pass others with `AppBaseline.Cases(typeof(X), os => os.With<IMail>(fake))`), opens the app, requests the operation with the empty input, and fails with `AppBaselineException` naming the app and the operation when a capability has no fake, the app throws or records an error state, a view throws, nothing is presented, or the row takes more than 5 s. Script apps use the `Cases(manifest, operations, install, fakes)` overload with an `IInstalledApp`.

## Storage: conformance kit and fake

A module providing storage@1 proves it in its `tests/` project with a sealed subclass of
`DigitalBrain.Testing.Conformance.Storage.StorageConformance<TStorage>` ([StorageConformance.cs](Conformance/Storage/StorageConformance.cs)):

- `Connect(StorageSeed seed, ct)` returns a `StorageSubject(storage, operations, hostileIds)` whose seed folder (the first root,
  or override `SeedFolder`) holds `StorageSeed.Canonical`: `Notes/a.txt`, `Plan.txt` (bytes 0..15), `Deep/1/2/3/leaf.txt` and
  `Many/` with 250 files. `operations` waits for the operations `Write` starts; `hostileIds` must each fail with `denied`.
- `MissingId()` may be overridden with a module-shaped unknown id (not-found expected).
- Facts for optional actions skip, with a reason, only when the storage's `Describe().Flags` does not offer the action.

`InMemoryStorage` (with `InMemoryOperations`) is the reference implementation that passes the kit. `FakeStorage` is the same
storage plus a `Calls` record; bind it with `os.WithStorage("drive", new FakeStorage("Work Drive"))` or
`os.WithStorage("c", label: "C:", configure: fake => fake.AddFile(fake.Root, "a.txt", bytes))`.

## Secrets and identity: conformance kits and fakes

secrets@1 and identity@1 take no caller parameter; the implementation reads the caller the runtime stamped. Both kits
switch that caller through a `CallerScope` the subclass supplies (`As(KitCaller)` stamps a caller until disposed);
`AmbientCallerScope` is the `AsyncLocal` scope the in-memory subjects read.

- `SecretsConformance<TSecrets>`: `Connect(ct)` returns `SecretsSubject(secrets, callers, capturedLogs)`. Facts cover
  round-trip, redacted references, not-found after remove, cross-brain denied, overwrite, last-writer-wins concurrent
  sets, a 256 KiB value, value-free errors and value-free logs. Override `Owner`/`Stranger` if your callers need
  provisioned ids.
- `IdentityConformance<TIdentity>`: `Connect(ct)` returns `IdentitySubject(identity, grants, callers, signIn?)`. Facts
  cover accounts, brains, idempotent provisioning, registration, membership, owner protection, grants (owner-only grant,
  authorize, revoke, ThisChat `Grant.Covers`) and, when `signIn` is given, the sign-in member.

`InMemorySecrets` and `InMemoryIdentity` are the reference implementations. `FakeSecrets` and `FakeIdentity` add a
`Calls` record; bind them with `os.WithSecrets("vault")`, `os.WithSecrets("vault", fake => fake.Put("owner", "key", "Key", "value"))`,
`os.WithIdentity("identity")` (both halves under one name) or `os.WithIdentity(name, identity, grants)`.
