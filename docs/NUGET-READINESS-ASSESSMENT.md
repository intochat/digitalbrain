# Package and repository readiness

Updated 2026-10-03 for PR #130.

## Boundaries

- `DigitalBrain` contains neuron and signal abstractions; `DigitalBrain.Contracts` contains shared contracts.
- `DigitalBrain.Client` is the script-edge HTTP client. `DigitalBrain.Client.Orleans` adds the in-cluster client.
- `DigitalBrain.Kernel` implements the runtime; `DigitalBrain.Kernel.AspNetCore` contains HTTP enforcement.
- `DigitalBrain.Platform.Contracts` exposes identity and credential contracts. `DigitalBrain.Platform` owns their privileged implementation.
- `DigitalBrain.Sdk` provides module authoring support.
- `DigitalBrain.Aspire.Hosting`, `.Server`, and `.Client` compose the AppHost, server, and client respectively.
- Executable tests live in lowercase `tests/` folders near their domain. Reusable `DigitalBrain.Testing*` libraries remain under `src/Testing`.

The physical and solution layout is documented in [Repository layout](REPOSITORY-LAYOUT-PROPOSAL.md).
All 116 original projects remain; two integration suites bring the solution to 118 projects.
Project directories, project names, assembly/package identities and root namespace metadata agree.
Public source namespaces remain flat by repository convention. Serialized aliases and existing field IDs are preserved.

## Automated gates

`DigitalBrain.Architecture.Tests` checks compiled dependency boundaries, contracts, serialized
state compatibility, and solution/disk project coverage. It rejects duplicate projects, broken
project references, mismatched solution groups and misplaced test projects.

`DigitalBrain.Aspire.Hosting.Tests.E2E` packs its real dependency closure into an isolated feed.
It checks package assets and dependency boundaries, builds separate contracts/client/server/unit-testing
consumers, and runs a package-only AppHost/server/client composition. The runtime scenarios cover
in-memory hosting, two-brain isolation and resource renaming with the original storage and service ID.
The consumer is outside the repository and has no ProjectReferences or inherited repository build files.

`DigitalBrain.Platform.Tests.E2E` uses disposable Azurite storage and separate host processes.
It checks inspection without writes, termination after the plan checkpoint and after an account write,
rejection of incomplete startup, resume, revocation across restart and migration retry, original-key
reuse, startup rejection for a changed key or service ID, and byte-for-byte snapshot restoration.
The source data comes from frozen compatibility fixtures captured before the identity refactor.

These are ordinary xUnit projects discovered by the solution's `dotnet test` step. CI builds and
packs before running suites sequentially. Tests own their temporary storage, enforce deadlines,
retain diagnostics on failure, and require neither live-provider credentials nor production snapshots.
There is no separate release rehearsal framework or publishing script.

## Fixes from review

- Removed stale Memory registrations from the product AppHost and container manifests. Qdrant remains.
- Separated the ClickHouse server resource name from its module node to prevent Aspire startup failure.
- Kept Azure blob-container names independent of Aspire resource prefixes, preserving valid names and storage identity after resource renaming.
- Added membership-validated shared-brain session selection and an authenticated HTTP regression test.
- Replaced source-code allowlist assertions and duplicate profile cases with a real product composition check.
- Updated test provider parameters and resource lookup to follow brain-scoped Aspire names.
- Application-host fixtures use stable module IDs and replace configured module options, so disabled settings and empty collections cannot leave live resources enabled. Ordinary host configuration still overlays code defaults; `DigitalBrain:Modules:<id>:ReplaceOptions=true` explicitly starts from type defaults instead.
- Removed redundant reads of the last immutable compute part while preserving earlier roots and the existing E2E trace budget.
- Made the wrong-key integration scenario require rejection during startup, preventing a later decryption failure from masking a broken startup guard.

## Deployment requirements

A production upgrade still needs the original master key, stable service ID and storage, a stopped-writer
snapshot, a complete legacy grant inventory, and explicit maintenance inspection/apply. Tests on synthetic
data do not establish readiness for an arbitrary production snapshot. See [Identity upgrade](IDENTITY-UPGRADE.md).

Memory data has no automatic Qdrant migration. Removing the Memory module removes its API and state owners;
operators with existing Memory data must plan that transition explicitly.

Package publication and PR merge remain manual approval steps. Current run results belong in the PR
validation summary; historical release-script results are not current gates.
