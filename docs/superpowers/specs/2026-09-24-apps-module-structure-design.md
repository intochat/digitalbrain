# Apps module structure

Date: 2026-09-24
Status: Proposed structure approved in conversation; written spec awaiting review.
Baseline: a039245d4 (workspace app runtime and account session flows).

## Outcome

Move `src/Modules/Apps` to `src/Modules/DigitalBrain/Apps`, beside Kernel and Behaviors. Organize contracts, implementation, and existing tests by capability, following the repository's Time and Flutter module layouts. This is a structural refactor with unchanged runtime behavior and public identities.

Codegraph exploration identified a flat contracts folder and a Runtime folder combining composition execution, workspace installation, activation, and durable receipts. It reported 88 callers of AppManifest, including catalog, discovery, marketplace, and application consumers. Keeping existing public names limits the scope of this refactor.

## Structure

```text
src/Modules/DigitalBrain/
  Kernel/
  Behaviors/
  Apps/
    README.md
    Contracts/
      DigitalBrain.Modules.Apps.Contracts.csproj
      Manifests/
      Composition/
      Catalog/Signals/
      Consent/Signals/
      Proxy/
      Workspace/Signals/
      Lifecycle/Signals/
    Apps/
      DigitalBrain.Modules.Apps.csproj
      AppsModule.cs
      Configuration/
      Manifests/
      Composition/
      Catalog/
      Consent/
      Proxy/
      Workspace/Receipts/
      Lifecycle/
    Tests/Unit/
      DigitalBrain.Modules.Apps.Tests.Unit.csproj
      Manifests/
      Composition/
      Registration/
      Workspace/
```

Folders exist only when populated. Existing tests move by responsibility; the tree does not require new tests for every directory. Keep the three existing projects, their filenames, package and assembly identities, and dependencies.

## File ownership

Paths in this section are relative to the old or new Apps module root.

| Current source | Destination and responsibility |
| --- | --- |
| Contracts/AppManifest.cs | Contracts/Manifests/: manifest and supporting declaration types |
| Contracts/AppManifestJson.cs | Contracts/Manifests/: wire serialization |
| Contracts/AppManifestException.cs | Contracts/Manifests/: existing validation error type |
| Contracts/ManifestValidator.cs | Contracts/Manifests/: manifest validation |
| Contracts/AppComposition.cs | Contracts/Composition/: graph, parts, bindings, activation policy; AppStatus belongs in Contracts/Workspace/ |
| Contracts/AppCompositionValidation.cs | Contracts/Composition/: graph validation |
| Contracts/AppCatalog.cs | Contracts/Catalog/: installation, save-as-app, scoped directory records and interfaces |
| Contracts/Consent.cs | Contracts/Consent/: review and approval contracts |
| Contracts/AppProxy.cs | Contracts/Proxy/: proxy contracts |
| Contracts/AppRuntime.cs | Contracts/Workspace/: IWorkspaceApp, AppSnapshot, AppDispatch; signals in Workspace/Signals/ |
| Contracts/WorkspaceLifecycle.cs | Contracts/Lifecycle/: activation interface and snapshot; signal in Lifecycle/Signals/ |
| Contracts/Signals/AppSignals.cs | Split AppInstalled, AppUninstalled, AppRolledBack, and AppCatalogued into Catalog/Signals/; AppConsentApproved into Consent/Signals/ |
| Apps/Runtime/AppBehaviorRegistry.cs | Apps/Composition/: separate IAppBehavior, AppBehaviorRegistry, and TextAppBehavior files |
| Apps/Runtime/WorkspaceAppNeuron.cs | Apps/Workspace/: installed app execution and state |
| Apps/Runtime/WorkspaceApps.cs | Apps/Workspace/: installation and workspace orchestration interface |
| Apps/Runtime/AppOperationReceiptNeuron.cs | Apps/Workspace/Receipts/: internal receipt interface, state and persistence implementation |
| Apps/Runtime/WorkspaceLifecycleNeuron.cs | Apps/Lifecycle/: durable workspace activation |
| Apps/Catalog/, Apps/Consent/, Apps/Proxy/ | Retain these feature folders and their responsibilities |
| Apps/Manifests/ | Retain first-party loading, manifest generation and discovery source adapter |
| Apps/Configuration/, Apps/AppsModule.cs | Retain module composition and registration ownership |
| Tests/Unit/ManifestFacts.cs | Tests/Unit/Manifests/ |
| Tests/Unit/AppRegistrationFacts.cs | Tests/Unit/Registration/ |
| Tests/Unit/WorkspaceAppFacts.cs | Tests/Unit/Workspace/; independently scoped composition-validation facts may move to Composition/ while preserving fixtures and assertions |

