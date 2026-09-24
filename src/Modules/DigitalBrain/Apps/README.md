# Apps

Shared behaviors (packages), their marketplace directory, and packages installed into workspaces as apps. The module also keeps first-party app manifests, catalog, consent and proxy for the existing app surfaces.

## Packages

A package is a shareable behavior addressed as `owner/name`, where the owner is the publishing account's username. `IPackage` holds content-addressed revisions: a revision id hashes its parents and content, so forks share ancestry.

A revision is accepted only with a Coding check artifact built from exactly its source and tests. Every revision that anyone installs, forks or accepts has passed its own tests.

| Git | Package |
| --- | --- |
| commit | `Commit` (optimistic on `ExpectedHead`, idempotent per operation id) |
| fork | `Fork` on the new, empty package: copies the source lineage and records `ForkedFrom` |
| pull | `Pull` fast-forwards; a diverged fork reconciles with `Commit` and `MergeFrom` |
| pull request | `Propose` on upstream; `Accept` fast-forwards when upstream's head is in the proposal's ancestry |
| release | `Publish` sets the stable revision and refreshes `IPackageDirectory` |

Reads are public. Changes require the stamped caller to own the package. Proposals require the caller to own the proposing fork.

## Installed apps

`IApp : INeuron` is one package revision installed in one workspace. Behaviors run out of process and cannot host neurons, so the app is the behavior's address in the brain:

- `Install`, `Configure`, `Upgrade` deploy the revision's verified artifact through `IBehaviorProgram` with `Behavior__App` and `Behavior__{name}` configuration, the same keys an automation reads in the behavior console. Configuring declared settings customizes a package without forking it.
- `Invoke` stores a pending invocation and publishes `AppInvoked`. The behavior answers with `Respond`. `Pending` returns work that arrived before the behavior subscribed, because signals are not durable.
- Each installation runs under its own behavior program key, because deleted programs cannot be redeployed.

A package behavior:

```csharp
var settings = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
await BehaviorApp.RunAsync<Researcher>(args, brain => [SubscriptionRequirement.For<AppInvoked>(brain.Get<IApp>(settings["Behavior:App"]!))]);

public sealed class Researcher(IDigitalBrain brain, IConfiguration configuration) : IBehavior
{
    public async Task RunAsync(CancellationToken cancellation = default)
    {
        var app = brain.Get<IApp>(configuration["Behavior:App"]!);
        await using var invocations = await brain.SubscribeAsync<AppInvoked>(app, cancellation);
        foreach (var missed in await app.Pending()) { await app.Respond(Answer(missed.Id, missed.Input)); }
        await foreach (var invoked in invocations.ReadAllAsync(cancellation)) { await app.Respond(Answer(invoked.InvocationId, invoked.Input)); }
    }
}
```

## Sharing from the behavior console

IntoChat's `POST /workspaces/{workspaceId}/behaviors/{id}/share` (the console's "Share as package" button) commits the automation's draft to `{you}/{name}`, using its passing check as the revision's artifact, and publishes it. The package's title and description come from the automation. Its settings are the automation's current `Behavior__*` configuration, and their current values become the published defaults, so never keep secrets there. Sharing unchanged code again publishes the same revision.

## Trust

Installing a package runs its code with the behavior worker's privileges; the worker is not a sandbox. IntoChat composes workers only in the developer profile. Its package routes need `IntoChat:DeveloperMode`, and running packages also needs `IntoChat:BehaviorAuthoring:AllowActivation`. A public marketplace needs an isolated executor first.

## Tests

```powershell
dotnet test --project src/Modules/DigitalBrain/Apps/Tests/Unit/DigitalBrain.Modules.Apps.Tests.Unit.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/DigitalBrain/Apps/Tests/E2E/DigitalBrain.Modules.Apps.Tests.E2E.csproj -p:CodeGraphRefresh=false
```

The E2E compiles real package behaviors and runs them in workers. IntoChat's `Packages/PackageSharingFacts` drives the same journey over HTTP with two registered accounts.
