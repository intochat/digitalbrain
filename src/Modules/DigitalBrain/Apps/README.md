# Apps

Shared C# apps (packages), their marketplace directory, and packages installed into workspaces as apps. The module also keeps first-party app manifests, the per-workspace catalog and consent for the existing app surfaces.

## Compiled applications

`DigitalBrain.Apps.IApplication` declares module requirements and startup steps through `IAppBuilder`.
`AppDefinition` captures that declaration; `ApplicationCatalog` starts it for a workspace-scoped key.
These types and their hosting extensions live in this module. Kernel only composes modules and has
no dependency on applications. `WithApp` adds missing requirements while preserving modules already
configured by the host. `DigitalBrain.Modules.Apps.Testing` provides the unit-test `WithApp`
extension, which also registers the application catalog without adding Apps dependencies to the
base test harness.

The Assistant in `src/Apps/Assistant` declares the main conversation surface with the application
builder. Assistant neurons own drafts, conversation/model selection and the presentation state;
Flutter renders the declared tree with generic neuron components. No app-specific Dart renderer
or client turn controller is required. IntoChat registers the app and exposes authenticated
transport. `IAssistant.Run` owns turn execution, replay, cancellation, conversation history,
tool selection, model calls, metering and receipts. `IAssistant.Transcribe` owns voice validation
and transcription. `OpenWindow` starts a distinct app instance and opens its surface in the
workspace. Application services are registered through `IAppBuilder.ConfigureServices`. Package apps below retain their
existing installation, publishing, consent, and verification lifecycle.

## Packages

A package is a shareable single-file C# app addressed as `owner/name`, where the owner is the publishing account's username. `IPackage` holds content-addressed revisions: a revision id hashes its parents and content, so forks share ancestry.

A revision is its manifest and source. Nothing compiles it at commit time: a broken script shows up as an `Exited` file with its compiler output in the logs.

| Git | Package |
| --- | --- |
| commit | `Commit` (optimistic on `ExpectedHead`, idempotent per operation id) |
| fork | `Fork` on the new, empty package: copies the source lineage and records `ForkedFrom` |
| pull | `Pull` fast-forwards; a diverged fork reconciles with `Commit` and `MergeFrom` |
| pull request | `Propose` on upstream; `Accept` fast-forwards when upstream's head is in the proposal's ancestry |
| release | `Publish` sets the stable revision and refreshes `IPackageDirectory` |

Reads are public. Changes require the stamped caller to own the package. Proposals require the caller to own the proposing fork.

## Installed apps

`IApp : INeuron` is one package revision installed in one workspace. Scripts run in containers and cannot host neurons, so the app is the script's address in the brain:

- `Install`, `Configure`, `Upgrade` write the revision's source into a fresh `ICSharpFile`, configure it and start it. The script reads `brain.Setting("App")` for the app's key, `brain.Setting(name)` for each declared setting and `brain.Setting("Account__" + slot)` for each connected account. Configuring declared settings customizes a package without forking it.
- `Invoke` stores a pending invocation and publishes `AppInvoked`. The script answers with `Respond`. `Pending` returns work that arrived before the script subscribed, because signals are not durable.
- Each generation runs in its own file; the previous one is deleted, which stops its container.

A package script:

```csharp
#:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
using DigitalBrain.Apps;
using DigitalBrain.Apps.Signals;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var app = brain.Get<IApp>(brain.Setting("App")!);
await using var invocations = await brain.SubscribeAsync<AppInvoked>(app, brain.Stopping);
foreach (var missed in await app.Pending()) { await app.Respond(Answer(missed.Id, missed.Input)); }
await foreach (var invoked in invocations.ReadAllAsync(brain.Stopping)) { await app.Respond(Answer(invoked.InvocationId, invoked.Input)); }
```

## Runtimes, scenarios and verification

A package's manifest names its `Runtime`. `csharp` (the default) runs `Source` as above. Any other runtime is
configuration on top of neurons: the host registers an `IAppRuntime` under that name, and `IAppRuntimeWorker`
answers each invocation through it off the app neuron, responding with `Respond` like a script would.
`PackageContent.Files` carries everything else an app is made of: prompts, runtime configuration and its spec.

`app.spec.md` is the app's spec in plain language; `tests.cs` is its proof, a C# file-based app that
installs scratch copies of the revision, drives them through contracts and prints one
`dbtest:pass <name>` or `dbtest:fail <name>	<message>` line per scenario. `IAppVerification` (keyed
`{owner}/{name}@{revision}`) runs `tests.cs` through `ITestScriptRunner` (by default as an ordinary
sandbox script) and keeps the verdicts. `Publish` refuses a revision that carries a spec or tests
until its verification is green. A csharp app's programs are its `Source` plus every
`behaviors/*.cs` file; each installs as its own `ICSharpFile`.

## Sharing from the C# console

IntoChat's `POST /brains/{brainId}/csharp/{id}/share` commits the file's current source to `{you}/{name}` and publishes it. The package's title and description come from the file's name and purpose, and the request may declare account slots. Settings are never shared. Sharing unchanged code again publishes the same revision.

## Trust

Installing a package runs its code in a container with full client access to the brain; the container is not a permission boundary. IntoChat composes the CSharp module only in the developer profile. Its package routes need `IntoChat:DeveloperMode`, and running packages needs the host's C# sandbox (composing `CSharpModule` in a repository declares it). A public marketplace needs per-app identities first.

## Tests

```powershell
dotnet test --project src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Tests.Unit/DigitalBrain.Modules.Apps.Tests.Unit.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Tests.E2E/DigitalBrain.Modules.Apps.Tests.E2E.csproj -p:CodeGraphRefresh=false
```

The E2E runs real package scripts in .NET SDK containers, so it needs a Docker daemon. IntoChat's `Packages/PackageSharingFacts` drives the same journey over HTTP with two registered accounts.