Split bundled public contract declarations into files named after their types. Keep tightly coupled internal state declarations with their implementation unless extraction improves navigation. Preserve all type and member attributes, comments, accessibility and behavior during extraction.

## Module relationships and behavior

Contracts remain the consumer interface. The implementation retains its dependencies on Apps contracts, Kernel and Discovery contracts. Folder placement under DigitalBrain does not add a Kernel-to-Apps dependency.

WorkspaceApps continues coordinating installation, catalog projection and activation. WorkspaceAppNeuron continues owning execution and state; operation receipts remain internal and durable. Composition continues providing graph validation and registered behavior execution. Lifecycle continues publishing durable activation generations. Catalog, consent, proxy and first-party discovery retain their current flows.

No changes to retry handling, failure propagation, validation ordering, persistence, authorization, endpoint routes or configuration semantics are included.

## Compatibility constraints

- Preserve existing namespaces even where they differ from new folder names. In particular, existing DigitalBrain.Apps.Signals types retain that namespace; runtime signals retain their current namespace.
- Preserve assembly/package names, serializer aliases and field IDs, grain aliases and types, storage keys, registration order and dependency injection lifetimes.
- Keep embedded manifest logical resource names byte-for-byte unchanged.
- Keep the concrete app packages in `src/Apps` and IntoChat endpoint and built-in app code in the application project.
- Keep Kernel's App base type in Kernel. This refactor does not redesign its relationship to Apps or Behaviors.
- Add no forwarding wrappers, compatibility projects, extra packages or speculative interfaces.

## Relocation integration

Update the Apps solution folder to `/Modules/DigitalBrain/Apps/` and update all three project paths. Recalculate outgoing relative references in moved projects and incoming references from IntoChat, Discovery, Broker, Marketplace and any other discovered consumers. Preserve project-reference metadata.

The implementation project's six embedded `src/Apps/*/app.json` paths gain one parent-directory level. References from the moved tests to shared Testing projects also gain one level. Kernel references now resolve through sibling `../Kernel` paths at the appropriate project depth.

Search maintained source, build configuration, scripts and active documentation for old module paths and update executable references and navigation links. Historical design documents may retain old paths when describing the baseline. Replace the Apps README's obsolete orchestration-skeleton text with current responsibilities, folder guidance and the relocated test command.

## Verification and acceptance

The baseline Apps unit suite passed 16 tests before the checkpoint commit. This is a baseline, not evidence for the future refactor.

Acceptance requires:

1. All Apps source is under the new root with no maintained build references to the old module location.
2. All changed project-reference and embedded-resource paths resolve, and solution entries point to existing projects.
3. The solution builds, covering downstream consumers and unchanged namespace compatibility. Any environmental blocker is reported separately from refactor failures.
4. Apps unit tests pass from the relocated project, preserving all existing coverage.
5. Discovery, Marketplace, Broker and IntoChat unit suites pass where present, exercising manifest consumers and integration registrations.
6. Existing manifest tests verify first-party loading from the embedded resources. Add a focused regression assertion only if existing coverage does not establish this property.
7. Review the diff to confirm changes are file relocation, declaration extraction, reference corrections and documentation; no behavior or public identity changes.
8. Whitespace checks pass and final status reports the actual checks run and any unverified E2E coverage.

## Alternatives considered

A root-only move leaves the flat contracts and mixed Runtime folder unresolved. Separate projects per capability add project overhead without a demonstrated packaging need. Mirrored feature folders within the existing projects provide the requested navigability while keeping dependencies and consumer usage stable.
