# Apps

Shared C# apps (packages), their marketplace directory, and packages installed into workspaces as apps. The module also keeps first-party app manifests, the per-workspace catalog and consent for the existing app surfaces.

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
#:project /brain/src/Modules/DigitalBrain/Apps/Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
using DigitalBrain.Apps;
using DigitalBrain.Apps.Signals;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var app = brain.Get<IApp>(brain.Setting("App")!);
await using var invocations = await brain.SubscribeAsync<AppInvoked>(app, brain.Stopping);
foreach (var missed in await app.Pending()) { await app.Respond(Answer(missed.Id, missed.Input)); }
await foreach (var invoked in invocations.ReadAllAsync(brain.Stopping)) { await app.Respond(Answer(invoked.InvocationId, invoked.Input)); }
```

## Sharing from the C# console

IntoChat's `POST /workspaces/{workspaceId}/csharp/{id}/share` commits the file's current source to `{you}/{name}` and publishes it. The package's title and description come from the file's name and purpose, and the request may declare account slots. Settings are never shared. Sharing unchanged code again publishes the same revision.

## Trust

Installing a package runs its code in a container with full client access to the brain; the container is not a permission boundary. IntoChat composes the CSharp module only in the developer profile. Its package routes need `IntoChat:DeveloperMode`, and running packages also needs `IntoChat:CSharp:AllowActivation`. A public marketplace needs per-app identities first.

## Tests

```powershell
dotnet test --project src/Modules/DigitalBrain/Apps/Tests/Unit/DigitalBrain.Modules.Apps.Tests.Unit.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/DigitalBrain/Apps/Tests/E2E/DigitalBrain.Modules.Apps.Tests.E2E.csproj -p:CodeGraphRefresh=false
```

The E2E runs real package scripts in .NET SDK containers, so it needs a Docker daemon. IntoChat's `Packages/PackageSharingFacts` drives the same journey over HTTP with two registered accounts.
