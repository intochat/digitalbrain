# Behavior packages

Date: 2026-09-24
Status: Option A approved by the owner ("users should be able to share their behaviors").
Baseline: 024911a4c on `delivery/integration`; work on `feature/behavior-packages`.

## Outcome

A user can publish a programmable behavior, anyone can install it into a workspace in one step,
fork it, change it, pull upstream work, and propose the change back. Packages are the only
distribution unit: first-party and user behaviors share the same format and flow.

The toy composition runtime (`AppComposition`, `text.*` parts, `WorkspaceAppNeuron`, `/app-runtime`)
is deleted. Kernel's `App<TState>` alias is deleted; built-in apps derive from `Neuron<TState>`.

## Model

```text
IPackage  "alice/researcher"            shared, public-read, owner-written
  revisions: id = sha256(parents + content), each carries the passing check artifact
  head, published, forkedFrom, proposals

IPackageDirectory "packages"            marketplace index of published packages

IApp  "{workspace-scoped key}"          one installed package revision in one workspace
  settings (declared by the manifest), invocations, status
  drives IBehaviorProgram "{app key}/behavior"
```

A package revision exists only if its exact source and tests passed the Coding check: `Commit`
opens the artifact through `ICodeArtifactStore` and requires the artifact's source and tests to
equal the committed content. Tests are the gate for commits, forks, merges and contributions.

### Git mapping

| Git | Package |
| --- | --- |
| commit | `Commit` with the author's verified artifact |
| fork | `Fork` on the new, empty target package, copying the source revision's lineage |
| pull (fast-forward) | `Pull` when the local head is an ancestor of the source revision |
| merge commit | `Commit` with `MergeFrom`: parents are the head and the merged revision |
| pull request | `Propose` on upstream; `Accept` fast-forwards when upstream head is an ancestor |
| release | `Publish` points the stable revision and refreshes the directory listing |

Revision ids are global content hashes, so forks share ancestry and accepting a proposal is a
fast-forward to the contributor's revision. A diverged proposal is refused until the contributor
commits a merge.

### Installed app

`IApp : INeuron` is the package's presence in the brain. Behaviors run out of process and cannot
host neurons, so the app neuron gives them an address, settings and an invocation surface.

- `Install(revision, settings)` validates settings against the manifest, deploys the revision's
  artifact through `IBehaviorProgram` with configuration `Behavior__App` and
  `Behavior__{name}` (a setting may not be named `App`).
- `Configure` redeploys the same artifact with new settings: customization without a fork.
- `Upgrade` redeploys another revision of the same package.
- `Invoke(operation, input)` stores a pending invocation and publishes `AppInvoked`.
  `Respond` completes it; `Pending` lets a restarted worker drain work it missed, because signals
  are not durable.

A behavior in a package:

```csharp
var app = brain.Get<IApp>(configuration["Behavior:App"]!);
await using var calls = await brain.SubscribeAsync<AppInvoked>(app, cancellation);
foreach (var missed in await app.Pending()) { await Handle(missed); }
await foreach (var call in calls.ReadAllAsync(cancellation)) { await Handle(call); }
```

## Identity and trust

- Package owner is the authenticated `CallerContext.PrincipalId` (identity usernames:
  `^[a-z0-9][a-z0-9-]*$`, at most 80). Names use `^[a-z0-9][a-z0-9-]{0,63}$`.
- Writes (`Commit`, `Fork` target, `Pull`, `Accept`, `Publish`) require the stamped caller to own
  the package. `Propose` requires the caller to own the proposed source package. Reads are public.
- Installing runs the package's code with the behavior worker's privileges. Behavior workers are
  composed only in the developer profile and gated by `IntoChat:DeveloperMode`; a sandboxed
  executor is a prerequisite for a public product marketplace and is out of scope.

## Limits

256 revisions per package, 64 open proposals, 256 retained invocations per app, 32 settings of at
most 4096 characters, manifest title 100 and description 2000 characters, 32 operations.

## Surfaces

HTTP (IntoChat, developer mode, authenticated):

- `GET /packages`, `GET /packages/{owner}/{name}`, `GET /packages/{owner}/{name}/revisions/{id}`
- `POST /packages/{owner}/{name}/revisions` — draft check then commit
- `POST /packages/{owner}/{name}/fork`, `/pull`, `/proposals`, `/proposals/{number}/accept`, `/publish`
- `/workspaces/{workspaceId}/packages/{owner}/{name}`: `GET`, `POST` install, `POST /configure`,
  `POST /upgrade`, `DELETE`, `POST /invocations`, `GET /invocations/{id}`

## Verification

- Unit: package commit gating, lineage (fork, pull, merge, propose, accept, divergence), publish
  and directory, app install/configure/upgrade/invoke/respond against a recording behavior program.
- E2E (Apps module, real Coding check and behavior worker): Alice publishes, Bob installs and
  invokes, customizes a setting, forks and changes code, proposes; Alice accepts and publishes;
  Bob upgrades to the accepted revision.
- E2E (IntoChat, HTTP, two registered accounts): the same journey through the product routes.
